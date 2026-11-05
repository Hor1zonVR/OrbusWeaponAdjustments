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
