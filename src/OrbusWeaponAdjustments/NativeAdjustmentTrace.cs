using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Observes ONLY named, signature-verified vanilla methods. No scene scan,
/// weapon mutation, save API calls, or native DOM changes. Separate from the
/// existing v0.1.4 live transform recorder.
/// </summary>
internal static class NativeAdjustmentTrace
{
    private const string HarmonyId = "com.horizon.orbus.weaponadjustments.methodtrace";
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static;
    private const BindingFlags Own = All | BindingFlags.DeclaredOnly;
    private const int MaxRecords = 256;
    private static readonly List<string> Records = new();
    private static Harmony? _harmony;
    private static DateTime _start;
    private static bool _armed;
    private static int _dropped;
    private static bool _installed;
    private static bool _failureLogged;

    internal static bool IsArmed => _armed;
    internal static bool HooksInstalled => _installed;


    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? candidate = assembly.GetType(fullName, false);
                if (candidate != null) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static MethodInfo? Method(Type type, string name, int parameters)
    {
        return type.GetMethods(Own).FirstOrDefault(m =>
            m.Name == name && m.ReturnType == typeof(void) &&
            m.GetParameters().Length == parameters);
    }

    // Il2CppInterop generates some native fields as managed properties.
    // Validate their declared type through either representation.
    private static Type? DataMemberType(Type type, string name)
    {
        return type.GetField(name, Own)?.FieldType ??
               type.GetProperty(name, Own)?.PropertyType;
    }

    private static bool Field(Type type, string name, Type expected)
    {
        return DataMemberType(type, name) == expected;
    }

    private static bool VerifyObfuscated(Type adjuster, Type menu)
    {
        // Derived from matching all four Community WeaponAdjuster field
        // offsets/types AND its ordered method signatures to Renewed cmi.
        // Fail CLOSED if the game changes class layout in a future update.
        return typeof(MonoBehaviour).IsAssignableFrom(adjuster) &&
               Field(adjuster, "bhje", typeof(float)) &&
               Field(adjuster, "bhjf", typeof(bool)) &&
               DataMemberType(adjuster, "bhjc")?.Name == "my" &&
               DataMemberType(adjuster, "bhjd")?.Name == "cle" &&
               DataMemberType(menu, "sew") != null;
    }

    private static bool VerifyCommunity(Type adjuster, Type menu)
    {
        return typeof(MonoBehaviour).IsAssignableFrom(adjuster) &&
               Field(adjuster, "timeUntilDeath", typeof(float)) &&
               Field(adjuster, "isGripped", typeof(bool)) &&
               DataMemberType(adjuster, "pi")?.Name == "PhysicsInteractable" &&
               DataMemberType(adjuster, "toolController")?.Name == "ToolController" &&
               DataMemberType(menu, "document") != null;
    }

    internal static void Install()
    {
        if (_installed) return;
        if (!Plugin.EnableNativeMethodTrace.Value)
        {
            Plugin.ModLog.LogInfo("[WeaponAdjustments/Methods] Native trace disabled in config.");
            return;
        }

        // No ClassAnalyzer-style broad scene scan. Only exact method hooks.
        Type? adjuster = FindType("cmi");
        Type? menu = FindType("ng");
        bool renewed = adjuster != null && menu != null &&
                       VerifyObfuscated(adjuster, menu);
        if (!renewed)
        {
            adjuster = FindType("MagicalActual.WeaponAdjuster");
            menu = FindType("PlayerMenu");
            if (adjuster == null || menu == null || !VerifyCommunity(adjuster, menu))
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    Plugin.ModLog.LogWarning(
                        "[WeaponAdjustments/Methods] Known Community/Renewed types not ready" +
                        " or fingerprint mismatch. Hooks remain OFF; retrying safely.");
                }
                return;
            }
        }

        string submit = renewed ? "mnh" : "SubmitAdjust";
        string gripped = renewed ? "mni" : "WeaponGripped";
        string ungripped = renewed ? "mnj" : "WeaponUngripped";
        string toggle = renewed ? "emh" : "ToggleAdjustWeapon";
        string reset = renewed ? "emi" : "ResetAdjustWeapon";

        MethodInfo? submitMethod = Method(adjuster, submit, 0);
        MethodInfo? grippedMethod = Method(adjuster, gripped, 0);
        MethodInfo? ungrippedMethod = Method(adjuster, ungripped, 0);
        MethodInfo? toggleMethod = Method(menu, toggle, 1);
        MethodInfo? resetMethod = Method(menu, reset, 1);
        if (submitMethod == null || grippedMethod == null || ungrippedMethod == null ||
            toggleMethod == null || resetMethod == null ||
            !toggleMethod.IsStatic || !resetMethod.IsStatic ||
            submitMethod.IsStatic || grippedMethod.IsStatic || ungrippedMethod.IsStatic)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                Plugin.ModLog.LogWarning(
                    "[WeaponAdjustments/Methods] Signature mismatch; hooks OFF.");
            }
            return;
        }

        try
        {
            _harmony = new Harmony(HarmonyId);
            Patch(toggleMethod, nameof(MenuToggleBefore), nameof(MenuToggleAfter));
            Patch(resetMethod, nameof(MenuResetBefore), nameof(MenuResetAfter));
            Patch(submitMethod, nameof(SubmitBefore), nameof(SubmitAfter));
            Patch(grippedMethod, null, nameof(GrippedAfter));
            Patch(ungrippedMethod, null, nameof(UngrippedAfter));
            _installed = true;
            _failureLogged = false;
            Plugin.ModLog.LogInfo("[WeaponAdjustments/Methods] READ-ONLY hooks installed: " +
                menu.FullName + "." + toggle + "/" + reset + ", " +
                adjuster.FullName + "." + submit + "/" + gripped + "/" + ungripped +
                "; build=" + (renewed ? "Renewed/Spades" : "Community") + ".");
            Plugin.ModLog.LogInfo(
                "[WeaponAdjustments/Methods] Ctrl+Alt+P start/stop a targeted method report.");
        }
        catch (Exception ex)
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            _installed = false;
            Plugin.ModLog.LogWarning("[WeaponAdjustments/Methods] Hook setup failed (fail-closed): " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void Patch(MethodInfo target, string? before, string? after)
    {
        MethodInfo? Before(string? name) =>
            name == null ? null : typeof(NativeAdjustmentTrace).GetMethod(name, All);
        _harmony!.Patch(
            target,
            prefix: before == null ? null : new HarmonyMethod(Before(before)!),
            postfix: after == null ? null : new HarmonyMethod(Before(after)!));
    }

    internal static void Toggle()
    {
        if (_armed) Stop("manual stop");
        else Start();
    }

    internal static void Start()
    {
        if (!_installed)
        {
            Plugin.ModLog.LogWarning(
                "[WeaponAdjustments/Methods] No validated native hooks installed; check LogOutput.");
            return;
        }
        Records.Clear();
        _dropped = 0;
        _start = DateTime.Now;
        _armed = true;
        Log("ARMED v0.1.6: native adjustment and local persistence API observations; read-only");
        PersistenceProbe.OnStart();
        Plugin.ModLog.LogInfo("[WeaponAdjustments/Methods] Trace ARMED. Press Ctrl+Alt+P to save.");
    }

    internal static void Stop(string reason)
    {
        if (!_armed) return;
        Log("STOP " + reason);
        _armed = false;
        PersistenceProbe.OnStop();
        try
        {
            string folder = Path.Combine(Paths.BepInExRootPath, "WeaponAdjustmentDiagnostics");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "WeaponMechanics_" +
                _start.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".txt");
            var sb = new StringBuilder(32768);
            sb.AppendLine("ORBUS WEAPON ADJUSTMENTS: NATIVE METHOD TRACE v0.1.6");
            sb.AppendLine("Read-only; method invocation confirms control flow, not persistent save semantics.");
            sb.AppendLine("Derived from Community WeaponAdjuster and renewed cmi class mapping.");
            sb.AppendLine("Local preference API calls are indicators only; they cannot prove where grip data is saved.");
            sb.AppendLine("No preference keys or values are captured. No events does NOT prove server storage.");
            sb.AppendLine("Review GameObject names/paths before sharing. No UI input text collected.");
            sb.AppendLine("eventCount=" + Records.Count + " dropped=" + _dropped);
            foreach (string line in Records) sb.AppendLine(line);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            Plugin.ModLog.LogInfo("[WeaponAdjustments/Methods] Saved: " + file);
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogError("[WeaponAdjustments/Methods] Report write failed: " + ex);
        }
    }

    private static void Log(string eventText)
    {
        if (!_armed) return;
        if (Records.Count >= MaxRecords) { _dropped++; return; }
        Records.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
            " " + eventText);
    }

    internal static void LogStorage(string eventText) => Log(eventText);

    private static string Pose(Transform t)
    {
        Vector3 lp = t.localPosition, wp = t.position;
        Quaternion lr = t.localRotation;
        return string.Format(CultureInfo.InvariantCulture,
            "local=({0:F5},{1:F5},{2:F5}) rot=({3:F5},{4:F5},{5:F5},{6:F5}) world=({7:F5},{8:F5},{9:F5})",
            lp.x, lp.y, lp.z, lr.x, lr.y, lr.z, lr.w, wp.x, wp.y, wp.z);
    }

    private static string PathOf(Transform t)
    {
        var segments = new List<string>();
        Transform? node = t;
        for (int n = 0; n < 8 && node != null; n++, node = node.parent)
        {
            string name = node.gameObject.name.Replace('\r', ' ').Replace('\n', ' ');
            segments.Add(name.Length <= 70 ? name : name.Substring(0, 70));
        }
        segments.Reverse();
        return string.Join("/", segments);
    }

    private static string SmallField(object instance, string name)
    {
        try
        {
            Type t = instance.GetType();
            object? val = t.GetField(name, Own)?.GetValue(instance);
            if (val == null)
                val = t.GetProperty(name, Own)?.GetValue(instance);
            return val is float f ? f.ToString("0.000", CultureInfo.InvariantCulture) :
                   val is bool b ? b.ToString() : "(unavailable)";
        }
        catch { return "(unavailable)"; }
    }

    private static string Describe(object? instance)
    {
        try
        {
            if (instance is not Component c || c == null) return "instance=(unavailable)";
            Transform t = c.transform;
            string parent = t.parent != null ? PathOf(t.parent) : "(scene root)";
            string gripField = instance.GetType().Name == "cmi" ? "bhjf" : "isGripped";
            string timerField = instance.GetType().Name == "cmi" ? "bhje" : "timeUntilDeath";
            return "class=" + instance.GetType().FullName +
                " obj=" + PathOf(t) + " parent=" + parent +
                " grip=" + SmallField(instance, gripField) +
                " timer=" + SmallField(instance, timerField) + " " + Pose(t);
        }
        catch (Exception ex) { return "description-failed=" + ex.GetType().Name; }
    }

    private static void Record(string label, object? instance = null)
    {
        if (!_armed) return;
        try { Log(label + (instance == null ? "" : " " + Describe(instance))); }
        catch (Exception ex)
        {
            Log(label + " observation-error=" + ex.GetType().Name);
        }
    }

    // Harmony prefix/postfix signatures must never return false or write to
    // __result: the vanilla Orbus methods remain completely unmodified.
    private static void MenuToggleBefore() => Record("MENU.ToggleAdjustWeapon BEFORE");
    private static void MenuToggleAfter() => Record("MENU.ToggleAdjustWeapon AFTER");
    private static void MenuResetBefore() => Record("MENU.ResetAdjustWeapon BEFORE");
    private static void MenuResetAfter() => Record("MENU.ResetAdjustWeapon AFTER");
    private static void SubmitBefore(object __instance) => Record("ADJUST.SubmitAdjust BEFORE", __instance);
    private static void SubmitAfter(object __instance)
    {
        Record("ADJUST.SubmitAdjust AFTER", __instance);
        PersistenceProbe.MarkSubmitted(__instance);
    }
    private static void GrippedAfter(object __instance) => Record("ADJUST.WeaponGripped AFTER", __instance);
    private static void UngrippedAfter(object __instance) => Record("ADJUST.WeaponUngripped AFTER", __instance);
}

public sealed class NativeAdjustmentTraceBehaviour : MonoBehaviour
{
    public NativeAdjustmentTraceBehaviour(IntPtr ptr) : base(ptr) { }
    private bool _previous;
    private float _nextHookAttempt;
    private const int Ctrl = 0x11, Alt = 0x12, P = 0x50;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    private static bool Key(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private void Update()
    {
        PersistenceProbe.Tick();
        // The generated game interop assembly may load after BepInEx plugins.
        // Retry a tiny exact-type lookup periodically; never scan scene objects.
        if (!NativeAdjustmentTrace.HooksInstalled && Plugin.EnableNativeMethodTrace.Value &&
            Time.realtimeSinceStartup >= _nextHookAttempt)
        {
            _nextHookAttempt = Time.realtimeSinceStartup + 10f;
            NativeAdjustmentTrace.Install();
        }
        bool chord = (!Plugin.RequireFocus.Value || Application.isFocused) &&
            Key(Ctrl) && Key(Alt) && Key(P);
        if (chord && !_previous) NativeAdjustmentTrace.Toggle();
        _previous = chord;
    }

    private void OnDestroy()
    {
        if (NativeAdjustmentTrace.IsArmed)
            NativeAdjustmentTrace.Stop("plugin unloaded");
    }
}
