@echo off
title WarAlarm
cd /d "%~dp0src\Reader"
echo.
echo   WarAlarm - starting live mode
echo   Map opens in your browser in about 10 seconds
echo   Map file: %CD%\map.html
echo   Press Ctrl+C to stop
echo.
start "" powershell -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 10; if (Test-Path '%CD%\map.html') { Start-Process '%CD%\map.html' }"
dotnet run
echo.
echo   WarAlarm stopped. Press any key to close.
pause >nul
