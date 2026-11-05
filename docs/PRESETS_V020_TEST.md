# Weapon Presets v0.2.0 — first live test plan

This is the first feature build, not a pre-verified release. Use a low-risk
class/grip first. Do **not** reset a valued vanilla grip until the
Original Grip (Imported) entry is visible and an additional backup exists.

## Build and install through RebornManager

```powershell
cd "$env:USERPROFILE\OneDrive\Documents\OrbusWeaponAdjustments"
git pull origin main
.\release.ps1
```

On successful publication, refresh RM and update WeaponAdjustments for the
Spades client. No manual DLL installation is necessary.

## 1 — Native UI and scope

- Open Orbus Settings > General > Adjust Weapon Grip. Expect the Weapon
  Presets section immediately below the two vanilla adjustment buttons.
- Equip Bard, Runemage and Bard again; the displayed class and preset list
  must switch automatically, without showing the other class's presets.
- Test a custom name in the native text field, including VR keyboard input.
- If class identity says pending or unknown, no write is allowed.

## 2 — First-launch originals, BEFORE any new adjustment

- With a pre-existing Bard grip, check that Original Grip (Imported) appears.
- Check the durable folder:
  %LOCALAPPDATA%\OrbusWeaponAdjustments\Presets\v1
- Verify Active and Originals both contain the original JSON record.
- Other classes import if the precise class-specific keys can be verified;
  otherwise import happens automatically upon first equip. An unset class
  must not get a fake original. If import says pending, stop and send log.

## 3 — Save, rename and separate classes

- Save two Bard names, e.g. Comfortable and Experiment. Reopen Settings
  and relaunch Orbus; both records should remain intact.
- Try a duplicate name and ensure the existing record is not overwritten.
- Rename an existing preset; verify its ID and values are preserved and
  a prior revision is still in Backups.
- Save a Runemage preset with the same name as a Bard preset.

## 4 — Deletion always confirms and can be restored

- Click Delete: expect only a Cancel / Confirm Delete prompt naming the
  preset and current class. Clicking Cancel changes nothing.
- Confirm Delete: move the preset to Trash, never permanently erase it.
- Show Trash -> Restore Preset must bring it back with identical values.
- Original immutable recovery copy must survive deleting the visible row.

## 5 — Load, verify, undo

- Back Up All Presets first. Check Exports has a new timestamped folder.
- Load a SMALL test adjustment for your current class.
- If the UI reports incomplete key mapping or enable flag, confirm it
  changed nothing: do not force unsafe values.
- On success, verify the grip physically moves in VR, not just in files.
- Use Restore Previous Grip, then verify it returns to the prior grip.
- Confirm Backups contains a before-load recovery record.
- Load must refuse a stale class/controller mismatch.

## 6 — Update safety and troubleshooting

- RM updates must not clear Active, Originals, Trash, Backups or Exports.
- Unknown schema or corrupt records must stop writes rather than erase.
- Legacy Ctrl+Alt+P/R/J/K/U diagnostics remain available.
- If anything fails, send BepInEx/LogOutput.log and the exact UI message.
  Do not publish preset JSON files publicly: those contain native grip
  preference keys with possible character-identifying information.
