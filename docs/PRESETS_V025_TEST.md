# WeaponAdjustments v0.2.5: live verification

## Install / release workflow

Use the existing RebornManager install workflow; NEVER manually copy a DLL.

```powershell
cd "$env:USERPROFILE\OneDrive\Documents\OrbusWeaponAdjustments"
git pull origin main
.\release.ps1
```

After the script succeeds, update WeaponAdjustments in RebornManager and
restart Orbus. RebornMenu needs no update (v0.2.11 or newer works).

## 1. Adjustment without visiting General

1. Select Bard or Runemage. Go to Settings > Mods > Weapon Adjustments.
2. Click Adjust Weapon Grip. The native in-world adjustment model
   should appear. Place the weapon and press trigger to apply.
3. Click Adjust Weapon Grip again, if applicable, to toggle it off.
4. If it does not appear, send LogOutput: we now log
   'Called active-class weapon adjustment toggle' on dispatch,
   or 'Active-class adjust failed: <exception type>' on failure.
   Do not use Reset on a valuable saved grip.

## 2. Active state and UI

1. Imported Original Grip must NOT always be green just because it exists.
2. Load a safely backed-up named preset (e.g., 23). Its row should say
   IN USE in green. The current grip summary should say In use: 23.
   A successful load also shows a green 'Preset loaded' message.
3. If a second preset contains identical values, it may show MATCH.
   If multiple matches cannot be disambiguated after restarting Orbus,
   the summary states that several presets share identical values.
4. Alter the grip using the actual game's adjustment tool and confirm.
   If the new position/rotation no longer matches saved values,
   the previous preset should lose its IN USE marker.
5. Names and buttons should appear on separate rows and never overlap.
   Check long names and an original with (Imported) in its disk name.

## 3. Naming

Select Save Current Grip / Rename. The verified in-page keyboard should
open without attempting native OrbusKeyboard (which threw Il2CppException
in v0.2.4). Enter a name, Save Name, and confirm the new row. Verify
that cancel leaves all preset files unchanged. Names remain per class.

## 4. New class with no saved adjustment

Switch to Ranger if it has never had a saved native adjustment.
The page should say there is no saved adjustment yet and direct you to
Adjust Weapon Grip. Confirm the adjustment using the in-world trigger.
On a successful first native submit, the mod preserves a first-captured
backup, explicitly NOT a pre-install imported original. New names may then
be saved. No default/fake original may be generated.

## 5. Rotation, recovery and persistence

1. Save a distinctive position AND rotation using a test weapon.
2. Make a different adjustment and save as another name.
3. Load the first and examine BOTH in-world angle and grip position.
   The mod writes the six original native PlayerPrefs float values and
   calls PlayerAvatar.MakeRigBasedAdjustments. Numeric verification does
   not itself prove the visual rig's rotation: report any mismatch.
4. Use Undo Last Load, Trash/Restore and Back Up All Presets.
   Restart the game and confirm originals/named presets survived.

## Source-data safety

No existing PresetStore v1 format, keys, values, originals, backups, Trash
or import/export storage code changed in this release.
Backing up all presets makes a local file copy, not yet a portable
cross-client export/import feature. That's separate future work.

Send a fresh LogOutput and screenshot if the new adjustment button
does not appear or if rotations still seem wrong.
