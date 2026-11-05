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

## v0.1.5 next test

Hooks installed only after matching an exact type/field/method fingerprint:
`ng.emh`, `ng.emi`, `cmi.mnh`, `cmi.mni`, `cmi.mnj`.
Community equivalent classes are also supported if their named fingerprints
match. Hooks are observation-only; the player explicitly starts/stops a trace
with Ctrl+Alt+P, saved separately as `WeaponMechanics_*.txt`.

If `SubmitAdjust` fires, capture the component's pose and `isGripped`/
`timeUntilDeath` *before and after* submission and compare the existing
transform report for the same interaction. Do not infer the storage mechanism
from the method's name alone: for that, inspect the actual native call path
from the appropriate version's binary or discover the live persisted state.

**Preservation rule:** do not remove `AdjustmentDiagnostics` or
`LiveAdjustmentCapture` until the new hooks are proven by test logs.
