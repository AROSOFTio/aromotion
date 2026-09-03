@echo off
setlocal
cd /d "%~dp0"
title AROMOTION Studio Setup
echo.
echo AROMOTION Studio - Lossless Quick Recorder
echo ==========================================
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1"
if errorlevel 1 (
  echo.
  echo AROMOTION setup failed. Read the error above.
  pause
)
endlocal
