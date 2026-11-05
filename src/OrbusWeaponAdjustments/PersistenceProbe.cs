using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Optional observation of local preference API writes while the native
/// adjustment trace is explicitly armed. Captures only submit-scoped
/// numeric write arguments and in-memory anonymous key identity. Raw keys,
/// file paths, request payloads and other settings are never logged.
/// No callbacks edit client or server state.
/// </summary>
internal static class PersistenceProbe
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static Harmony? _harmony;
    private static int _hookCount;
    private static object? _lastSubmitted;
    private static float _nextSample;
    private static int _sampleIndex;
    private static readonly float[] SampleIntervals = { 0.5f, 1.5f, 4.0f };
    private static readonly Dictionary<string, int> AnonymousKeySlots =
        new(StringComparer.Ordinal);
    private static bool _withinSubmit;
    private static float _submitDeadline;
    private static int _writesInSubmit;
    private const int MaxWriteDetailsPerSubmit = 16;
    private const int MaxKeySlotsPerTrace = 32;

    private static Type? FindType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }

    internal static void Install(ManualLogSource log)
    {
        if (!Plugin.ObserveLocalPreferenceWrites.Value)
        {
            log.LogInfo("[WeaponAdjustments/Storage] Optional PlayerPrefs probe disabled.");
            return;
        }
        try
        {
            _harmony = new Harmony("com.horizon.orbus.weaponadjustments.storageprobe");
            TryPatchType("UnityEngine.PlayerPrefs", "Unity", log);
            TryPatchType("CodeStage.AntiCheat.ObscuredTypes.ObscuredPrefs",
                         "Obscured", log);
        }
        catch (Exception ex)
        {
            log.LogWarning("[WeaponAdjustments/Storage] Optional probe setup skipped: " +
                ex.GetType().Name + ". Native adjustment hooks remain active.");
        }
        log.LogInfo("[WeaponAdjustments/Storage] Observing " + _hookCount +
            " supported local preference methods (metadata only). " +
            "No observed writes does NOT prove a server-side save.");
    }

    private static void TryPatchType(string typeName, string prefix, ManualLogSource log)
    {
        Type? type = FindType(typeName);
        if (type == null) return;
        foreach (string methodName in new[] { "SetString", "SetFloat", "SetInt", "Save", "DeleteKey" })
        {
            try
            {
                MethodInfo? method = type.GetMethods(Flags).FirstOrDefault(m =>
                {
                    if (m.Name != methodName || m.ReturnType != typeof(void)) return false;
                    ParameterInfo[] parameters = m.GetParameters();
                    if (methodName == "Save") return parameters.Length == 0;
                    if (methodName == "DeleteKey") return parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(string);
                    return parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string) &&
                        parameters[1].ParameterType ==
                        (methodName == "SetString" ? typeof(string) :
                         methodName == "SetFloat" ? typeof(float) : typeof(int));
                });
                if (method == null) continue;
                MethodInfo? callback = typeof(PersistenceProbe).GetMethod(
                    prefix + methodName, BindingFlags.NonPublic | BindingFlags.Static);
                if (callback == null) continue;
                // Existing method-call postfix is preserved. Numeric details
                // are captured ONLY during the verified SubmitAdjust frame.
                MethodInfo? details = null;
                if (methodName == "SetFloat" || methodName == "SetInt")
                    details = typeof(PersistenceProbe).GetMethod(
                        prefix + methodName + "Details",
                        BindingFlags.NonPublic | BindingFlags.Static);
                _harmony!.Patch(method,
                    prefix: details == null ? null : new HarmonyMethod(details),
                    postfix: new HarmonyMethod(callback));
                _hookCount++;
            }
            catch (Exception ex)
            {
                log.LogWarning("[WeaponAdjustments/Storage] Optional " + prefix +
                    "." + methodName + " probe skipped: " + ex.GetType().Name);
            }
        }
    }

    // General Harmony postfixes intentionally capture only the method name.
    // Separate guarded prefixes handle numeric arguments during SubmitAdjust.
    // The raw key remains in memory only to assign a per-trace alias.
    private static void UnitySetFloatDetails(string __0, float __1) =>
        RecordNumeric("Unity.PlayerPrefs", __0, __1, null);
    private static void UnitySetIntDetails(string __0, int __1) =>
        RecordNumeric("Unity.PlayerPrefs", __0, null, __1);
    private static void ObscuredSetFloatDetails(string __0, float __1) =>
        RecordNumeric("ObscuredPrefs", __0, __1, null);
    private static void ObscuredSetIntDetails(string __0, int __1) =>
        RecordNumeric("ObscuredPrefs", __0, null, __1);

    private static void RecordNumeric(string api, string key, float? floatValue, int? intValue)
    {
        try
        {
            if (!NativeAdjustmentTrace.IsArmed || !_withinSubmit) return;
            if (Time.realtimeSinceStartup > _submitDeadline)
            {
                _withinSubmit = false;
                NativeAdjustmentTrace.LogStorage(
                    "SUBMIT_WRITE observation window expired; no further numeric captures");
                return;
            }
            if (_writesInSubmit >= MaxWriteDetailsPerSubmit) return;
            int slot;
            if (!AnonymousKeySlots.TryGetValue(key, out slot))
            {
                if (AnonymousKeySlots.Count >= MaxKeySlotsPerTrace) return;
                slot = AnonymousKeySlots.Count + 1;
                AnonymousKeySlots[key] = slot;
            }
            int index = ++_writesInSubmit;
            string type = floatValue.HasValue ? "float" : "int";
            // Float values are only included in this explicitly armed,
            // submit-scoped opt-in capture, never during unrelated writes.
            string value = floatValue.HasValue ?
                floatValue.Value.ToString("R", CultureInfo.InvariantCulture) :
                intValue is >= -16 and <= 255 ?
                    intValue.Value.ToString(CultureInfo.InvariantCulture) :
                    "(integer outside  -16..255 redacted)";
            NativeAdjustmentTrace.LogStorage("SUBMIT_WRITE #" + index +
                " api=" + api + " slot=K" + slot + " type=" + type +
                " value=" + value + " (raw key withheld)");
        }
        catch { /* Instrumentation must never affect vanilla PlayerPrefs. */ }
    }

    internal static void BeginSubmission()
    {
        if (!NativeAdjustmentTrace.IsArmed || !Plugin.ObserveLocalPreferenceWrites.Value) return;
        _withinSubmit = true;
        _writesInSubmit = 0;
        _submitDeadline = Time.realtimeSinceStartup + 0.5f;
        NativeAdjustmentTrace.LogStorage("SUBMIT_WRITE capture began (500ms safety limit)");
    }

    internal static void EndSubmission()
    {
        if (!_withinSubmit) return;
        NativeAdjustmentTrace.LogStorage("SUBMIT_WRITE capture ended; count=" + _writesInSubmit);
        _withinSubmit = false;
    }

    private static void UnitySetString() => Record("Unity.PlayerPrefs.SetString");
    private static void UnitySetFloat() => Record("Unity.PlayerPrefs.SetFloat");
    private static void UnitySetInt() => Record("Unity.PlayerPrefs.SetInt");
    private static void UnitySave() => Record("Unity.PlayerPrefs.Save");
    private static void UnityDeleteKey() => Record("Unity.PlayerPrefs.DeleteKey");
    private static void ObscuredSetString() => Record("ObscuredPrefs.SetString");
    private static void ObscuredSetFloat() => Record("ObscuredPrefs.SetFloat");
    private static void ObscuredSetInt() => Record("ObscuredPrefs.SetInt");
    private static void ObscuredSave() => Record("ObscuredPrefs.Save");
    private static void ObscuredDeleteKey() => Record("ObscuredPrefs.DeleteKey");

    private static void Record(string method)
    {
        if (!NativeAdjustmentTrace.IsArmed) return;
        NativeAdjustmentTrace.LogStorage("LOCAL_PREF_API " + method +
            " called; key/value not inspected");
    }

    internal static void OnStart()
    {
        _lastSubmitted = null;
        _sampleIndex = 0;
        _withinSubmit = false;
        _writesInSubmit = 0;
        AnonymousKeySlots.Clear();
        NativeAdjustmentTrace.LogStorage("STORAGE PROBE hookCount=" + _hookCount +
            " (does not monitor server or arbitrary disk/network operations)");
    }

    internal static void OnStop()
    {
        _lastSubmitted = null;
        _sampleIndex = 0;
        _withinSubmit = false;
        _writesInSubmit = 0;
        AnonymousKeySlots.Clear();
    }

    internal static void MarkSubmitted(object instance)
    {
        if (!NativeAdjustmentTrace.IsArmed) return;
        _lastSubmitted = instance;
        _sampleIndex = 0;
        _nextSample = Time.realtimeSinceStartup + SampleIntervals[0];
    }

    internal static void Tick()
    {
        if (!NativeAdjustmentTrace.IsArmed || _lastSubmitted == null ||
            _sampleIndex >= SampleIntervals.Length ||
            Time.realtimeSinceStartup < _nextSample) return;
        NativeAdjustmentTrace.LogStorage("POST_SUBMIT_T+" +
            SampleIntervals[_sampleIndex].ToString("0.0", CultureInfo.InvariantCulture) +
            " " + Describe(_lastSubmitted));
        _sampleIndex++;
        if (_sampleIndex >= SampleIntervals.Length)
        {
            _lastSubmitted = null;
            return;
        }
        _nextSample = Time.realtimeSinceStartup +
            (SampleIntervals[_sampleIndex] - SampleIntervals[_sampleIndex - 1]);
    }

    private static string Describe(object instance)
    {
        try
        {
            if (instance is not Component component || component == null)
                return "temporary adjustment object no longer available";
            Transform transform = component.transform;
            string parent = transform.parent != null ?
                transform.parent.gameObject.name : "(scene root)";
            string s = "object=" + transform.gameObject.name + " parent=" + parent +
                " local=" + Pose(transform);
            int count = Math.Min(transform.childCount, 12);
            for (int i = 0; i < count; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null || child.gameObject.name.IndexOf("wand",
                        StringComparison.OrdinalIgnoreCase) < 0) continue;
                s += " wandChild=" + child.gameObject.name +
                     " wandLocal=" + Pose(child);
                break;
            }
            return s;
        }
        catch (Exception ex) { return "object observation failed: " + ex.GetType().Name; }
    }

    private static string Pose(Transform t)
    {
        Vector3 p = t.localPosition;
        Quaternion q = t.localRotation;
        return string.Format(CultureInfo.InvariantCulture,
            "pos=({0:F5},{1:F5},{2:F5}) rot=({3:F5},{4:F5},{5:F5},{6:F5})",
            p.x, p.y, p.z, q.x, q.y, q.z, q.w);
    }
}
