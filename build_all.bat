@echo off
title WarAlarm - build all
rem One click: release\WarAlarm-Windows (.exe), WarAlarm.apk, WarAlarm.aab, WarAlarm.ipa (iOS via GitHub Actions).
rem   build_all.bat -NoIos   skips the iOS cloud build.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0app\build_all.ps1" %*
explorer "%~dp0release"
pause
