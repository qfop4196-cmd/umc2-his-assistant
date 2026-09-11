@echo off
rem CHE DO DEMO / CHAM THI: may chu to khai voi du lieu gia + Cloudflare tunnel cho ca hai trang (giu cua so nay mo)
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0chay-demo.ps1"
echo.
pause
