@echo off
setlocal
cd /d "%~dp0"
echo Starting AROMOTION Studio setup...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-AROMOTION.ps1"
if errorlevel 1 (
  echo.
  echo Setup failed. Please read the error above.
  pause
)
endlocal
