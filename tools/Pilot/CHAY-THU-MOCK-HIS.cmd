@echo off
rem Luot 1 PILOT-CHECKLIST: may chu to khai tam (localhost) + Mock HIS + tro ly de thu tay
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0chay-mock-his.ps1"
echo.
pause
