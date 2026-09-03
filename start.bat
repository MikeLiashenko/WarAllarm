@echo off
chcp 65001 >nul
title WarAlarm - живий режим
cd /d "%~dp0src\Reader"

echo.
echo   WarAlarm - запускаю живий режим...
echo   Карта: %cd%\map.html  (вiдкриється сама через 10 сек)
echo   Вихiд: Ctrl+C
echo.

rem відкрити карту в браузері, коли застосунок її створить
start "" powershell -NoProfile -WindowStyle Hidden -Command "Start-Sleep 10; if (Test-Path '%cd%\map.html') { Start-Process '%cd%\map.html' }"

dotnet run

echo.
echo   WarAlarm зупинено. Натисни будь-яку клавiшу...
pause >nul
