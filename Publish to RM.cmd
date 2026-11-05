@echo off
cd /d "%~dp0"
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
set "RESULT=%ERRORLEVEL%"
echo.
if not "%RESULT%"=="0" (
  echo Publishing failed. Copy the error above and send it for debugging.
) else (
  echo Publishing complete. Open RebornManager, refresh, and install OrbusWeaponAdjustments.
)
echo.
pause
exit /b %RESULT%
