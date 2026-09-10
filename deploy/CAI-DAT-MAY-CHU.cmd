@echo off
rem UMC2 Intake Server - cai dat may chu to khai (tu xin quyen Administrator)
cd /d "%~dp0"
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Dang xin quyen Administrator...
  powershell -NoProfile -Command "Start-Process -FilePath \"%~f0\" -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-server.ps1" %*
echo.
pause
