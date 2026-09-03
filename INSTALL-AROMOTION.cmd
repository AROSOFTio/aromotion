@echo off
setlocal
cd /d "%~dp0"
title AROMOTION Studio Installer
cls
echo.
echo =============================================
echo          AROMOTION STUDIO INSTALLER
echo =============================================
echo.
echo Installing the real Windows application...
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Install.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo AROMOTION installation did not complete. Error code: %ERR%
  echo Please send ChatGPT a screenshot of this window.
  pause
  exit /b %ERR%
)
echo Installation complete.
timeout /t 3 >nul
exit /b 0
