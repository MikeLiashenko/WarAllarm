@echo off
title WarAlarm
rem Reader now runs 24/7 on the Google Cloud server (deploy/deploy-server.sh). Two Readers on
rem the same Telegram session can get it revoked — so starting here asks first.
echo.
echo   !!! WarAlarm Reader already runs on the SERVER (Google Cloud, 24/7).
echo   !!! Starting it here too = two copies on one Telegram session (Telegram may revoke it).
echo   Start ONLY if the server is stopped. Type YES to start anyway:
set /p ok=^>
if /i not "%ok%"=="YES" exit /b
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
