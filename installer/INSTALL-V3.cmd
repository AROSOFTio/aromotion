@echo off
setlocal
title AROMOTION Studio Installer V3
color 0B
cls
echo.
echo ============================================================
echo             AROMOTION STUDIO INSTALLER V3
echo ============================================================
echo.
echo No recursive scanning. No hidden failure.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-V3.ps1"
set RC=%ERRORLEVEL%
echo.
if not "%RC%"=="0" (
  echo ============================================================
  echo INSTALL FAILED - error code %RC%
  echo ============================================================
  echo Send ChatGPT a screenshot of the error shown above.
  echo.
  pause
  exit /b %RC%
)
echo ============================================================
echo INSTALLATION COMPLETED
echo ============================================================
echo AROMOTION Studio should now be open.
echo.
pause
exit /b 0
