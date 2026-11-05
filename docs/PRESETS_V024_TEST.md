# WeaponAdjustments v0.2.4 — focused VR test

These source changes do not change existing original-grip/preset JSON.

## Install

Use your existing clone, then publish through release.ps1 and update
WeaponAdjustments in RebornManager. No manual DLL installs.

```powershell
cd "$env:USERPROFILE\OneDrive\Documents\OrbusWeaponAdjustments"
git pull origin main
.\release.ps1
```

RebornMenu v0.2.11+ already supports this page; v0.2.12 is the generic
page-provider version. Neither needs updating for this v0.2.4 fix.

## What to check

1. Open Settings > Mods > OrbusWeaponAdjustments on Bard.
   Look for **Adjust Weapon Grip** above **Save Current Grip**.
2. Click Adjust Weapon Grip. It should toggle the SAME model as the
   vanilla General > Adjust Weapon Grip control. The General control
   must still work and no preset file should change.
3. Click Save Current Grip. If Orbus's VR keyboard opens, enter a name.
   Otherwise use the new inline letter/number keyboard (which is always
   visible while naming). Select Save Name, or Cancel to discard.
4. Check the new named preset row. Rename it with the same editor.
5. Load an Original Grip preset. If it cannot load yet, the message
   should say in plain English that it is backed up but cannot be
   applied safely; the in-game grip MUST remain unchanged.
6. Switch Bard > Runemage > Bard and confirm preset separation.
7. Delete > Cancel and Delete > Confirm/Delete > Trash > Restore.
   Verify backups and original snapshots remain intact after restart.

## How to report

Send the new LogOutput.log and a screenshot of the Mods page.
Any blocked preset load is now logged only as
  [WeaponAdjustments/Presets] Load safely stopped: <stage>
without character-specific preference keys or preset values.
Native keyboard attempts log either
  [WeaponAdjustments/Keyboard] Requested native Orbus keyboard.
or a sanitized reason that the native route was unavailable.

Important: the old uploaded 2026-11-05 16:46 log had
OrbusWeaponAdjustments 0.2.2 and RebornMenu 0.2.11.
It confirmed successful Mods-page injection and character/class
detection but contained no keyboard-specific exception.
The separate ErrorLog contained Oculus compositor/frame errors;
it does not attribute the naming failure to WeaponAdjustments.

If a load remains blocked, DON'T reset or re-save an irreplaceable
grip just to make it work. The original JSON backups are deliberate
and should remain untouched until the native apply mapping is proven.
