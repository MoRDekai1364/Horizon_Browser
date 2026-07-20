@echo off
setlocal enabledelayedexpansion

set SCRIPT_DIR=%~dp0
set PS_SCRIPT=%SCRIPT_DIR%deploy_backend.ps1

where powershell >nul 2>nul
if errorlevel 1 (
    echo [ERROR] PowerShell not found on this system.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -Phase 4

if %errorlevel% neq 0 (
    echo [ERROR] Backend script exited with error code %errorlevel%.
    pause
    exit /b %errorlevel%
)

pause
endlocal