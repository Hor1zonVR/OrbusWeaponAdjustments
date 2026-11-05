using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using UnityEngine;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Bounded read-only observation of the vanilla adjustment flow. Trigger markers
/// are inferred input edges, NOT verified game adjustment callbacks.
/// </summary>
public sealed class LiveAdjustmentCapture : MonoBehaviour
{
    public LiveAdjustmentCapture(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    private const int Ctrl = 0x11, Alt = 0x12, R = 0x52, M = 0x4D;
    private const int MaxEvents = 3500, MaxWatched = 110, MaxScan = 30000;
    private const float Timeout = 90f;
    private static readonly string[] Terms =
    { "weapon", "wand", "adjust", "offset", "grip", "held", "handle",
      "mage", "hand", "controller", "equip", "settings", "powerui", "pointer" };

    private sealed class Watch
    {
        public Transform Transform = null!;
        public string Path = "";
        public int Rank;
        public Vector3 FirstPos, LastPos;
        public Quaternion FirstRot, LastRot;
        public bool FirstActive, LastActive;
    }
    private readonly List<Watch> _watched = new();
    private readonly List<string> _events = new();
    private bool _armed, _busy, _wasHotkey, _wasMark;
    private bool _leftKnown, _rightKnown, _leftDown, _rightDown;
    private float _started, _nextInput, _nextSample, _rescanAt = -1, _firstTrigger = -1;
    private int _presses, _samples, _overflow, _manualMarks, _rescanCount;
    private uint _rawButtons;

    private static bool Key(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool Has(string s, string part) =>
        s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool Relevant(string s) => Terms.Any(t => Has(s, t));
    private static string Clean(string s) => s.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        Transform? node = t;
        for (int i=0; i<11 && node != null; i++,node=node.parent)
        {
            string name = Clean(node.gameObject.name);
            parts.Add(name.Length > 70 ? name[..70] : name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
    private static string V(Vector3 p) => $"({p.x:0.00000},{p.y:0.00000},{p.z:0.00000})";
    private static string Q(Quaternion r) => $"({r.x:0.00000},{r.y:0.00000},{r.z:0.00000},{r.w:0.00000})";
    private void Add(string s)
    {
        if (_events.Count < MaxEvents)
            _events.Add($"{Time.realtimeSinceStartup - _started:000.000}s {s}");
        else _overflow++;
    }
    private void Start()
    {
        Plugin.ModLog.LogInfo("[WeaponAdjustments] Ctrl+Alt+R arms/stops live capture; Ctrl+Alt+M adds a timeline marker.");
        Plugin.ModLog.LogWarning("[WeaponAdjustments] Trigger markers are inferred; Orbus start/commit callbacks remain unknown.");
    }
    private void Update()
    {
        bool modifiers = (!Plugin.RequireFocus.Value || Application.isFocused)
            && Key(Ctrl) && Key(Alt);
        bool hotkey = modifiers && Key(R);
        bool marker = modifiers && Key(M);
        if (marker && !_wasMark && _armed && !_busy)
            Add("USER MARK #" + (++_manualMarks) + " (Ctrl+Alt+M)");
        _wasMark = marker;
        if (hotkey && !_wasHotkey && !_busy)
        {
            if (_armed) End("manual stop");
            else Begin();
        }
        _wasHotkey = hotkey;
        if (!_armed || _busy) return;
        float now = Time.realtimeSinceStartup;
        if (now - _started >= Timeout) { End("90-second timeout"); return; }
        if (now >= _nextInput)
        {
            _nextInput = now + 0.05f;
            ReadInputs();
        }
        if (!_armed) return;
        if (_rescanAt > 0 && now >= _rescanAt)
        {
            _rescanCount++;
            _rescanAt = _rescanCount < 3 ? now + 4.0f : -1;
            Scan();
        }
        if (now >= _nextSample)
        {
            _nextSample = now + 0.10f;
            Sample();
        }
    }
    private void Begin()
    {
        _busy = true;
        try
        {
            _events.Clear(); _watched.Clear();
            _leftKnown = _rightKnown = _leftDown = _rightDown = false;
            _presses = _samples = _overflow = _manualMarks = _rescanCount = 0;
            _rawButtons = 0;
            _firstTrigger = -1;
            _rescanAt = Time.realtimeSinceStartup + 1.5f;
            _started = Time.realtimeSinceStartup;
            _nextInput = _nextSample = _started;
            ProbeXrTriggerApi();
            Add("ARMED: press vanilla Weapon Adjustment button, move/rotate wand, confirm with trigger.");
            Add("XR input capability: " + _xrTriggerApiStatus);
            Scan();
            _armed = true;
            Plugin.ModLog.LogInfo("[WeaponAdjustments] Live capture ARMED; watched=" + _watched.Count);
        }
        catch (Exception e)
        {
            _armed = false;
            Plugin.ModLog.LogError("[WeaponAdjustments] Unable to arm live capture: " + e);
        }
        finally { _busy = false; }
    }
    private void Scan()
    {
        // Only live active scene transforms. The old FindObjectsOfTypeAll scan
        // selected inactive prefabs and hundreds of repeated other-player wands.
        var known = new HashSet<int>();
        foreach (Watch w in _watched)
            if (w.Transform != null) known.Add(w.Transform.GetInstanceID());
        var candidates = new List<Watch>();
        int inspected = 0, relevant = 0;
        foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
        {
            if (++inspected > MaxScan) break;
            try
            {
                if (t == null || !t.gameObject.activeInHierarchy ||
                    !known.Add(t.GetInstanceID())) continue;
                string path = PathOf(t);
                string name = t.gameObject.name;
                if (!Relevant(name) && !Relevant(path)) continue;
                relevant++;
                int score = (Has(path, "PlayerRigv4") ? 240 : 0) +
                    (Has(path, "LocalPlayer") ? 120 : 0) +
                    (Has(name, "Wand") ? 120 : 0) +
                    (Has(name, "Weapon") ? 90 : 0) +
                    (Has(name, "Adjust") ? 100 : 0) +
                    (Has(path, "Runemage") ? 90 : 0) +
                    (Has(path, "Controller (right)") ? 60 : 0) +
                    (Has(path, "Controller (left)") ? 55 : 0) +
                    (Has(name, "Hand") ? 45 : 0) +
                    (Has(path, "Grip") || Has(path, "Offset") ? 50 : 0) -
                    (Has(path, "OtherPlayer") ? 180 : 0) -
                    (Has(path, "TransmogStation") ? 100 : 0) -
                    (Has(path, "Npc") ? 90 : 0);
                candidates.Add(new Watch {
                    Transform = t, Path = path, Rank = score,
                    FirstPos = t.localPosition, LastPos = t.localPosition,
                    FirstRot = t.localRotation, LastRot = t.localRotation,
                    FirstActive = true, LastActive = true
                });
            }
            catch { }
        }
        int added = 0;
        var usedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (Watch w in _watched) usedPaths.Add(w.Path);
        foreach (Watch w in candidates.OrderByDescending(w => w.Rank))
        {
            if (_watched.Count >= MaxWatched) break;
            if (!usedPaths.Add(w.Path)) continue;
            _watched.Add(w);
            if (++added <= 40)
                Add("ACTIVE WATCH " + w.Path + " priority=" + w.Rank +
                    " initialPos=" + V(w.FirstPos) +
                    " initialRot=" + Q(w.FirstRot));
        }
        Add($"ACTIVE SCENE SCAN inspected={inspected} relevant={relevant} " +
            $"candidates={candidates.Count} newlyWatched={added} watched={_watched.Count}");
    }
    private void ReadInputs()
    {
        if (_xrNodeType != null)
        {
            bool leftValid = TryTrigger(Enum.Parse(_xrNodeType, "LeftHand"), out bool left, out _);
            bool rightValid = TryTrigger(Enum.Parse(_xrNodeType, "RightHand"), out bool right, out _);
            if (leftValid && (!_leftKnown || _leftDown != left))
                Trigger("LEFT",left);
            if (!_armed) return;
            if (rightValid && (!_rightKnown || _rightDown != right))
                Trigger("RIGHT",right);
            _leftKnown=leftValid; _rightKnown=rightValid;
            _leftDown=left; _rightDown=right;
        }
        try
        {
            uint buttons = 0;
            for (int i=0;i<20;i++)
                if (Input.GetKey((KeyCode)((int)KeyCode.JoystickButton0+i))) buttons |= 1u<<i;
            uint changed = buttons ^ _rawButtons;
            for (int i=0;i<20;i++)
                if ((changed & (1u<<i)) != 0)
                    Add("RAW JOYSTICK "+i+(((buttons & (1u<<i)) != 0)?" DOWN":" UP"));
            _rawButtons=buttons;
        }
        catch { }
    }
    private void Trigger(string hand, bool down)
    {
        Add(hand + " XR TRIGGER " + (down?"DOWN":"UP"));
        if (!down) return;
        _presses++;
        if (_firstTrigger < 0)
        {
            _firstTrigger = Time.realtimeSinceStartup;
            _rescanAt = _firstTrigger + 0.40f;
            Add("INFERRED START: first trigger press (not a verified game callback)");
        }
        else if (Plugin.AutoStopOnSecondTrigger.Value &&
                 Time.realtimeSinceStartup - _firstTrigger >= 1.5f)
        {
            Add("INFERRED FINISH: later trigger press (not verified as commit)");
            End("inferred subsequent trigger");
        }
    }
    private void Sample()
    {
        _samples++;
        int changed = 0;
        foreach (Watch w in _watched)
        {
            try
            {
                if (w.Transform == null) continue;
                Vector3 p = w.Transform.localPosition;
                Quaternion r = w.Transform.localRotation;
                bool active = w.Transform.gameObject.activeInHierarchy;
                float meters=Vector3.Distance(p,w.LastPos);
                float degrees=Quaternion.Angle(r,w.LastRot);
                if (meters<0.0007f && degrees<0.12f && active==w.LastActive) continue;
                if (++changed<=7)
                    Add($"TRANSFORM {w.Path} dPos={meters:0.0000}m dRot={degrees:0.00}deg active={active} pos={V(p)} rot={Q(r)}");
                w.LastPos=p; w.LastRot=r; w.LastActive=active;
            }
            catch { }
        }
        if (changed>7 && _samples%10==0)
            Add("Further changed transforms suppressed: "+(changed-7));
    }
    private void OnDestroy()
    {
        if (_armed && !_busy) End("plugin unloaded");
    }
    private void End(string reason)
    {
        _busy = true; _armed = false;
        try
        {
            Add("STOP: "+reason);
            var sb = new StringBuilder(150000);
            sb.AppendLine("ORBUS WEAPON ADJUSTMENTS LIVE DIAGNOSTIC v0.1.2");
            sb.AppendLine("Read-only: these input edges are NOT verified game callbacks.");
            sb.AppendLine("Review paths for player names before sharing. No UI text or network data collected.");
            sb.AppendLine($"reason={reason} seconds={Time.realtimeSinceStartup-_started:0.00} samples={_samples} watched={_watched.Count} triggers={_presses} dropped={_overflow}");
            sb.AppendLine("=== TIMELINE ===");
            foreach (string line in _events) sb.AppendLine(line);
            sb.AppendLine("=== START VS END LOCAL TRANSFORMS ===");
            foreach (Watch w in _watched)
            {
                try
                {
                    if (w.Transform==null) { sb.AppendLine("DESTROYED "+w.Path); continue; }
                    sb.AppendLine(w.Path+" start="+V(w.FirstPos)+" "+Q(w.FirstRot)+
                        " end="+V(w.Transform.localPosition)+" "+Q(w.Transform.localRotation)+
                        " activeBefore="+w.FirstActive+" activeAfter="+w.Transform.gameObject.activeInHierarchy);
                }
                catch { sb.AppendLine("UNAVAILABLE "+w.Path); }
            }
            string folder = Path.Combine(Paths.BepInExRootPath,"WeaponAdjustmentDiagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder,"WeaponAdjustments_Live_"+
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".txt");
            File.WriteAllText(path,sb.ToString(),new UTF8Encoding(false));
            Plugin.ModLog.LogInfo("[WeaponAdjustments] Live diagnostic saved: "+path);
        }
        catch(Exception e) { Plugin.ModLog.LogError("[WeaponAdjustments] Unable to write live report: "+e); }
        finally { _busy = false; }
    }

    // XR trigger reader shared with the proven RebornCamera VR UI observer.
    // Reflection avoids hard references to missing legacy Unity XR types.
    private static System.Type? _xrNodeType;
    private static bool _xrTriggerApiChecked;
    private static string _xrTriggerApiStatus = "not inspected";
    private static System.Reflection.MethodInfo? _getDeviceAtNode;
    private static System.Reflection.MethodInfo? _readDigital;
    private static System.Reflection.MethodInfo? _readAnalog;
    private static System.Reflection.PropertyInfo? _isValid;
    private static object? _digitalUsage;
    private static object? _analogUsage;

    private static System.Type? FindManagedType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                System.Type? type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            catch { /* Optional Unity assembly may not load. */ }
        }
        return null;
    }

    private static object? ReadStaticUsage(System.Type type, string name)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static;
        try
        {
            var field = type.GetField(name, flags);
            if (field != null) return field.GetValue(null);
            var property = type.GetProperty(name, flags);
            return property != null ? property.GetValue(null) : null;
        }
        catch { return null; }
    }

    private static System.Reflection.MethodInfo? FeatureMethod(
        System.Type deviceType, object? usage, System.Type outputType)
    {
        if (usage == null) return null;
        foreach (var method in deviceType.GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance))
        {
            if (method.Name != "TryGetFeatureValue") continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 2 &&
                parameters[0].ParameterType == usage.GetType() &&
                parameters[1].ParameterType ==
                    outputType.MakeByRefType())
                return method;
        }
        return null;
    }

    private static void ProbeXrTriggerApi()
    {
        if (_xrTriggerApiChecked) return;
        _xrTriggerApiChecked = true;
        try
        {
            System.Type? devices = FindManagedType("UnityEngine.XR.InputDevices");
            _xrNodeType = FindManagedType("UnityEngine.XR.XRNode");
            System.Type? usages = FindManagedType("UnityEngine.XR.CommonUsages");
            if (devices == null || usages == null || _xrNodeType == null)
            {
                _xrTriggerApiStatus =
                    "Legacy XR API: InputDevices/CommonUsages not exposed";
                return;
            }
            _getDeviceAtNode = devices.GetMethod("GetDeviceAtXRNode",
                new[] { _xrNodeType });
            if (_getDeviceAtNode == null)
            {
                _xrTriggerApiStatus =
                    "InputDevices found; GetDeviceAtXRNode unavailable";
                return;
            }
            System.Type deviceType = _getDeviceAtNode.ReturnType;
            _isValid = deviceType.GetProperty("isValid");
            _digitalUsage = ReadStaticUsage(usages, "triggerButton");
            _analogUsage = ReadStaticUsage(usages, "trigger");
            _readDigital = FeatureMethod(
                deviceType, _digitalUsage, typeof(bool));
            _readAnalog = FeatureMethod(
                deviceType, _analogUsage, typeof(float));

            _xrTriggerApiStatus = _readDigital != null || _readAnalog != null
                ? "Runtime Unity XR trigger polling available"
                : "Unity XR types found; trigger features unavailable";
        }
        catch (Exception ex)
        {
            _xrTriggerApiStatus =
                "XR API probe failed: " + ex.GetType().Name;
        }
    }

    private static bool TryTrigger(object node, out bool pressed,
        out string kind)
    {
        pressed = false;
        ProbeXrTriggerApi();
        kind = _xrTriggerApiStatus;
        if (_getDeviceAtNode == null) return false;

        try
        {
            object? device = _getDeviceAtNode.Invoke(null,
                new object[] { node });
            if (device == null)
            {
                kind = "XR input device not available";
                return false;
            }
            if (_isValid != null &&
                _isValid.GetValue(device) is bool valid && !valid)
            {
                kind = "XR device not valid";
                return false;
            }
            if (_readDigital != null && _digitalUsage != null)
            {
                object?[] args = { _digitalUsage, false };
                object? ok = _readDigital.Invoke(device, args);
                if (ok is bool received && received &&
                    args[1] is bool digital)
                {
                    pressed = digital;
                    kind = "XR triggerButton";
                    return true;
                }
            }
            if (_readAnalog != null && _analogUsage != null)
            {
                object?[] args = { _analogUsage, 0f };
                object? ok = _readAnalog.Invoke(device, args);
                if (ok is bool received && received &&
                    args[1] is float analog)
                {
                    pressed = analog >= 0.70f;
                    kind = "XR analog trigger >=0.70";
                    return true;
                }
            }
            kind = "XR trigger feature not returned by device";
        }
        catch (Exception ex)
        {
            kind = "XR trigger poll error: " + ex.GetType().Name;
        }
        return false;
    }

    
}
