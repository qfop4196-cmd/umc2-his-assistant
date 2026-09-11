@echo off
rem Xoa du lieu thu cua may chu tam (tai khoan, to khai, ghep noi) roi mo lai Mock HIS + tro ly
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0chay-mock-his.ps1" -Reset
echo.
pause
