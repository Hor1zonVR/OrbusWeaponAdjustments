using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace OrbusWeaponAdjustments;

[BepInPlugin("com.horizon.orbus.weaponadjustments", "OrbusWeaponAdjustments", "0.2.4")]
public sealed class Plugin : BasePlugin
{
    internal static BepInEx.Logging.ManualLogSource ModLog = null!;
    internal static ConfigEntry<bool> RequireFocus = null!;
    internal static ConfigEntry<bool> IncludeTypeNames = null!;
    internal static ConfigEntry<bool> AutoStopOnSecondTrigger = null!;
    internal static ConfigEntry<bool> EnableNativeMethodTrace = null!;
    internal static ConfigEntry<bool> ObserveLocalPreferenceWrites = null!;

    public override void Load()
    {
        ModLog = Log;
        RequireFocus = Config.Bind("Diagnostics", "RequireGameFocus", true,
            "Only react to keyboard shortcuts while the game window is focused.");
        IncludeTypeNames = Config.Bind("Diagnostics", "IncludeTypeMetadata", true,
            "Include matching class/member names (not their values) in snapshot reports.");
        AutoStopOnSecondTrigger = Config.Bind("LiveCapture", "AutoStopOnSecondTrigger", false,
            "Infer the end of adjustment from a second VR trigger down at least 1.5s after the first.");
        EnableNativeMethodTrace = Config.Bind(
            "NativeMethodTrace", "EnableReadOnlyHooks", true,
            "Hook the signature-verified Orbus WeaponAdjuster and vanilla PlayerMenu " +
            "events for opt-in Ctrl+Alt+P diagnostics. Read-only; restart required when changed.");
        ObserveLocalPreferenceWrites = Config.Bind(
            "StorageProbe", "ObserveLocalPreferenceWrites", true,
            "Read-only submit-scoped PlayerPrefs probe. Captures the six candidate grip " +
            "floats (and small integers) with anonymous key slots while Ctrl+Alt+P is active. " +
            "Key names are never saved. Restart required.");
        PresetStore.Init();
        NativeAdjustmentTrace.Install();
        PersistenceProbe.Install(Log);
        GripEngine.Install();
        AddComponent<NativeAdjustmentTraceBehaviour>();
        AddComponent<WeaponPresetUi>();
        AddComponent<AdjustmentDiagnostics>();
        AddComponent<LiveAdjustmentCapture>();

        Log.LogInfo("[WeaponAdjustments v0.2.4] Weapon grip button and reliable naming fallback on Mods page; diagnostics preserved.");
        Log.LogInfo("[WeaponAdjustments/Presets] Durable storage: " + PresetStore.Location);
        if (!string.IsNullOrEmpty(PresetStore.Error))
            Log.LogWarning("[WeaponAdjustments/Presets] " + PresetStore.Error);
        Log.LogInfo("[WeaponAdjustments] Ctrl+Alt+J = baseline; Ctrl+Alt+K = compare; Ctrl+Alt+U = snapshot.");
        Log.LogInfo("[WeaponAdjustments] Ctrl+Alt+R = arm/stop LIVE capture; Ctrl+Alt+M = timestamp marker.");
        Log.LogInfo("[WeaponAdjustments] Ctrl+Alt+P = trace native adjustment and local preference write API calls.");
        Log.LogInfo("[WeaponAdjustments] Only SubmitAdjust-scoped numeric writes are captured; preference key names are never saved.");
        Log.LogInfo("[WeaponAdjustments] RebornMenu Mods tab displays class-scoped Save/Load/Rename/Delete/Trash/Restore controls.");
        Log.LogInfo("[WeaponAdjustments] Load only runs after exact live key verification and a durable rollback backup.");
    }
}
