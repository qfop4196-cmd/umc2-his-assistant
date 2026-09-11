@echo off
rem BUILD LAI + CHAY DEMO: bien dich lai IntakeServer (co vai tro Bac si) roi khoi dong may chu demo. Giu cua so nay mo.
chcp 65001 >nul
echo === Buoc 1/2: Build lai (tu dong tat may chu cu) ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -NoZip
if errorlevel 1 (
  echo.
  echo *** BUILD LOI - chup man hinh cua so nay gui cho Claude ***
  pause
  exit /b 1
)
echo.
echo === Buoc 2/2: Khoi dong may chu demo (dia chi tunnel se hien ben duoi) ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Pilot\chay-demo.ps1"
echo.
pause
