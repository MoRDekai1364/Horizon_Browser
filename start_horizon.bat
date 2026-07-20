@echo off
title Horizon Stealth Console
color 0A

echo ==================================================
echo   HORIZON STEALTH // LAUNCHER
echo ==================================================

dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    color 0C
    echo [CRITICAL] .NET SDK not found. Install .NET 8 SDK.
    pause
    exit /b
)

echo [INFO] Booting Core...
echo [INFO] Logs redirected to /logs
echo.

call dotnet run

if %errorlevel% neq 0 (
    color 0C
    echo.
    echo ==================================================
    echo [FATAL] SYSTEM CRASH DETECTED (Exit Code: %errorlevel%)
    echo ==================================================
    
    if exist "logs\crash_tape.err" (
        echo [CRASH TAPE PLAYBACK]:
        echo --------------------------------------------------
        type "logs\crash_tape.err"
        echo --------------------------------------------------
    ) else (
        echo [ERROR] No Crash Tape found. Check /logs/debug_*.log manually.
    )
    
    pause
) else (
    echo [INFO] Session terminated normally.
    timeout /t 3 >nul
)