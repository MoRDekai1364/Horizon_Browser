@echo off
setlocal EnableDelayedExpansion

cd /d "%~dp0"
set "PROJECT_ROOT=%CD%"
set "STATUS_FILE=%PROJECT_ROOT%\update_status.txt"

call :WRITE_STATUS "Restoring data..." 90

set "BACKUP_DIR="
if exist "%PROJECT_ROOT%\last_backup_path.txt" (
    set /p BACKUP_DIR=<"%PROJECT_ROOT%\last_backup_path.txt"
)
if not defined BACKUP_DIR set "BACKUP_DIR=C:\Program Files\Horizon_Browser\Backup"

if not exist "%BACKUP_DIR%" (
    call :WRITE_STATUS "ERROR: No backup found at %BACKUP_DIR%" 90
    exit /b 1
)

if not defined LOCALAPPDATA set "LOCALAPPDATA=%USERPROFILE%\AppData\Local"
set "HORIZON_DATA=%PROJECT_ROOT%\HorizonData"

taskkill /F /IM Horizon.Browser.exe 2>nul
taskkill /F /IM msedgewebview2.exe /T 2>nul

call :WRITE_STATUS "Restoring exports..." 93
if exist "%BACKUP_DIR%\vault.dat" copy /Y "%BACKUP_DIR%\vault.dat" . >nul
if exist "%BACKUP_DIR%\settings.json" copy /Y "%BACKUP_DIR%\settings.json" . >nul
if exist "%BACKUP_DIR%\cookies.json" copy /Y "%BACKUP_DIR%\cookies.json" . >nul

call :WRITE_STATUS "Restoring browser profile..." 95
if exist "%BACKUP_DIR%\HorizonData" (
    robocopy "%BACKUP_DIR%\HorizonData" "%HORIZON_DATA%" /E /NFL /NDL /NJH /NJS /R:0 /W:0
)

call :WRITE_STATUS "Relaunching Horizon..." 99
timeout /t 1 /nobreak >nul
start "" "Horizon.Browser.exe"

call :WRITE_STATUS "DONE" 100
exit /b 0

:WRITE_STATUS
> "%STATUS_FILE%" (
    echo STEP:%~1
    echo PERCENT:%~2
)
exit /b
