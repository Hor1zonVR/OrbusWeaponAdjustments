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
/// adjustment trace is explicitly armed. NO keys, values, file paths,
/// request payloads or client settings are read, saved or changed.
/// A null result does not rule out server/custom storage.
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
                _harmony!.Patch(method, postfix: new HarmonyMethod(callback));
                _hookCount++;
            }
            catch (Exception ex)
            {
                log.LogWarning("[WeaponAdjustments/Storage] Optional " + prefix +
                    "." + methodName + " probe skipped: " + ex.GetType().Name);
            }
        }
    }

    // Harmony post-fixes intentionally accept no parameters. This prevents
    // recording keys, game preferences, account data, or saved values.
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
        NativeAdjustmentTrace.LogStorage("STORAGE PROBE hookCount=" + _hookCount +
            " (does not monitor server or arbitrary disk/network operations)");
    }

    internal static void OnStop()
    {
        _lastSubmitted = null;
        _sampleIndex = 0;
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
