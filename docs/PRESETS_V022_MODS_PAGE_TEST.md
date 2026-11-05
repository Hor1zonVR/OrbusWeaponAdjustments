# Weapon Presets v0.2.2 — RebornMenu layout and native keyboard

## Prepare

1. Publish RebornMenu v0.2.11 and WeaponAdjustments v0.2.2.
2. Install/update BOTH through RebornManager in your active instance.
3. Relaunch Orbus. Do not reset any valuable existing grip.

## UI verification

- General > Adjust Weapon Grip should have ONLY its vanilla toggle/reset.
- Mods > OrbusWeaponAdjustments should show Weapon Presets and current class.
- Verify the list has each name on the LEFT, Load/Rename/Delete to its RIGHT.
- Change Bard -> Runemage -> Bard. Never show another class's records.
- Original Grip (Imported) must still be present. No data migration occurs.

## Native VR name keyboard

- Select Save Current Grip. It should call OrbusKeyboard and show the
  game's actual VR typing surface, not an inert HTML input.
- Type a new distinct name, submit and verify the new row appears.
- Select Rename and verify the existing name is prefilled.
- Rename and verify its record survives restarting the game.
- If no keyboard opens, stop: send the exact UI message and LogOutput.log.
  No save or rename should occur if the callback could not be installed.

## Destructive and recovery safeguards

- Load one low-risk named preset, confirm the physical weapon changes,
  then Restore Previous Grip.
- Delete -> Cancel leaves the original record unchanged.
- Delete -> Confirm moves the selected record to Trash.
- View Trash -> Restore returns it without losing its name/values.
- Backup/Export works even when the current character is unavailable.
- Inspect %LOCALAPPDATA%\OrbusWeaponAdjustments\Presets\v1 to confirm
  old Active, Originals, Trash and Backups remain untouched after updates.

## Fail-safe

WeaponAdjustments works as a game plugin without RebornMenu, but the new
preset page requires RebornMenu v0.2.11+. RebornMenu remains functional
without WeaponAdjustments. The provider is an optional reflection hook.
Neither mod deletes old preset records or changes the v1 schema.

Report compiler errors or whether the native keyboard opened. The
Save/Load methods are still being validated on live Renewed/Spades.
