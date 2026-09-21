@echo off
setlocal EnableDelayedExpansion

cd /d "%~dp0"
set "PROJECT_ROOT=%CD%"
set "STATUS_FILE=%PROJECT_ROOT%\update_status.txt"

call :WRITE_STATUS "Backing up data" 10

if not exist "Horizon.Browser.exe" (
    call :WRITE_STATUS "ERROR: Horizon.Browser.exe not found" 0
    exit /b 1
)

call :WRITE_STATUS "Closing Horizon..." 12
taskkill /F /IM Horizon.Browser.exe 2>nul
taskkill /F /IM msedgewebview2.exe /T 2>nul

if not defined LOCALAPPDATA set "LOCALAPPDATA=%USERPROFILE%\AppData\Local"
if not defined PROGRAMFILES set "PROGRAMFILES=C:\Program Files"

set "HORIZON_APPDATA=%LOCALAPPDATA%\Horizon_Browser"
set "HORIZON_DATA=%PROJECT_ROOT%\HorizonData"

call :RESOLVE_BACKUP_LOCATION

call :WRITE_STATUS "Backing up exports..." 15
call :CHECK_AND_COPY "vault.dat" "%BACKUP_DIR%\" "Vault Database"
call :CHECK_AND_COPY "settings.json" "%BACKUP_DIR%\" "User Settings"

if exist "cookies.json" (
    copy "cookies.json" "%BACKUP_DIR%\" >nul
) else if exist "%PROJECT_ROOT%\cookies.json" (
    copy "%PROJECT_ROOT%\cookies.json" "%BACKUP_DIR%\" >nul
)

call :WRITE_STATUS "Backing up browser profile..." 20

set "SRC="
if exist "%HORIZON_DATA%" (
    set "SRC=%HORIZON_DATA%"
) else if exist "%HORIZON_APPDATA%\HorizonData" (
    set "SRC=%HORIZON_APPDATA%\HorizonData"
)

if defined SRC (
    robocopy "%SRC%" "%BACKUP_DIR%\HorizonData" /E /XD "Cache" "Code Cache" "GPUCache" "ShaderCache" "Service Worker" "GrShaderCache" "Crashpad" /NFL /NDL /NJH /NJS /R:0 /W:0
)

echo %BACKUP_DIR% > "%PROJECT_ROOT%\last_backup_path.txt"
call :WRITE_STATUS "Backup complete" 25
exit /b 0

:RESOLVE_BACKUP_LOCATION
set "BACKUP_DIR=C:\Program Files\Horizon_Browser\Backup"
if not exist "%BACKUP_DIR%" mkdir "%BACKUP_DIR%"
exit /b

:CHECK_AND_COPY
set "FNAME=%~1"
set "TARGET=%~2"
if exist "%FNAME%" copy /Y "%FNAME%" "%TARGET%" >nul
exit /b

:WRITE_STATUS
> "%STATUS_FILE%" (
    echo STEP:%~1
    echo PERCENT:%~2
)
exit /b
