# OrbusWeaponAdjustments

Standalone BepInEx IL2CPP plugin for OrbusVR. **v0.2.5 adds verified active-grip highlighting, a clean two-row preset layout, and direct active-class weapon adjustment to the RebornMenu Mods page into WeaponAdjustments' own RebornMenu Mods page.** Vanilla General Settings is left untouched. Save and Rename use Orbus's native VR keyboard, and a compact class-specific list shows Load / Rename / Delete beside each named preset. Automatic originals, backups, Trash and the legacy diagnostics are preserved.

**First-test warning:** The v0.2.4 additions still require a live Spades test. The preset manager is under Settings → Mods → WeaponAdjustments, not in the vanilla General Settings section. The mod fails closed when the class, exact live PlayerPrefs keys, or vanilla rig refresh method cannot be verified. A preset cannot load until its class's six reader keys and enable flag are known, so a class may show "pending" on its first visit. Don't use Reset on a valuable grip until the initial Original backup appears.

Presets, Trash, immutable originals, exports and before-load backups live under `%LOCALAPPDATA%\\OrbusWeaponAdjustments\\Presets\\v1`, **outside RebornManager's instance and DLL update directories**. No network calls or manual DLL installation are required.

## Clone, build and publish for RebornManager

If the repository is already cloned, update it before publishing:

```powershell
cd "$env:USERPROFILE\OneDrive\Documents\OrbusWeaponAdjustments"
git pull origin main
.\release.ps1
```

If it is not yet cloned, use `gh repo clone Hor1zonVR/OrbusWeaponAdjustments` first.

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

## v0.2.0: first native weapon preset release

The add-on is inserted immediately after the game's existing **Toggle Weapon Adjustment / Reset** list, before **Relocation Anchors**. Native PowerUI handlers support the user's VR controls. The preset textbox uses a vanilla PowerUI text input. Existing Settings nodes are never removed or rebuilt.

- **Automatic scope:** only the currently active character/class/controller's presets appear. Switching classes changes the list; stale actions recheck the scope before any change.
- **Original backup:** opening Settings invokes the verified native rig refresh once to discover saved keys. If all six keys exist, the previous vanilla adjustment is saved as **Original Grip (Imported)**, and a second recovery copy is written before it can be changed. Other classes are imported when keys can be verified by exact class substitution and `PlayerPrefs.HasKey`, or on first equip. No fake originals are created for a default/unset class.
- **Save As / Rename:** custom names up to 48 characters. New saves never overwrite existing presets. Renames preserve a backup of the previous revision.
- **Load:** requires a validated six-key map, enabled flag, currently matching character/class/controller and an on-disk before-load rollback snapshot. Writes to the *game's existing* PlayerPrefs keys then calls the mapped native `PlayerAvatar.MakeRigBasedAdjustments` method. If the key readback fails, it attempts to restore the exact previous state.
- **Delete:** always shows an explicit Confirm Delete / Cancel step. Confirm moves the preset to recoverable Trash, not permanent deletion. Original recovery copies and backups are never purged automatically.
- **Restore Previous Grip:** undoes the most recent successful load for the same active class, retaining a recoverable before-undo backup.
- **Back Up All Presets:** copies all Active/Trash/Originals/Backups JSON records into a separate timestamped Exports directory.

**Limitations:** the renewed class mappings are metadata-verified but the v0.2.0 UI, GetFloat reader hooks and apply path require a first live test. The game may not expose an original's enable key until a native reset or a new adjustment event. In that case Load displays a safety message and **changes nothing**. Before reporting success, verify a class switch, a session relaunch and the physical weapon grip in VR. Don't assume that saving PlayerPrefs alone proves the rig visually refreshed.

See `docs/PRESETS_V020_TEST.md` for step-by-step tests. Existing v0.1.4-v0.1.7 diagnostic sections above are retained for troubleshooting.

## v0.2.1 context fix

After v0.2.0 successfully inserted the native Weapon Presets section but showed
"Character or class not ready", we corrected the character identity mapping.
The old code used PlayerMenu.currentPlayerName (Renewed ng.seu), a menu-target
field that may be unset or point to another player. v0.2.1 uses the actual
local PlayerAvatar.playerDbId (Renewed cbn.beix, mapped by contiguous native
field and property order). That identifier is hashed together with the
active class and controller, never written to ordinary logs. The same stable
identity is used for first-run cross-class imports. The menu now reports
exactly which context stage is missing, without printing any player IDs, and
**Back Up All Presets** remains available even when Save/Load are safely locked.

**First-run safety:** do not reset or experiment with a valuable existing
vanilla grip until the class-specific **Original Grip (Imported)** appears.
This is still the first unverified live preset implementation; please share the
new contextual status and log if the controls remain unavailable.

## v0.2.2 — dedicated RebornMenu mod page

Install BOTH RebornMenu v0.2.11+ and WeaponAdjustments v0.2.2+ through RM.
Open Settings > Mods > OrbusWeaponAdjustments, NOT General > Adjust Weapon Grip.
All original General Settings buttons are left untouched.

RebornMenu discovers an optional provider through reflection and gives this
mod its own 350px-wide right-side page, without depending on a shared DLL.
Other mods continue using their generic BepInEx Config.Bind settings UI.

- The active class/controller is displayed. Only that class's presets appear.
- Save Current Grip opens the actual Orbus VR keyboard for a custom name.
- Each preset row has three buttons: Load, Rename and Delete.
- Rename opens the native keyboard, initially filled with the saved name.
- Delete first requires Cancel or Confirm Delete and moves to Trash.
- Footer actions support Restore Previous Grip, View Trash and Backup/Export.
- The native keyboard mapping is PlayerCharacter.myKeyboard -> ceh.beue
  and OrbusKeyboard.ShowKeyboard(Action<string>) -> lu.efy(Action<string>).
- The keyboard callback is converted with the same IL2CPP DelegateSupport
  technique as RebornMenu's already working PowerUI event callbacks.

**This keyboard bridge requires its first live VR test.** If unavailable,
no Save/Rename mutation occurs; the error is shown in the page.

Preset storage paths and data formats DID NOT CHANGE. Previously imported
originals, named presets, Trash, Backups and exports remain at:
%LOCALAPPDATA%\OrbusWeaponAdjustments\Presets\v1

Focused test instructions: docs/PRESETS_V022_MODS_PAGE_TEST.md

## v0.2.3 — generic RebornMenu custom-page provider

WeaponAdjustments now advertises its page through the **v1 public
custom-page convention**, rather than requiring RebornMenu to hardcode
the WeaponPresetUi class or this plugin's GUID.

- `public const string RebornMenuModGuid` is set to the BepInPlugin GUID.
- `public static bool RenderInRebornMenu(object document, object content)`
  renders the page in the existing Mods right-side panel.
- **RebornMenu v0.2.12+** discovers any installed mod's corresponding
  provider in its own assembly; future mods can use this same convention
  without any RebornMenu source changes.
- No preset schema, durable storage, backup policy, loading mechanic,
  naming interface or class separation changed in this release.
- The v0.2.2-specific hardcoded hook still works with older
  RebornMenu v0.2.11 as a compatibility fallback until you upgrade.

See the RebornMenu repository's `docs/AUTOMATIC_MOD_SETTINGS.md` for
the generic provider contract and a copyable example.

## v0.2.4 — in-page adjustment, reliable naming and readable errors

- **Adjust Weapon Grip** now appears at the top of the dedicated Mods page,
  above Save. It invokes the original `PlayerMenu.ToggleAdjustWeapon(UIEvent)`
  handler (`ng.emh` on Renewed/Spades) with the current PowerUI pointer
  event. If the native event type cannot be verified, it refuses to invoke
  the method and tells the user to use General instead. The vanilla General
  button is **not removed or changed**.
- Clicking **Save Current Grip** / **Rename** first attempts the native
  Orbus VR keyboard (`lu.efy`). The IL2CPP callback bridge now accepts the
  supported managed or wrapped string argument signature.
- Whether the native keyboard opens or not, a temporary **in-page letter
  keyboard** offers 0–9, A–Z, space, hyphen, underscore, backspace,
  Save Name and Cancel. This uses the same verified PowerUI VR-click
  binding as the preset list, rather than requiring an HTML text input.
  No name is saved until Save Name is selected.
- The name display updates in place instead of rebuilding dozens of
  IL2CPP event listeners for each character.
- **Load Preset** refusal messages now explain in plain English why the
  grip wasn't changed; safe non-sensitive stage codes are logged to
  BepInEx. If Orbus has not verified the original's enable/save key, it
  is still protected and **loading remains disabled** rather than
  guessing or overwriting vanilla adjustments.
- No preset storage schema, file location, class scoping, original snapshot,
  backup, Trash or delete-confirmation behavior changed.
- Tested log from the previous v0.2.2 build confirmed successful mod
  startup, original page injection and character/class detection, but did
  **not** report a native keyboard exception. VR headset compositor errors
  in ErrorLog are not evidence that these game UI calls failed.

**RebornMenu itself needs no source update.** The existing custom-page
provider mechanism works with v0.2.11+; v0.2.12+ is preferred for the
generic developer API.

## v0.2.5 — active grip, cleaner list, working adjustment action

- **Only a verified current grip is green.** Green now means the six current
  PlayerPrefs grip values (three rotation, three position) match a stored
  preset AND its native enable flag has been verified. The list reports
  IN USE, MATCH (when two saved presets are identical), or BACKUP
  (for protected originals that are not currently active).
- The imported original stays named **Original Grip** in the menu without
  modifying its on-disk name or its protected backup. A user-renamed
  original retains its custom label.
- Current grip summary at top: in-use name, custom/unsaved, multiple
  identical presets or unverified. Never use a green indicator merely
  because a file has Original=true.
- Each preset has a dedicated name/status line, then a second line of
  equally spaced Load / Rename / Delete buttons, preventing overlap.
- After a successful Load, a clear green Preset loaded message is shown
  and the matching row is green. Loading STILL uses the same six-float
  readback, before-load rollback, per-class scope and enable-key checks.
- **Adjust Weapon Grip** now calls the *active PlayerClass* instance's
  native no-parameter ToggleAdjustWeapon method: Community
  PlayerClass.ToggleAdjustWeapon() maps to Renewed/Spades cei.lnv().
  The old ng.emh(UIEvent) bridge failed because the Mods pointer's
  MouseEvent wasn't the vanilla callback's UIEvent.
- Native OrbusKeyboard.lu.efy repeatedly threw Il2CppException in the
  v0.2.4 live log. We now use our verified on-page VR-pointer keyboard
  directly, without triggering those exceptions on Save or Rename.
- When a class (e.g. Ranger) has no pre-existing native saved grip, the
  page explains that the user must perform and confirm an adjustment
  before saving their first preset. It does not synthesize fake originals.
- The bottom controls are compact Undo Last Load / View Trash, with
  Back Up All Presets below. Backups are still local copies; portable
  cross-install Export/Import is not yet implemented.

**Unchanged:** PresetStore v1 schema, per-character/class/controller scope,
originals, backups, Trash, confirmed deletion, all six rotation/position
values, and existing diagnostics. The actual VR behaviour of cei.lnv()
still needs a live Spades test, as do the rotation visuals after loading.
RebornMenu v0.2.11+ already supports the page; no RebornMenu update.

See docs/PRESETS_V025_TEST.md for verification steps.
