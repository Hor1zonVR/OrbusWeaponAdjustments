# OrbusWeaponAdjustments

Standalone, **read-only diagnostic plugin** for OrbusVR (BepInEx IL2CPP, Unity 2018). The goal is to understand the game's **existing Weapon Adjustment** workflow before adding native Settings controls, numeric fine-tuning, named presets and preset import/export.

Current version: **v0.1.7** — experimental diagnostics only. **No weapon changes, UI injection, network access, or preset storage.** The optional numeric trace records arguments to vanilla save calls only while SubmitAdjust executes; preference key names are never saved.

## Clone, build and publish for RebornManager

Open PowerShell anywhere:

```powershell
cd "$env:USERPROFILE\OneDrive\Documents"
git clone https://github.com/Hor1zonVR/OrbusWeaponAdjustments.git
cd OrbusWeaponAdjustments
.\release.ps1
```

The release script requires Git, GitHub CLI (`gh`, authenticated as `Hor1zonVR`) and a .NET SDK. It detects a RebornManager instance under `%LOCALAPPDATA%\OrbusRebornModdingInstances` with BepInEx-generated interop, compiles the DLL, pushes any local source changes, creates a **stable** GitHub release and uploads `OrbusWeaponAdjustments.dll`.

If GitHub release creation fails, the compiled DLL remains intact. Use `.\release.ps1` to retry; it checks for partially created releases and does not overwrite a complete release.

You can explicitly choose an instance for compilation:

```powershell
.\release.ps1 -GameDir "$env:LOCALAPPDATA\OrbusRebornModdingInstances\Modded Orbus"
```

To compile without publishing, use `.\release.ps1 -BuildOnly`. After a successful stable release, refresh RM's mod library and install OrbusWeaponAdjustments to the instance you want.

## Test 1: capture the full Weapon Adjustment interaction

1. Launch your chosen OrbusVR instance with the mod, equip your Runemage wand and open the game's **existing Weapon Adjustment** menu.
2. Press **Ctrl+Alt+R** on the desktop keyboard to **ARM** the live capture **before** clicking Orbus's adjustment button.
3. Click the real in-game adjustment button, then move and rotate your wand naturally.
4. Press the controller trigger to confirm. The recorder records XR trigger and raw joystick button edges. Press **Ctrl+Alt+R again to stop manually** after confirming in Orbus. Automatic end detection is off by default because the tested client did not expose the XR trigger API.
5. Locate `BepInEx\WeaponAdjustmentDiagnostics\WeaponAdjustments_Live_*.txt` within your selected Orbus instance.

The recorder prioritises explicitly named **wand, weapon, adjustment and holder** objects plus a small number of controller anchors, samples likely adjustment/wand/hand/controller/menu transforms around 10 times per second, notes changes to their local positions and rotations, and performs scheduled active-scene rescans to catch objects created when adjustment mode starts, even when the XR trigger API is unavailable. It times out after 90 seconds. Ctrl+Alt+M optionally adds a timestamp marker. The scans may briefly hitch in VR. Test somewhere stationary and safe.

**Important:** The inferred trigger start/end markers are *not* verified game callbacks or proof of persistence. Some clients may not expose XR trigger state, in which case manual stop is necessary. The optional `LiveCapture.AutoStopOnSecondTrigger=true` heuristic is unreliable unless an XR trigger API is available. Keep it disabled by default and stop manually.

## Test 2: compare before and after

- **Ctrl+Alt+J:** capture a baseline before touching the adjustment.
- Change the wand using Orbus's built-in adjustment.
- **Ctrl+Alt+K:** compare the current Unity objects against the baseline and save a report.
- **Ctrl+Alt+U:** save a standalone snapshot with candidate component/type/member *names*, not member values.

Reports are saved in `BepInEx\WeaponAdjustmentDiagnostics` in the launched instance. Before sharing them, review object paths; they can contain player/character names. No passwords, raw UI text, login tokens or network data are recorded.

## Planned functionality (not implemented)

- Native Settings controls immediately below Orbus's Weapon Adjustment UI.
- Named presets that restore verified adjustment values.
- Precise X/Y/Z movement and rotation sliders.
- Undo/history, backups, and import/export preset codes.

First verify what values the original game changes and saves; do not assume a moving hand transform is the actual persistent weapon offset.

## v0.1.7 diagnostic changes

Prior recordings proved that the player's PlayerRigv4 controllers moved but a large number of finger bones crowded out more useful objects. This version uses direct object-name checks, captures the actual wand clone and its parent transforms, watches changes in both local and world coordinates, and lists component type names on newly watched objects.

For a useful recording, capture a clear before/after adjustment for roughly 10–20 seconds. Press Ctrl+Alt+R to start, perform a single adjustment, then Ctrl+Alt+R to stop. If possible, also take a Ctrl+Alt+J baseline before adjustment and Ctrl+Alt+K comparison afterward. No adjustment settings are modified by this plugin.

## Focused parent-change investigation (v0.1.7)

The previous recording caught the actual `New Game Object/wandT2c(Clone)(Clone)` and large local position jumps on its parent, while world position stayed nearly the same. This strongly suggests that the parent gets reattached to a hand/controller transform, but does not prove where the game stores the final adjustment.

The live diagnostic now writes **PARENT CHANGE** events with old/new parent paths and IDs, plus the wand's local/world pose. It also reports native IL2CPP component class names (instead of generic `Component`) for likely wand holders, and captures last-known values when a temporary wand is destroyed.

Suggested test: start live capture while the wand is equipped, perform one vanilla weapon adjustment, confirm it, then manually stop capture. Send the `WeaponAdjustments_Live_*.txt` report. If there are no parent-change events, take a `Ctrl+Alt+U` snapshot after the adjustment as a follow-up. Everything remains read-only.

## Test 3: mapped native method events (v0.1.7)

The readable Community dump has `MagicalActual.WeaponAdjuster` with `SubmitAdjust`, `WeaponGripped` and `WeaponUngripped`. Structural matching to the Renewed/Spades dump identifies `cmi.mnh`, `cmi.mni`, and `cmi.mnj` respectively. The native Settings UI routes its original buttons through `ng.emh` (toggle adjustment) and `ng.emi` (reset).

Unlike the older scene scanning, **this test patches only those five signature-checked methods** with observational Harmony callbacks. It never invokes submit, changes a transform, edits settings, or touches the Settings HTML.

1. Load WeaponAdjustments v0.1.7; check the BepInEx log for `[WeaponAdjustments/Methods] READ-ONLY hooks installed`. If it says the signatures do not match, **nothing is patched**.
2. With Runemage equipped and the game's native Settings open, press **Ctrl+Alt+P** to start the method trace.
3. Click **Toggle Weapon Adjustment**, grip/move the spawned weapon, and press the VR trigger to apply the adjustment.
4. Press **Ctrl+Alt+P** again. This writes `BepInEx\WeaponAdjustmentDiagnostics\WeaponMechanics_*.txt`.
5. Send that report. It should reveal the actual order of menu toggle, grip, ungrip, and **submit** callbacks, together with the adjustment component's local/world transform and its grip/timer fields.

A separate **Reset** test is possible, but avoid pressing Reset during the first trace so it doesn't erase your chosen adjustment. The old live/snapshot/baseline diagnostics remain installed, with their original shortcuts. All method hooks can be disabled through the BepInEx `NativeMethodTrace.EnableReadOnlyHooks` config entry (restart required), which RebornMenu may display automatically.

**Research boundary:** A dump provides class layouts and method names, not the bodies of the game functions. `SubmitAdjust` is strongly identified as the submit handler, but until a native trace fires and the actual persistent data path is investigated, no preset/save implementation is safe.

## v0.1.7: identify candidate persistence APIs

Confirmed in-game on Renewed/Spades: `ng.emh` invokes vanilla adjustment, grip/ungrip events fire through `cmi.mni/mnj`, and `cmi.mnh` runs on submit. Its instance is reparented from the controller's HoldPoint to the controller on submit. Player testing confirms class-specific grip adjustments persist between sessions, but **the save mechanism remains unidentified**.

**Targeted optional storage probe:** while the `Ctrl+Alt+P` native trace is armed, v0.1.7 can observe calls to the standard Unity `PlayerPrefs.SetString/SetFloat/SetInt/Save/DeleteKey` APIs, plus the `CodeStage.AntiCheat.ObscuredTypes.ObscuredPrefs` equivalents when present. It logs method names and timestamps **only**, never keys, saved values, player IDs, network payloads, or file contents. Hooks are optional and isolated from the five proven adjustment hooks. `StorageProbe.ObserveLocalPreferenceWrites` (default `true`) can be switched off via BepInEx/RebornMenu; restart required.

The same native trace also records the temporary adjustment object's local pose at approximately 0.5, 1.5 and 4 seconds after `SubmitAdjust`, plus the direct wand child when still present. The old live timeline and snapshot shortcuts are unchanged.

**Important limitations:** No observed PlayerPrefs write does **not** establish that saving is server-side: the game may use an unrelated local format, direct storage, delayed writes, network services, or methods not patched here. The IL2CPP dump contains only metadata, not actual `SubmitAdjust` implementation; the matching Community `GameAssembly.dll` or verified runtime data is required to establish the complete call path.

### Focused test

1. Refresh/install v0.1.7 through RebornManager. Equip Runemage and open Settings → Adjust Weapon Grip.
2. Press **Ctrl+Alt+P**, then use Toggle Weapon Adjustment, grip/rotate, and confirm with the VR trigger.
3. Keep the same session running for **at least 7 more seconds** so the delayed samples are captured.
4. Press **Ctrl+Alt+P** to stop. Send `WeaponMechanics_*.txt` and the BepInEx log.
5. Do **not** press Reset for this test. For a control sample, optionally repeat while simply toggling Settings without making an adjustment.

If the optional preference-method hooks fail, the core adjustment trace still works. No weapon positions, values, or saving routines are changed by this release.

## v0.1.7: identify the six grip settings using their actual native writes

The v0.1.6 **Bard** recording confirmed the same seven calls in each of two consecutive native `SubmitAdjust` executions: one `UnityEngine.PlayerPrefs.SetInt` and **six `UnityEngine.PlayerPrefs.SetFloat`**. The first submit completed at 13:39:26.186 and the second at 13:39:38.137 (November 5, 2026). The adjustment object disappears by 0.5 s afterward. No `PlayerPrefs.Save` call appeared in that trace. These calls strongly implicate Unity PlayerPrefs in grip persistence, though the precise key/value mapping is still a hypothesis.

The optional `StorageProbe.ObserveLocalPreferenceWrites` (default true) now captures **the actual six float arguments** when `SetFloat` occurs *inside* the validated `cmi.mnh` or Community `SubmitAdjust` call, using a 500 ms hard window and maximum 16 writes per submission. A small int argument (range -16 to 255) is captured only inside that same window. **Raw preference keys are never written to disk**; memory-only string comparisons assign stable `K1`, `K2`, etc. aliases for one armed trace. These aliases are cleared when the report stops. No other game settings, user identifiers, network data, or raw strings are recorded.

Example of the expected output format (illustrative values, not real game findings):

```text
ADJUST.SubmitAdjust BEFORE ...
SUBMIT_WRITE capture began (500ms safety limit)
SUBMIT_WRITE #1 api=Unity.PlayerPrefs slot=K1 type=int value=3 (raw key withheld)
SUBMIT_WRITE #2 api=Unity.PlayerPrefs slot=K2 type=float value=0.05 (raw key withheld)
...
SUBMIT_WRITE capture ended; count=7
ADJUST.SubmitAdjust AFTER ...
```

### Short test to distinguish X/Y/Z position from rotation

1. Update/install v0.1.7 through the normal `release.ps1` and RebornManager flow.
2. Keep Bard equipped. Open Settings > Adjust Weapon Grip. Press `Ctrl+Alt+P`.
3. Make **one deliberate translation** (move the adjustment instrument sideways but minimise rotation), then confirm.
4. Re-enter adjustment mode and make **one deliberate rotation** (rotate in place but minimise translation), then confirm.
5. Press `Ctrl+Alt+P` to save and send the report. The same anonymous K slots will appear in both submissions and differences in the six float values will indicate which saved components respond to translation vs rotation.

If preferred, do one safe small adjustment to minimise disruption to your existing Bard grip. No Reset needed.

**Interpretation caution:** 6 floats suggest 3 position + 3 Euler angles, but order, units, coordinate frame, and key format are still unverified. Never use experimental values to write settings until that mapping is established. Existing v0.1.4 scene diagnostics and v0.1.5 method hooks remain available.
