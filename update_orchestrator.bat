@echo off
setlocal EnableDelayedExpansion

cd /d "%~dp0"
set "PROJECT_ROOT=%CD%"
set "STATUS_FILE=%PROJECT_ROOT%\update_status.txt"

set "INSTALLER_PATH=%~1"
if "%INSTALLER_PATH%"=="" (
    > "%STATUS_FILE%" (
        echo STEP:ERROR: No installer path provided
        echo PERCENT:0
    )
    exit /b 1
)

set "TARGET_DIR="
if exist "%PROJECT_ROOT%\path.txt" (
    set /p TARGET_DIR=<"%PROJECT_ROOT%\path.txt"
)
if not defined TARGET_DIR set "TARGET_DIR=%PROJECT_ROOT%"

rem 1. Check for professional native Horizon.Updater.exe
if exist "%PROJECT_ROOT%\Tools\Horizon.Updater.exe" (
    start "" "%PROJECT_ROOT%\Tools\Horizon.Updater.exe" --installer "%INSTALLER_PATH%" --target "%TARGET_DIR%"
    exit /b 0
)
if exist "%PROJECT_ROOT%\Horizon.Updater.exe" (
    start "" "%PROJECT_ROOT%\Horizon.Updater.exe" --installer "%INSTALLER_PATH%" --target "%TARGET_DIR%"
    exit /b 0
)

rem 2. Fallback execution without MSHTA
taskkill /f /im Horizon.Browser.exe >nul 2>&1
timeout /t 1 /nobreak >nul

call "%PROJECT_ROOT%\backup.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%\install_auto.ps1" -InstallerPath "%INSTALLER_PATH%" -TargetDir "%TARGET_DIR%" -StatusFile "%PROJECT_ROOT%\update_status.txt"
call "%PROJECT_ROOT%\restore.bat"

exit /b 0
