# Weapon adjustment: evidence-backed class map (2026-11-05)

This is a **metadata mapping**, not a decompilation of function bodies. Community
and Renewed/Spades dumps were supplied separately; their absolute RVAs and
TypeDefIndex values MUST NOT be substituted for one another. All updates to
vanilla game state are intentionally disabled until live behaviour is validated.

## Verified readable Community classes

| Class | Fields relevant to adjustment | Methods |
| --- | --- | --- |
| `PlayerMenu` | `document` (static document at offset 0x50) | `ToggleAdjustWeapon(UIEvent)`, `ResetAdjustWeapon(UIEvent)` |
| `MagicalActual.WeaponAdjuster` | `pi: PhysicsInteractable` (0x18); `toolController: ToolController` (0x20); `timeUntilDeath: float` (0x28); `isGripped: bool` (0x2C) | `Start`, `Update`, `SubmitAdjust`, `WeaponGripped`, `WeaponUngripped` |
| `MagicalActual.ToolController` | Main/offhand trigger/grip events and tracked-controller fields | `doMainTriggerClick`, `doMainGripClick`, etc. |
| `MagicalActual.Runemage` | `weaponAdjust: GameObject` (0xB8); `wand: GameObject` (0x68) | `ToggleAdjustWeapon()`, `WeaponAdjusted()` |
| `IPlayerClass` | — | `GetWeaponObject()`, `GetWeaponSkin()`, `ToggleAdjustWeapon()`, `WeaponAdjusted()` |

Community WeaponAdjuster method RVA: `SubmitAdjust = 0x4A9DA0` (COMMUNITY
ONLY, **do not** use for Renewed). The dump has only declarations and native
addresses, not the function implementation.

## Mapped Renewed/Spades classes

| Readable role | Obfuscated name | Basis |
| --- | --- | --- |
| PlayerMenu | `ng` | Identical 0x50 document offset, adjacent Settings fields, matching original HTML event handlers |
| PlayerMenu.ToggleAdjustWeapon | `ng.emh` | Extracted Renewed PlayerMenu HTML and method signatures |
| PlayerMenu.ResetAdjustWeapon | `ng.emi` | Extracted Renewed PlayerMenu HTML and method signatures |
| WeaponAdjuster | `cmi` | **Unique** four-field signature (two class refs, float at 0x28, bool at 0x2C), identical 5-method order |
| WeaponAdjuster.pi | `cmi.bhjc: my` | Field offset 0x18 |
| WeaponAdjuster.toolController | `cmi.bhjd: cle` | Field offset 0x20 |
| WeaponAdjuster.timeUntilDeath | `cmi.bhje` | float at 0x28 |
| WeaponAdjuster.isGripped | `cmi.bhjf` | bool at 0x2C |
| WeaponAdjuster.SubmitAdjust | `cmi.mnh` | Third non-constructor method, RVA 0x14BA430 in sampled Renewed dump |
| WeaponAdjuster.WeaponGripped | `cmi.mni` | Fourth non-constructor method |
| WeaponAdjuster.WeaponUngripped | `cmi.mnj` | Fifth non-constructor method |
| Runemage | `chr` | Matching wand/projectile field types and offsets across obfuscated and readable dumps |
| Runemage.weaponAdjust | `chr.bfyw` | GameObject at 0xB8 |

## Native settings UI target

The extracted Renewed PlayerMenu markup contains:

- `<h4>Adjust Weapon Grip</h4>`
- A paragraph describing how to grip and press trigger to apply the change.
- A `ul.menu-list` containing the two existing actions `ng.emh` and `ng.emi`.
- The next heading is `<h4>Relocation Anchors</h4>`.

The proper future insertion target is **after the vanilla adjust/reset list and
before Relocation Anchors**, preserving the existing PowerUI classes,
mousedown event handling and scroll container. RebornMenu already proves
`ng.sew` is the live native document and that `Dom.Element.insertAdjacentHTML("beforeend", html)`
is supported. The weapon-specific UI should be separate from the generic
BepInEx settings automatically exposed by RebornMenu.

## What the recorded tests established

- v0.1.3 observed the local `New Game Object/wandT2c(Clone)(Clone)` object.
- The parent repeatedly changed local position/rotation sharply with little
  world-space movement; this is consistent with attachment/reparenting and
  requires an actual parent/method trace before concluding precisely why.
- The weapon's own local transform stayed comparatively fixed. General
  controller movement is NOT evidence of persistent grip settings.
- The existing XR input API did not expose native trigger events on the test
  client; joystick button edges were visible, but ambiguous.
- No recording has yet confirmed a persistent per-class save format,
  network pathway, or safe call to apply numerical adjustments.

## v0.1.5 confirmed runtime evidence — Renewed/Spades

A user-run native report (2026-11-05 12:50) confirmed:
- All five native Harmony hooks installed: `ng.emh/emi` and `cmi.mnh/mni/mnj`.
- `ng.emh` before/after fired when opening vanilla weapon adjustment.
- `cmi.mni` / `cmi.mnj` fired repeatedly during grip/ungrip.
- `cmi.mnh` definitely fired on confirmation, with 7 ms between the recorded
  before/after timestamps.
- Immediately before submit, the adjustment object was parented beneath
  `PlayerRigv4/Controller (right)/Grabber/Hand/HoldPoint`.
- Immediately after submit, it was parented directly under
  `PlayerRigv4/Controller (right)`, with different *local* coordinates.
  Those before/after coordinates are in different reference frames and must
  **not** be interpreted as the saved weapon grip offset.

The tester also verified that each class has a separate grip adjustment,
that the adjusted grip survives class switches and new sessions, and that
vanilla Reset or the next class adjustment replaces it.

**What remains UNKNOWN:** the persistence implementation, which exact
vector/quaternion is saved, whether the data lives in Unity preferences,
a custom local file, player-avatar state, or a remote service. No native
function body is available in our decompiled C# metadata dump.

## Community/Renewed PlayerAvatar preference field difference

Another structural class match provides a useful caution.

Community `MagicalActual.PlayerAvatar` has the consecutive fields:

```text
0x524 xp                 int
0x528 neededxp           int
0x52C currentSuperCharge ushort
0x52E superActive        bool
0x530 inventory          string
0x538 equipment          string
0x540 playerPrefs        string
0x548 primaryWeaponHand  byte
```

Renewed/Spades `cbn` structurally matches `PlayerAvatar`, including its
opening fields `bool` + four `GameObject` refs + two `Transform` refs,
and a later aligned progression sequence:

```text
0x470 begi    int          [xp-compatible]
0x474 begj    int          [neededXP-compatible]
0x478 begk    ushort       [superCharge-compatible]
0x47A begl    bool         [superActive-compatible]
0x480 begm    string       [inventory-compatible]
0x488 begn    string       [equipment-compatible]
0x490 bego    Vector3      [basePosition-compatible]
```

Thus the **Community `playerPrefs` string has no corresponding field at
this point in the newer class layout**; we must not map it to `begn` or
assume its offset is unchanged. This does **not** establish whether
weapon grips are saved somewhere else in `cbn`, in different storage, or
server-side. Structural class mapping is metadata evidence, not runtime
verification of individual values.

## v0.1.6 targeted persistence instrumentation

To distinguish likely local preferences from other persistence paths, the
native trace now optionally observes **method calls only** to
`UnityEngine.PlayerPrefs.SetString/SetFloat/SetInt/Save/DeleteKey`
and the analogous `ObscuredPrefs` methods where that type exists.
No preference key, value, file content, player identifier, or network
payload is collected. Optional hook failures do not affect the five
already-proven native adjustment method hooks.

After `cmi.mnh` completes, delayed snapshots at 0.5, 1.5 and 4 seconds
check whether the temporary adjuster still exists and what its immediate
wand child / local transform look like. This is bounded to the submitted
object, not a full-scene search.

An observed local API write may be unrelated and must be correlated
with the actual submit timeline. The **absence** of write events
does not prove server-side storage; unhooked local/custom APIs and
network storage remain possible.

**Preservation rule:** keep the old `AdjustmentDiagnostics` and
`LiveAdjustmentCapture` components while the targeted native methods
and storage candidates are being validated.

## v0.1.6 confirmed Bard test and v0.1.7 next step

The Bard `WeaponMechanics_20261105_133920_335.txt` report showed two
independent `cmi.mnh` submissions, each containing **exactly one**
`Unity.PlayerPrefs.SetInt` call followed by **six** `SetFloat` calls, all
between the method prefix/postfix within approximately 8 ms. The pattern
was identical across both adjustments. The temporary `cmi` adjustment
object was destroyed before +0.5 s. Zero `PlayerPrefs.Save` calls
were recorded (this does NOT prove no eventual save).

This provides strong direct evidence that the vanilla adjustment handler
writes numeric settings through Unity PlayerPrefs. The exact six values,
raw preference keys, axes, units, and reset behaviour remain unverified.

v0.1.7 adds an isolated, submit-scoped argument trace to the existing
preference-method hooks. Only `SetFloat` values and small `SetInt`
values during actual `SubmitAdjust` are logged. Raw preference keys
remain held only transiently in RAM for per-trace anonymous K-slot
comparisons and are never written. The next controlled test compares
one translation and one rotation of the **same Bard** weapon to determine
which K-slots represent which motion. No settings are written by the mod.


## 2026-11-05 Ghidra breakthrough: save AND read routines identified

**This section supersedes the earlier unknown-axis/unknown-local-persistence
notes above.** The original readable *Community Edition* native
`GameAssembly.dll` was inspected at RVA `0x4A9DA0`
(`MagicalActual.WeaponAdjuster.SubmitAdjust`, VA `0x1804A9DA0`).
The method directly calls `UnityEngine.PlayerPrefs.SetInt` once
(adjustment flag = 1) then `UnityEngine.PlayerPrefs.SetFloat` six times.
The Bard runtime logs independently observed exactly this 1+6 pattern on
Renewed/Spades `cmi.mnh`, twice.

The six Community `SetFloat` keys are built with
`string.Concat(string[6])`, each using the same five preceding runtime
components and a distinct last global string reference:

| Order | Meaning | Last suffix string pointer in Community Ghidra |
| --- | --- | --- |
| 1 | rotation X | `DAT_181d26a00` |
| 2 | rotation Y | `DAT_181d26a08` |
| 3 | rotation Z | `DAT_181d26a38` |
| 4 | position X | `DAT_181d26a60` |
| 5 | position Y | `DAT_181d26a80` |
| 6 | position Z | `DAT_181d26aa8` |

**Crucial cross-check:** two native Community read functions use
`PlayerPrefs.GetFloat(string)` with those exact suffix references,
in the same axis order. This establishes actual save/read pairing:

- `FUN_180472750` = `RigBasedAdjustment.getPlayerAdjustRotation()`,
  returns a `Vector3` made from suffixes `26a00/26a08/26a38`.
- `FUN_180471F20` = `RigBasedAdjustment.getPlayerAdjustPosition()`,
  returns a `Vector3` made from suffixes `26a60/26a80/26aa8`.

The shown suffix symbols are **Community addresses of string references,
not the literal key suffix text**, and must NOT be hardcoded as key strings
or used as native addresses on Renewed/Spades.

`PlayerAvatar.MakeRigBasedAdjustments()` (Community
`FUN_18033EAA0`) enumerates `RigBasedAdjustment` descendants, invokes
`RigBasedAdjustment.Reset()` (`FUN_180471E00`) under appropriate
conditions, then uses `getPositionAdjustment(PlayerAvatar)`
(`FUN_180472F80`) and `getRotationAdjustment(PlayerAvatar)`
(`FUN_1804732E0`) to apply resulting local transforms. These call into
the player-preference readers. Thus **a plausible immediate reapply
sequence** is `PlayerPrefs.SetFloat` for all six fields followed by the
vanilla `PlayerAvatar.MakeRigBasedAdjustments()`. It remains necessary
to verify safe current-class/client behaviour in Renewed; never bypass
the vanilla reset/apply flow by directly accumulating transforms.

### Community to Renewed/Spades structural class map

Readable `MagicalActual.RigBasedAdjustment` = obfuscated `cgr` (same
ten fields in the same order/offsets, six `Vector3` adjustment fields,
`Quaternion` at 0x30, final `bool` at 0x88, matching method order).

| Community | Renewed/Spades metadata |
| --- | --- |
| `RigBasedAdjustment` | `cgr` |
| `rotationAdjustmentTouch` | `cgr.bfsl` |
| `positionAdjustmentTouch` | `cgr.bfsm` |
| `finalQuaternionTouch` | `cgr.bfsn` |
| `rotationAdjustmentSteam` | `cgr.bfso` |
| `positionAdjustmentSteam` | `cgr.bfsp` |
| `rotationAdjustmentIndex` | `cgr.bfsq` |
| `positionAdjustmentIndex` | `cgr.bfsr` |
| `Reset()` | `cgr.Reset()` |
| `getPositionAdjustment(cbn)` | `cgr.lvm(cbn)` |
| `getRotationAdjustment(cbn)` | `cgr.lvn(cbn)` |
| `getPlayerAdjustRotation()` | `cgr.lvo()` |
| `getPlayerAdjustPosition()` | `cgr.lvp()` |

The class and method signatures match in metadata; the newer functions'
internal logic and exact key format still need verifying against its binary
or a bounded live preset test.

### Recommended implementation milestone (replaces diagnostics-only loop)

Implement a local, versioned preset store containing per-class/per-controller
six-float snapshots plus the enabled flag, with keys captured from the
real vanilla adjustment save call (and kept local). Saving a preset must not
alter game PlayerPrefs. Loading should:

1. Require a validated active character/class/controller and a complete
   matching key set. Block cross-character/class/controller misapplication.
2. Backup current six `PlayerPrefs.GetFloat` values and enabled state,
   validate the selected preset and then atomically write the six
   `PlayerPrefs.SetFloat` values plus enabled flag, with rollback on error.
3. Trigger the **mapped vanilla rig refresh** rather than directly
   reparenting or overriding weapon transforms.
4. Offer a one-click rollback if the first load doesn't visibly take effect.

Expose Save / Load / Restore Previous Grip through native Settings,
directly below Adjust Weapon Grip buttons. Keep legacy diagnostics
available until loading is proven in both Community and Renewed/Spades.


## Preset safety contract (agreed 2026-11-05)

The user wants named, class-specific weapon-grip presets, with backups and
mandatory confirmation before any deletion. Treat these as **acceptance
requirements** for the first preset-capable release, not future polish.

### Identity, classes, and names

- Detect the currently equipped class automatically. The native
  **Adjust Weapon Grip** section shows **only presets for that class**.
  When no class can be identified safely, disable Save and Load instead of
  guessing. Class-switching must never delete or migrate other presets.
- Scope presets by **character + class + controller profile**. Capture and
  revalidate scope before both Save and Load, including if the class changes
  while the Settings panel is open. Never apply a preset belonging to another
  class/controller/character, even if a stale UI row is clicked.
- Every preset has an immutable unique internal ID (e.g. GUID), independent
  of its editable human-readable display name. File paths are based on IDs,
  **not user-entered names**. Two classes may use the same display name.
- Provide a native PowerUI text field / game-supported VR text entry for
  **Save Current as New Preset**, plus Rename later. Trim and validate names
  (non-empty, sensible maximum length, safely rendered as text rather than
  injected HTML). Avoid confusing duplicate names within the same scope.
- Save As **creates a new preset** by default. Overwriting an existing preset
  requires a distinct Replace action, a confirmation showing the target name,
  and a backup of the old revision. Never silently replace because the user
  typed an existing name.

### Storage and recovery

- Store local preset files **outside the mod DLL / release asset / mod
  install directory**, under a documented durable BepInEx user-data path.
  RebornManager updates, plugin version changes, or class switches must
  **not clear, overwrite, reset, or prune** that directory.
- Use a schema-versioned format; store an explicit class/controller/character
  scope, immutable ID, display name, created/modified timestamps, enabled
  flag and six verified grip values. Validate finite numbers and class
  identity before Save/Load. Unknown future schema versions are
  read-only and must not be overwritten with empty defaults.
- Prefer one independent data file per preset rather than replacing an entire
  collection with one save operation. Write changes using a temp file,
  flush it, validate it, then atomic rename/replace. Keep the previous
  good revision until the new one is definitely committed.
- Make a timestamped copy of the prior revision **before** Rename, Replace
  or Delete; provide a manual **Back Up / Export Presets** action. Do not
  silently expire backups in the first release.
- On startup, missing/corrupt/partially written files produce a visible
  recoverable error; never interpret read failure as "no presets" and then
  save an empty collection over good data.
- No raw storage keys, account details or character identifiers in ordinary
  diagnostic logs.

### Delete, undo, and protection from mistakes

- **Every Delete requires an explicit second-step confirmation** in the
  native UI identifying the preset's display name and class, with separate
  **Cancel** and **Confirm Delete** actions. No single-click destructive
  deletion; Delete must never fire when selecting or loading a preset.
- "Delete" moves the preset to a **recoverable Trash**, not irreversible
  deletion. Provide **Restore** and a one-click Undo confirmation after
  deletion. Backups and Trash stay intact across updates/restarts.
- Do not automatically empty Trash or delete backups. A potential permanent
  removal/cleanup feature is a separate explicit future decision, not part
  of the initial preset workflow.
- Loading a preset is **non-destructive to the saved preset**; before
  changing game PlayerPrefs, backup the currently active grip values and
  expose **Restore Previous Grip** for instant rollback.
- The first Load/apply version must fail closed if the vanilla key mapping,
  active class/controller, or mapped refresh mechanism cannot be verified
  on the actual client. Avoid writing directly into a weapon's transient
  transform and avoid claiming a successful apply without observing it.

### Native PowerUI target and acceptance tests

Controls must appear **directly below the vanilla Adjust Weapon Grip
buttons and above Relocation Anchors**. Native styling and VR interaction
should behave like other settings; RebornMenu's generic config controls
do not replace this interface.

Before declaring presets ready, test the following on Renewed/Spades:

1. Save two distinctly named Bard presets; switch to Runemage and verify
   neither Bard preset appears, then return and verify both are still there.
2. Save a Runemage preset with the same display name as a Bard preset;
   ensure each is isolated, retained after relaunch, and only applies to
   its class/controller scope.
3. Rename a preset and relaunch. Confirm the immutable ID and grip values
   survive; test a naming collision with an existing same-class preset.
4. Click Delete -> Cancel: preset remains. Click Delete -> Confirm:
   it moves to Trash and can be restored intact.
5. Simulate a malformed preset file and a failed/partial write: existing
   valid presets remain intact and user receives a safe error.
6. Confirm RebornManager update/reinstall does not touch presets, Trash
   or backups.
7. Load a preset, validate the actual visible grip change, then
   Restore Previous Grip and verify the undo. Repeat after a class switch.
