@echo off
rem UMC2 HIS Assistant - cai dat cho may bac si (khong can quyen Administrator)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-assistant.ps1" -AutoStart %*
echo.
pause
