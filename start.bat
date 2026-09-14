@echo off
title WarAlarm
cd /d "%~dp0src\Reader"
echo.
echo   WarAlarm - starting live mode (auto-restart on crash / dead link)
echo   Map opens in your browser in about 10 seconds
echo   Map file: %CD%\map.html
echo   Press Ctrl+C to stop
echo.
start "" powershell -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 10; if (Test-Path '%CD%\map.html') { Start-Process '%CD%\map.html' }"

:loop
dotnet run
echo.
echo   WarAlarm exited (code %errorlevel%). Restarting in 5 s...  (close this window to stop)
timeout /t 5 /nobreak >nul
goto loop
