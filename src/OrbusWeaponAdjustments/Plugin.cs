using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace OrbusWeaponAdjustments;

[BepInPlugin("com.horizon.orbus.weaponadjustments", "OrbusWeaponAdjustments", "0.1.2")]
public sealed class Plugin : BasePlugin
{
    internal static BepInEx.Logging.ManualLogSource ModLog = null!;
    internal static ConfigEntry<bool> RequireFocus = null!;
    internal static ConfigEntry<bool> IncludeTypeNames = null!;
    internal static ConfigEntry<bool> AutoStopOnSecondTrigger = null!;

    public override void Load()
    {
        ModLog = Log;
        RequireFocus = Config.Bind("Diagnostics", "RequireGameFocus", true,
            "Only react to keyboard shortcuts while the game window is focused.");
        IncludeTypeNames = Config.Bind("Diagnostics", "IncludeTypeMetadata", true,
            "Include matching class/member names (not their values) in snapshot reports.");
        AutoStopOnSecondTrigger = Config.Bind("LiveCapture", "AutoStopOnSecondTrigger", false,
            "Infer the end of adjustment from a second VR trigger down at least 1.5s after the first.");
        AddComponent<AdjustmentDiagnostics>();
        AddComponent<LiveAdjustmentCapture>();

        Log.LogInfo("[WeaponAdjustments v0.1.2] READ-ONLY diagnostics loaded.");
        Log.LogInfo("[WeaponAdjustments] Ctrl+Alt+J = baseline; Ctrl+Alt+K = compare; Ctrl+Alt+U = snapshot.");
        Log.LogInfo("[WeaponAdjustments] Ctrl+Alt+R = arm/stop LIVE capture; Ctrl+Alt+M = timestamp marker.");
        Log.LogInfo("[WeaponAdjustments] Does not edit weapon values, inject UI, or access network.");
    }
}
