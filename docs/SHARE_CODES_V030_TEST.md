# WeaponAdjustments v0.3.0 — offline share code test

## Release / install

Publish v0.3.0 through your existing release.ps1 and refresh RebornManager.
Only WeaponAdjustments changes. RebornMenu's existing generic Mods page
provider already supports this update; do not install DLLs manually.

```powershell
cd "$env:USERPROFILE\OneDrive\Documents\OrbusWeaponAdjustments"
git pull origin main
.\release.ps1
```

## 1. Export without a file

1. Equip Bard and open Settings > Mods > Weapon Adjustments.
2. Each saved preset now has Load / Rename / Delete / Share.
3. Click Share for a preset and check the code is copied to the
   Windows clipboard; paste it into Notepad or Discord.
4. The code starts OW1- and is separated into easy-to-read groups.
   A copy is also shown on the mod page for verification.
5. Sharing should not change the currently equipped grip or any
   Active/Trash/Originals preset JSON.

## 2. Import with an automatic name

1. Copy an OW1 code from another player (or another character with
   different grip values). Open Import Code (Paste from Clipboard).
2. Check the preview shows the intended class, compatible controller,
   next unused Imported N name, and rotation/position preview.
3. Press Cancel and verify that no new preset appears.
4. Paste again, Confirm Import and verify the name Imported 1.
5. Import another distinct compatible code: Imported 2, then 3.
   The name collision check includes Trash so a deleted Imported 2
   can still be restored without colliding with a new import.
6. The import must NOT change the current grip: it only adds a
   schema-2 keyless preset under that class's collection.
7. Click Rename and use the existing native pointer keyboard to
   rename Imported 1. Its ID and exact values must be preserved.

## 3. Import for another class

While equipped as Runemage, paste a Bard code. The preview should
say Bard, and confirmation should save Imported N to Bard's collection.
Nothing should appear in Runemage's preset list. Switch to Bard;
its imported preset should now appear. If no original Bard grip has
ever been saved on this character, Load must remain safely blocked
until the vanilla Bard adjustment keys have been captured.

## 4. Load the RECEIVER's own keys

On an already adjusted class with verified original grip, Load the
imported code and inspect both position AND rotation in VR.
Successful load should highlight that matching preset as IN USE.
Use Undo Last Load and verify your exact previous grip returns.
The importing player's native PlayerPrefs key names, character ID
and save-enabled key are NOT transmitted or stored in imported JSON.
The receiver's six live verified keys are used only when loading.

## 5. Safety / compatibility

- Try the same grip twice: duplicate values should be rejected.
- Change one code character: integrity checksum must reject it.
- Paste ordinary Discord text: invalid format, no preset created.
- Try a code for an incompatible controller: import must be blocked.
- Test Copy when Windows clipboard is locked/unavailable: the code
  should remain visible; no game preferences should change.
- Test Import without clipboard text: in-page uppercase letter/number
  keyboard should appear for manual code entry and correction.
- Test Original Grip, Trash, Rename, Save New and local Back Up All
  Presets with existing v1 records. No migration/overwrite occurs.
- Test restarting Orbus; active, imported, backup and trashed files
  must remain intact.

## Code format

OW1 codes are offline Base32 with a version byte, class index, a
4-byte non-reversible controller-profile fingerprint, six unrounded
IEEE-754 float32 values (rotation XYZ then position XYZ), and a
4-byte SHA-256-derived corruption check. Codes carry NO character
identity, per-user PlayerPrefs keys, network location or preset name.
Codes are about 70 characters and are intended to be pasted from
Discord, not laboriously typed. The checksum is for accidental
corruption detection, not proof that the sender is trustworthy.

Imported records are schema 2 and intentionally contain only the
recipient's character/class/controller scope and the six values.
Existing schema 1 files are read unchanged. A downgrade to a version
without schema 2 support may lock preset editing for safety;
reinstall v0.3.0+ to regain access. Never delete data to fix that.

## Troubleshooting

Send BepInEx/LogOutput.log, the in-game message and a screenshot.
Share code text itself contains only the disclosed adjustment data,
but still avoid publishing private game log information publicly.
v0.3.0 source is not precompiled against your local Spades instance;
the first test must verify Win32 clipboard behaviour and all six
values on real weapons.
