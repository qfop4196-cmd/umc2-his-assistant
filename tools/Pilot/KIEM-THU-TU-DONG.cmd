@echo off
rem Luot 1 PILOT-CHECKLIST: build + kiem thu tu dong (ServerFlowTest + SmokeTest voi Mock HIS)
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0kiem-thu.ps1"
echo.
pause