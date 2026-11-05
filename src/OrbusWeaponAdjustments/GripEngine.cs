using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Strictly bounded native read/write bridge. Reuses vanilla PlayerPrefs and
/// vanilla PlayerAvatar.MakeRigBasedAdjustments; never moves a weapon transform.
/// Backups are written BEFORE any mod-initiated preference change.
/// </summary>
internal static class GripEngine
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.Instance;
    private static readonly string[] AllClasses = {
        "Bard", "Runemage", "Warrior", "Paladin", "Musketeer",
        "Ranger", "Shaman", "Scoundrel"
    };
    private sealed class Partial
    {
        internal GripSnapshot Snapshot = new();
        internal readonly string[][] Keys = { new string[3], new string[3] }; // rot, pos
        internal bool[] Ready = { false, false };
        internal bool Missing;
    }
    private static readonly Dictionary<string, Partial> Pending = new();
    private static readonly Dictionary<string, GripSnapshot> LastKnown = new();
    private static Harmony? _harmony;
    private static bool _installed;
    private static Type? _avatarType, _rigType, _menuType;
    private static MethodInfo? _apply, _getController;
    private static int _reading = -1, _readCount;
    private static string _readScope = "";
    private static string[] _readKeys = new string[3];
    private static bool _submitting, _ourWrite;
    private static bool _readingFlag;
    private static string _flagScope = "";
    private static readonly List<(string key, int value)> FlagReads = new();
    private static readonly Dictionary<string, (string key, int value)> KnownFlags = new();
    private static readonly List<(string key, float value)> SubmitFloats = new();
    private static string? _submitFlag;
    private static int _submitFlagValue;
    private static GripSnapshot? _undo;
    private static bool _contextWarningLogged;
    internal static bool Installed => _installed;
    internal static string Status { get; private set; } = "Waiting for supported Orbus client";
    internal static string LastMessage { get; private set; } = "";

    private static Type? Find(string name)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { Type? t = a.GetType(name, false); if (t != null) return t; }
            catch { }
        }
        return null;
    }

    private static object? Member(object? obj, string name)
    {
        if (obj == null) return null;
        try
        {
            Type t = obj is Type type ? type : obj.GetType();
            object? instance = obj is Type ? null : obj;
            return t.GetField(name, Flags)?.GetValue(instance) ??
                   t.GetProperty(name, Flags)?.GetValue(instance);
        }
        catch { return null; }
    }

    private static Type? MemberType(Type type, string name) =>
        type.GetField(name, Flags)?.FieldType ??
        type.GetProperty(name, Flags)?.PropertyType;

    private static MethodInfo? Method(Type type, string name, int count)
    {
        return type.GetMethods(Flags).FirstOrDefault(m =>
            m.Name == name && m.GetParameters().Length == count);
    }

    private static bool VerifiedRig(Type t, bool renewed)
    {
        string[] names = renewed
            ? new[] { "bfsl", "bfsm", "bfsn", "bfso", "bfsp", "bfsq", "bfsr", "bfss", "bfst", "bfsu" }
            : new[] { "rotationAdjustmentTouch", "positionAdjustmentTouch", "finalQuaternionTouch",
                      "rotationAdjustmentSteam", "positionAdjustmentSteam",
                      "rotationAdjustmentIndex", "positionAdjustmentIndex",
                      "originalPosition", "originalRotation", "ignoreReset" };
        // Community private field names may differ across builds. Renewed is
        // verified by exact field layout, type sequence and method signatures.
        Type[] types = { typeof(Vector3), typeof(Vector3), typeof(Quaternion),
            typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Vector3),
            typeof(Vector3), typeof(Vector3), typeof(bool) };
        if (!renewed) return true;
        for (int i = 0; i < names.Length; i++)
        {
            Type? actual = MemberType(t, names[i]);
            if (actual != types[i]) return false;
        }
        return true;
    }

    internal static void Install()
    {
        if (_installed) return;
        try
        {
            bool renewed = Find("cgr") != null;
            _rigType = Find(renewed ? "cgr" : "MagicalActual.RigBasedAdjustment");
            _avatarType = Find(renewed ? "cbn" : "MagicalActual.PlayerAvatar");
            _menuType = Find(renewed ? "ng" : "PlayerMenu");
            Type? prefs = Find("UnityEngine.PlayerPrefs");
            Type? player = Find(renewed ? "ceh" : "MagicalActual.PlayerCharacter");
            Type? cls = Find(renewed ? "cei" : "MagicalActual.PlayerClass");
            if (_rigType == null || _avatarType == null || _menuType == null ||
                player == null || cls == null || prefs == null ||
                !VerifiedRig(_rigType, renewed))
                return;

            string apply = renewed ? "kxt" : "MakeRigBasedAdjustments";
            string controller = renewed ? "kxs" : "getControllerType";
            string rotation = renewed ? "lvo" : "getPlayerAdjustRotation";
            string position = renewed ? "lvp" : "getPlayerAdjustPosition";
            _apply = Method(_avatarType, apply, 0);
            _getController = Method(_avatarType, controller, 0);
            MethodInfo? getRotation = Method(_rigType, rotation, 0);
            MethodInfo? getPosition = Method(_rigType, position, 0);
            Type? adjuster = Find(renewed ? "cmi" : "MagicalActual.WeaponAdjuster");
            MethodInfo? submit = adjuster == null ? null :
                Method(adjuster, renewed ? "mnh" : "SubmitAdjust", 0);
            MethodInfo? resetRig = Method(_rigType, "Reset", 0);
            MethodInfo? getInt = prefs.GetMethods(Flags).SingleOrDefault(m =>
                m.Name == "GetInt" && m.IsStatic && m.ReturnType == typeof(int) &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            MethodInfo? getFloat = prefs.GetMethods(Flags).SingleOrDefault(m =>
                m.Name == "GetFloat" && m.IsStatic && m.ReturnType == typeof(float) &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            MethodInfo? setFloat = prefs.GetMethods(Flags).SingleOrDefault(m =>
                m.Name == "SetFloat" && m.IsStatic && m.ReturnType == typeof(void) &&
                m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(string) &&
                m.GetParameters()[1].ParameterType == typeof(float));
            MethodInfo? setInt = prefs.GetMethods(Flags).SingleOrDefault(m =>
                m.Name == "SetInt" && m.IsStatic && m.ReturnType == typeof(void) &&
                m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(string) &&
                m.GetParameters()[1].ParameterType == typeof(int));
            if (_apply == null || _getController == null || getRotation == null ||
                getPosition == null || getFloat == null || setFloat == null ||
                setInt == null || getInt == null || resetRig == null ||
                submit == null || submit.ReturnType != typeof(void) ||
                submit.IsStatic || submit.GetParameters().Length != 0 ||
                resetRig.ReturnType != typeof(void) ||
                _apply.ReturnType != typeof(void) ||
                _getController.ReturnType != typeof(string) ||
                getRotation.ReturnType != typeof(Vector3) ||
                getPosition.ReturnType != typeof(Vector3))
                return;
            // Validate the known local-character chain before allowing hooks.
            if (renewed &&
                (MemberType(player, "betm") != _avatarType ||
                 MemberType(_avatarType, "becm") != cls ||
                 MemberType(cls, "bfav") != typeof(string) ||
                 MemberType(_menuType, "seu") != typeof(string)))
                return;

            _harmony = new Harmony("com.horizon.orbus.weaponadjustments.presets");
            HarmonyMethod H(string name) => new(typeof(GripEngine).GetMethod(name, Flags)!);
            // Preset capture must not depend on whether legacy diagnostics
            // are enabled in BepInEx.
            _harmony.Patch(submit, prefix: H(nameof(OnVanillaSubmitBefore)),
                postfix: H(nameof(OnVanillaSubmitAfter)));
            _harmony.Patch(resetRig, prefix: H(nameof(BeginRigReset)), postfix: H(nameof(EndRigReset)));
            _harmony.Patch(getInt, postfix: H(nameof(ObserveGetInt)));
            _harmony.Patch(getRotation, prefix: H(nameof(BeginRotation)), postfix: H(nameof(EndRead)));
            _harmony.Patch(getPosition, prefix: H(nameof(BeginPosition)), postfix: H(nameof(EndRead)));
            _harmony.Patch(getFloat, postfix: H(nameof(ObserveGetFloat)));
            _harmony.Patch(setFloat, prefix: H(nameof(ObserveSetFloat)));
            _harmony.Patch(setInt, prefix: H(nameof(ObserveSetInt)));
            _installed = true;
            Status = "Preset bridge ready; awaiting saved grip read";
            Plugin.ModLog.LogInfo("[WeaponAdjustments/Presets] Native grip reader/write hooks installed (" +
                (renewed ? "Renewed/Spades" : "Community") + "), no values modified.");
        }
        catch (Exception ex)
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            Status = "Preset bridge unavailable (" + ex.GetType().Name + ")";
            if (!_contextWarningLogged)
            {
                Plugin.ModLog.LogWarning("[WeaponAdjustments/Presets] Failed closed: " +
                    ex.GetType().Name + " " + ex.Message);
                _contextWarningLogged = true;
            }
        }
    }

    // Derive scope only from the game's actual local player and active class.
    internal static bool TryContext(out object? avatar, out GripSnapshot snapshot)
    {
        avatar = null;
        snapshot = new GripSnapshot();
        try
        {
            bool renewed = _avatarType?.Name == "cbn";
            Type? playerType = Find(renewed ? "ceh" : "MagicalActual.PlayerCharacter");
            object? pc = Member(playerType, renewed ? "blfr" : "Instance");
            if (pc == null && playerType != null)
                pc = Method(playerType, renewed ? "led" : "get_Instance", 0)?.Invoke(null, null);
            avatar = Member(pc, renewed ? "betm" : "myPlayerAvatar");
            object? active = Member(avatar, renewed ? "becm" : "currentActiveClass");
            string className = Member(active, renewed ? "bfav" : "className") as string ?? "";
            string character = Member(_menuType, renewed ? "seu" : "currentPlayerName") as string ?? "";
            string controller = _getController?.Invoke(avatar, null) as string ?? "";
            if (avatar == null || string.IsNullOrWhiteSpace(className) ||
                string.IsNullOrWhiteSpace(character) || string.IsNullOrWhiteSpace(controller) ||
                className.Length > 60 || character.Length > 120 || controller.Length > 60)
                return false;
            snapshot.ClassName = className.Trim();
            snapshot.Controller = controller.Trim();
            snapshot.Scope = PresetStore.HashScope(character, snapshot.ClassName, snapshot.Controller);
            return true;
        }
        catch { return false; }
    }

    private static void BeginRigReset()
    {
        if (_ourWrite || !_installed || !TryContext(out _, out GripSnapshot ctx)) return;
        _readingFlag = true;
        _flagScope = ctx.Scope;
        FlagReads.Clear();
    }

    private static void ObserveGetInt(string __0, int __result)
    {
        if (_readingFlag && !_ourWrite && !string.IsNullOrWhiteSpace(__0) &&
            FlagReads.Count < 8)
            FlagReads.Add((__0, __result));
    }

    private static bool FlagMatchesKeys(string key, string[] floats)
    {
        if (floats.Length != 6 || floats.Any(string.IsNullOrWhiteSpace)) return false;
        string prefix = floats[0];
        foreach (string value in floats.Skip(1))
        {
            int n = 0;
            while (n < prefix.Length && n < value.Length && prefix[n] == value[n]) n++;
            prefix = prefix.Substring(0, n);
        }
        return prefix.Length >= 6 && key.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static void EndRigReset()
    {
        if (!_readingFlag) return;
        _readingFlag = false;
        if (FlagReads.Count != 1 || FlagReads[0].value != 1 ||
            !PlayerPrefs.HasKey(FlagReads[0].key) ||
            !TryContext(out _, out GripSnapshot ctx) || ctx.Scope != _flagScope)
            return;
        KnownFlags[ctx.Scope] = FlagReads[0];
        if (LastKnown.TryGetValue(ctx.Scope, out GripSnapshot? existing))
        {
            existing.FlagKey = FlagReads[0].key;
            existing.FlagValue = 1;
        }
    }

    private static void BeginRotation() => BeginRead(0);
    private static void BeginPosition() => BeginRead(1);

    private static void BeginRead(int part)
    {
        if (!_installed || _ourWrite || !TryContext(out _, out GripSnapshot ctx)) return;
        _reading = part;
        _readCount = 0;
        _readScope = ctx.Scope;
        _readKeys = new string[3];
        if (!Pending.ContainsKey(ctx.Scope))
            Pending[ctx.Scope] = new Partial { Snapshot = ctx };
    }

    private static void ObserveGetFloat(string __0, float __result)
    {
        if (!_installed || _reading < 0 || _reading > 1 || _ourWrite ||
            _readCount >= 3 || string.IsNullOrEmpty(__0)) return;
        _readKeys[_readCount++] = __0;
    }

    private static void EndRead()
    {
        if (_reading is < 0 or > 1) return;
        int part = _reading;
        _reading = -1;
        if (_readCount != 3 || !Pending.TryGetValue(_readScope, out Partial? p)) return;
        if (_readKeys.Distinct(StringComparer.Ordinal).Count() != 3) return;
        p.Keys[part] = (string[])_readKeys.Clone();
        p.Ready[part] = true;
        TryComplete(p);
    }

    private static void TryComplete(Partial p)
    {
        if (!p.Ready[0] || !p.Ready[1] || p.Missing) return;
        string[] keys = p.Keys[0].Concat(p.Keys[1]).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != 6) return;
        // Distinguishes a real saved adjustment from the default 0s.
        if (!keys.All(k => PlayerPrefs.HasKey(k))) { p.Missing = true; return; }
        if (!TryContext(out _, out GripSnapshot ctx) || ctx.Scope != p.Snapshot.Scope) return;
        ctx.Keys = keys;
        ctx.Values = keys.Select(PlayerPrefs.GetFloat).ToArray();
        if (ctx.Values.Any(x => !float.IsFinite(x))) return;
        if (KnownFlags.TryGetValue(ctx.Scope, out var known) &&
            FlagMatchesKeys(known.key, keys))
        {
            ctx.FlagKey = known.key;
            ctx.FlagValue = known.value;
        }
        else if (LastKnown.TryGetValue(ctx.Scope, out GripSnapshot? last))
        {
            ctx.FlagKey = last.FlagKey;
            ctx.FlagValue = last.FlagValue;
        }
        LastKnown[ctx.Scope] = ctx;
        // Never label the freshly submitted values as a pre-install
        // "Original" if the first observable read happens DURING submit.
        if (!_submitting)
        {
            EnsureOriginal(ctx);
            TryImportAllClasses(ctx);
        }
        Status = "Grip values identified for " + ctx.ClassName;
    }

    private static void EnsureOriginal(GripSnapshot snapshot)
    {
        if (!PresetStore.HasOriginal(snapshot.Scope) && string.IsNullOrEmpty(PresetStore.Error))
        {
            var result = PresetStore.SaveNew("Original Grip (Imported)", snapshot, true);
            if (result.ok)
                Plugin.ModLog.LogInfo("[WeaponAdjustments/Presets] Protected original imported for " +
                    snapshot.ClassName + ".");
            else if (!result.message.Contains("already protected", StringComparison.Ordinal))
                Plugin.ModLog.LogWarning("[WeaponAdjustments/Presets] Original not imported: " +
                    result.message);
        }
    }

    private static void TryImportAllClasses(GripSnapshot sample)
    {
        // Only derive keys by exact known class-name substitution, AND only
        // when all six resulting keys exist in vanilla PlayerPrefs. No fake
        // default presets are generated for unknown classes.
        if (!TryContext(out object? avatar, out GripSnapshot live) ||
            live.Scope != sample.Scope) return;
        bool renewed = _avatarType?.Name == "cbn";
        object? classes = Member(avatar, renewed ? "beck" : "classObjects");
        if (classes is not IEnumerable enumerable) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? c in enumerable)
        {
            string? name = Member(c, renewed ? "bfav" : "className") as string;
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
        }
        if (names.Count == 0) return;
        // The preference keys can contain a class-specific prefix. If the
        // exact name isn't present once in every key, leave scope pending.
        foreach (string className in names)
        {
            if (className == sample.ClassName) continue;
            if (!sample.Keys.All(k => k.Split(new[] { sample.ClassName },
                    StringSplitOptions.None).Length == 2))
                continue;
            string[] candidate = sample.Keys.Select(k =>
                k.Replace(sample.ClassName, className, StringComparison.Ordinal)).ToArray();
            if (!candidate.All(PlayerPrefs.HasKey)) continue;
            var imported = new GripSnapshot
            {
                ClassName = className, Controller = sample.Controller,
                Scope = PresetStore.HashScope(
                    Member(_menuType, renewed ? "seu" : "currentPlayerName") as string ?? "",
                    className, sample.Controller),
                Keys = candidate, Values = candidate.Select(PlayerPrefs.GetFloat).ToArray(),
                FlagKey = sample.FlagKey != null && sample.FlagKey.Contains(sample.ClassName,
                    StringComparison.Ordinal)
                    ? sample.FlagKey.Replace(sample.ClassName, className, StringComparison.Ordinal) : null,
                FlagValue = 1
            };
            if (imported.Values.All(float.IsFinite) &&
                (imported.FlagKey == null || PlayerPrefs.HasKey(imported.FlagKey)))
                EnsureOriginal(imported);
        }
    }

    private static void OnVanillaSubmitBefore() => BeginSubmit();
    private static void OnVanillaSubmitAfter() => EndSubmit();

    internal static void BeginSubmit()
    {
        if (!_installed || !TryContext(out _, out _)) return;
        _submitting = true;
        SubmitFloats.Clear();
        _submitFlag = null;
        _submitFlagValue = 0;
    }

    private static void ObserveSetFloat(string __0, float __1)
    {
        if (!_submitting || _ourWrite || SubmitFloats.Count >= 8 ||
            string.IsNullOrEmpty(__0) || !float.IsFinite(__1)) return;
        SubmitFloats.Add((__0, __1));
    }

    private static void ObserveSetInt(string __0, int __1)
    {
        if (!_submitting || _ourWrite || _submitFlag != null ||
            string.IsNullOrEmpty(__0)) return;
        _submitFlag = __0;
        _submitFlagValue = __1;
    }

    internal static void EndSubmit()
    {
        if (!_submitting) return;
        _submitting = false;
        if (SubmitFloats.Count != 6 || _submitFlag == null ||
            _submitFlagValue != 1 ||
            !TryContext(out _, out GripSnapshot snapshot)) return;
        snapshot.Keys = SubmitFloats.Select(x => x.key).ToArray();
        snapshot.Values = SubmitFloats.Select(x => x.value).ToArray();
        snapshot.FlagKey = _submitFlag;
        snapshot.FlagValue = _submitFlagValue;
        if (snapshot.Keys.Distinct(StringComparer.Ordinal).Count() != 6 ||
            !FlagMatchesKeys(_submitFlag, snapshot.Keys)) return;
        // Do not call EnsureOriginal: a just-submitted grip is NEW, not
        // necessarily the user's original grip from before installation!
        KnownFlags[snapshot.Scope] = (_submitFlag, _submitFlagValue);
        LastKnown[snapshot.Scope] = snapshot;
        // Classes without a pre-existing adjustment should remain usable.
        // This is labelled FIRST CAPTURED, never falsely "Imported Original".
        if (!PresetStore.HasOriginal(snapshot.Scope))
        {
            var first = PresetStore.SaveNew("First Grip Captured After Install", snapshot, true);
            if (first.ok)
                Plugin.ModLog.LogInfo("[WeaponAdjustments/Presets] First newly saved grip protected. " +
                    "No claim is made that it was an old, pre-install adjustment.");
        }
        Status = snapshot.ClassName + ": adjusted grip captured";
    }

    internal static bool TryCurrent(out GripSnapshot snapshot)
    {
        snapshot = new GripSnapshot();
        if (!_installed || !TryContext(out _, out GripSnapshot ctx) ||
            !LastKnown.TryGetValue(ctx.Scope, out GripSnapshot? cached)) return false;
        if (cached.Keys.Length != 6 || !cached.Keys.All(PlayerPrefs.HasKey)) return false;
        snapshot = cached.Copy();
        if (snapshot.FlagKey == null && KnownFlags.TryGetValue(ctx.Scope, out var flag))
        {
            snapshot.FlagKey = flag.key;
            snapshot.FlagValue = flag.value;
        }
        snapshot.Values = snapshot.Keys.Select(PlayerPrefs.GetFloat).ToArray();
        return snapshot.Values.All(float.IsFinite);
    }

    internal static string SaveNamed(string name)
    {
        if (!TryCurrent(out GripSnapshot snapshot)) return "No verified current grip available yet.";
        if (!PresetStore.HasOriginal(snapshot.Scope))
            return "Original grip not yet backed up. Change class/reopen Settings to import first.";
        var result = PresetStore.SaveNew(name, snapshot);
        return result.message;
    }

    // The first time the user opens Settings, rerun the game's known
    // reset-and-apply routine once to read existing adjustment keys. This
    // doesn't write PlayerPrefs, and stops after one attempt per scope.
    private static readonly HashSet<string> FirstImportAttempts = new();
    internal static void TryImportOnFirstMenu()
    {
        if (!_installed || _ourWrite || _apply == null ||
            !TryContext(out object? avatar, out GripSnapshot ctx) || avatar == null ||
            PresetStore.HasOriginal(ctx.Scope) ||
            !FirstImportAttempts.Add(ctx.Scope)) return;
        try { _apply.Invoke(avatar, null); }
        catch (Exception ex)
        {
            Status = "Original import pending; native refresh failed: " + ex.GetType().Name;
        }
    }

    private static void RestoreKeys(GripSnapshot saved, object avatar)
    {
        if (saved.Keys.Length != 6 || saved.Values.Length != 6 ||
            saved.Exists.Length != 6 || saved.FlagKey == null)
            throw new InvalidOperationException("Invalid rollback snapshot");
        for (int i = 0; i < 6; i++)
        {
            if (saved.Exists[i]) PlayerPrefs.SetFloat(saved.Keys[i], saved.Values[i]);
            else PlayerPrefs.DeleteKey(saved.Keys[i]);
        }
        if (saved.FlagExists) PlayerPrefs.SetInt(saved.FlagKey, saved.FlagValue);
        else PlayerPrefs.DeleteKey(saved.FlagKey);
        PlayerPrefs.Save();
        _apply!.Invoke(avatar, null);
    }

    internal static string Load(string id)
    {
        if (!_installed || _apply == null || !TryContext(out object? avatar, out GripSnapshot ctx) ||
            avatar is not Component owner)
            return "Cannot verify active class/controller or native refresh; nothing changed.";
        GripPreset? p = PresetStore.List(ctx.Scope).FirstOrDefault(x => x.Id == id);
        if (p == null || p.Scope != ctx.Scope || p.ClassName != ctx.ClassName ||
            p.Controller != ctx.Controller || p.Keys.Length != 6 ||
            p.Values.Length != 6 || p.Values.Any(x => !float.IsFinite(x)) ||
            p.Keys.Distinct(StringComparer.Ordinal).Count() != 6)
            return "Preset mismatch/invalid values; nothing changed.";
        if (!PresetStore.HasOriginal(ctx.Scope)) return "Original not protected; load refused.";

        // Exact keys must come from a real native reader or submit event,
        // never from an unverified class-name substitution alone.
        if (!LastKnown.TryGetValue(ctx.Scope, out GripSnapshot? observed) ||
            !p.Keys.SequenceEqual(observed.Keys, StringComparer.Ordinal))
            return "Waiting for a verified live grip read for this class. No changes made.";
        string? flagKey = p.FlagKey ?? observed.FlagKey;
        if (flagKey == null && KnownFlags.TryGetValue(ctx.Scope, out var known) &&
            FlagMatchesKeys(known.key, p.Keys))
            flagKey = known.key;
        if (flagKey != null && !FlagMatchesKeys(flagKey, p.Keys))
            return "Enable key does not match the six grip keys; nothing changed.";
        if (string.IsNullOrWhiteSpace(flagKey) ||
            (observed.FlagKey != null && flagKey != observed.FlagKey))
            return "Vanilla enable-key not verified; load disabled rather than risk another class.";
        if (p.FlagValue != 1) return "Preset has an invalid enable flag; load refused.";

        var previous = new GripSnapshot
        {
            Scope = ctx.Scope, ClassName = ctx.ClassName, Controller = ctx.Controller,
            Keys = (string[])p.Keys.Clone(), FlagKey = flagKey,
            Exists = p.Keys.Select(PlayerPrefs.HasKey).ToArray(),
            FlagExists = PlayerPrefs.HasKey(flagKey),
            FlagValue = PlayerPrefs.HasKey(flagKey) ? PlayerPrefs.GetInt(flagKey) : 0
        };
        previous.Values = p.Keys.Select((key, i) =>
            previous.Exists[i] ? PlayerPrefs.GetFloat(key) : 0f).ToArray();
        if (previous.Values.Any(x => !float.IsFinite(x)))
            return "Previous grip is invalid; load refused.";
        var backup = PresetStore.BackupBeforeApply(previous);
        if (!backup.ok) return "Backup failed; load cancelled: " + backup.message;

        try
        {
            _ourWrite = true;
            if (!TryContext(out object? again, out GripSnapshot check) ||
                again is not Component after || check.Scope != ctx.Scope ||
                after.GetInstanceID() != owner.GetInstanceID())
                return "Character/class changed; no values altered (backup retained).";

            for (int i = 0; i < 6; i++) PlayerPrefs.SetFloat(p.Keys[i], p.Values[i]);
            PlayerPrefs.SetInt(flagKey, 1);
            PlayerPrefs.Save();
            _apply.Invoke(avatar, null);

            if (p.Keys.Where((key, i) =>
                Math.Abs(PlayerPrefs.GetFloat(key) - p.Values[i]) > 0.00001f).Any() ||
                PlayerPrefs.GetInt(flagKey) != 1)
                throw new InvalidOperationException("Preference readback mismatch");
            _undo = previous;
            LastKnown[ctx.Scope] = new GripSnapshot
            {
                Scope = ctx.Scope, ClassName = ctx.ClassName, Controller = ctx.Controller,
                Keys = (string[])p.Keys.Clone(), Values = (float[])p.Values.Clone(),
                FlagKey = flagKey, FlagValue = 1
            };
            Status = ctx.ClassName + ": " + p.Name + " loaded";
            return "Loaded " + p.Name + ". Restore Previous Grip is available.";
        }
        catch (Exception ex)
        {
            try { RestoreKeys(previous, avatar); }
            catch (Exception rollback)
            {
                Plugin.ModLog.LogError("[WeaponAdjustments/Presets] Recovery failed: " +
                    rollback.GetType().Name + "; durable before-load snapshot was retained.");
                return "Load failed AND automatic rollback failed. Use the saved backup before retrying.";
            }
            return "Load failed (" + ex.GetType().Name + "); previous grip restored.";
        }
        finally { _ourWrite = false; }
    }

    internal static string Undo()
    {
        if (_undo == null || !_installed || _apply == null ||
            !TryContext(out object? avatar, out GripSnapshot current) || avatar == null ||
            _undo.Scope != current.Scope)
            return "No previous grip for this active class to restore.";
        var undo = _undo.Copy();
        var currentBackup = new GripSnapshot
        {
            Scope = current.Scope, ClassName = current.ClassName, Controller = current.Controller,
            Keys = (string[])undo.Keys.Clone(), FlagKey = undo.FlagKey,
            Exists = undo.Keys.Select(PlayerPrefs.HasKey).ToArray(),
            FlagExists = PlayerPrefs.HasKey(undo.FlagKey!)
        };
        currentBackup.Values = undo.Keys.Select((key, i) =>
            currentBackup.Exists[i] ? PlayerPrefs.GetFloat(key) : 0f).ToArray();
        currentBackup.FlagValue = currentBackup.FlagExists ? PlayerPrefs.GetInt(undo.FlagKey!) : 0;
        var backup = PresetStore.BackupBeforeApply(currentBackup);
        if (!backup.ok) return "Undo backup failed; nothing changed: " + backup.message;
        try
        {
            _ourWrite = true;
            RestoreKeys(undo, avatar);
            _undo = null;
            // Keep the verified KEY identity even if the restored vanilla
            // default had no saved float keys. Save Current still rejects
            // unset keys, but a later Load can safely restore a preset.
            LastKnown[undo.Scope] = new GripSnapshot
            {
                Scope = undo.Scope, ClassName = undo.ClassName,
                Controller = undo.Controller, Keys = (string[])undo.Keys.Clone(),
                Values = (float[])undo.Values.Clone(), FlagKey = undo.FlagKey,
                FlagValue = undo.FlagValue
            };
            return "Previous grip restored. Before/after backups remain available.";
        }
        catch (Exception ex)
        {
            try { RestoreKeys(currentBackup, avatar); } catch { }
            return "Restore failed (" + ex.GetType().Name + "); backup preserved.";
        }
        finally { _ourWrite = false; }
    }

    internal static string ResetStatus() => Status;
}
