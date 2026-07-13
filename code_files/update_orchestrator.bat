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

> "%STATUS_FILE%" (
    echo STEP:Starting...
    echo PERCENT:0
)
start "" mshta.exe "%PROJECT_ROOT%\overlay.hta"

call "%PROJECT_ROOT%\backup.bat"

set "TARGET_DIR="
if exist "%PROJECT_ROOT%\path.txt" (
    set /p TARGET_DIR=<"%PROJECT_ROOT%\path.txt"
)
if not defined TARGET_DIR set "TARGET_DIR=%PROJECT_ROOT%"

> "%STATUS_FILE%" (
    echo STEP:Launching installer...
    echo PERCENT:30
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%\install_auto.ps1" -InstallerPath "%INSTALLER_PATH%" -TargetDir "%TARGET_DIR%" -StatusFile "%STATUS_FILE%"

call "%PROJECT_ROOT%\restore.bat"

exit /b 0
