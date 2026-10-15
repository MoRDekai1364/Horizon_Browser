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
if not defined LOCALAPPDATA set "LOCALAPPDATA=%USERPROFILE%\AppData\Local"
if not defined BACKUP_DIR set "BACKUP_DIR=%LOCALAPPDATA%\Horizon_Browser\Backups\pre_update_backup"

if not exist "%BACKUP_DIR%" (
    call :WRITE_STATUS "ERROR: No backup found at %BACKUP_DIR%" 90
    exit /b 1
)

rem 1. Prefer professional native Horizon.Updater.exe with --restore
if exist "%PROJECT_ROOT%\Tools\Horizon.Updater.exe" (
    start "" "%PROJECT_ROOT%\Tools\Horizon.Updater.exe" --restore "%BACKUP_DIR%" --target "%PROJECT_ROOT%"
    exit /b 0
)
if exist "%PROJECT_ROOT%\Horizon.Updater.exe" (
    start "" "%PROJECT_ROOT%\Horizon.Updater.exe" --restore "%BACKUP_DIR%" --target "%PROJECT_ROOT%"
    exit /b 0
)

rem 2. Fallback restoration
set "HORIZON_DATA=%PROJECT_ROOT%\HorizonData"
set "APPDATA_HORIZON_DATA=%LOCALAPPDATA%\Horizon_Browser\HorizonData"

taskkill /F /IM Horizon.Browser.exe 2>nul
taskkill /F /IM msedgewebview2.exe /T 2>nul

if not exist "%HORIZON_DATA%" mkdir "%HORIZON_DATA%"
if not exist "%APPDATA_HORIZON_DATA%" mkdir "%APPDATA_HORIZON_DATA%"

call :WRITE_STATUS "Restoring exports..." 93
for %%F in (vault.dat settings.json config.json cookies.json bookmarks.json history.json) do (
    if exist "%BACKUP_DIR%\%%F" (
        copy /Y "%BACKUP_DIR%\%%F" . >nul
        copy /Y "%BACKUP_DIR%\%%F" "%HORIZON_DATA%\" >nul
        copy /Y "%BACKUP_DIR%\%%F" "%APPDATA_HORIZON_DATA%\" >nul
    )
    if exist "%BACKUP_DIR%\HorizonData\%%F" (
        copy /Y "%BACKUP_DIR%\HorizonData\%%F" . >nul
        copy /Y "%BACKUP_DIR%\HorizonData\%%F" "%HORIZON_DATA%\" >nul
        copy /Y "%BACKUP_DIR%\HorizonData\%%F" "%APPDATA_HORIZON_DATA%\" >nul
    )
)

call :WRITE_STATUS "Restoring browser profile..." 95
if exist "%BACKUP_DIR%\HorizonData" (
    robocopy "%BACKUP_DIR%\HorizonData" "%HORIZON_DATA%" /E /XD "Cache" "Code Cache" "GPUCache" "ShaderCache" /NFL /NDL /NJH /NJS /R:0 /W:0 >nul 2>&1
    robocopy "%BACKUP_DIR%\HorizonData" "%APPDATA_HORIZON_DATA%" /E /XD "Cache" "Code Cache" "GPUCache" "ShaderCache" /NFL /NDL /NJH /NJS /R:0 /W:0 >nul 2>&1
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
