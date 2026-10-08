@echo off
title Finder Setup
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo Setup encountered an issue.
    pause
) else (
    echo.
    echo Setup completed successfully!
    timeout /t 3 >nul
)
