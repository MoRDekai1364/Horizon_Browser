@echo off
setlocal EnableDelayedExpansion

net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
color 0B
title Horizon Stealth // Update Manager // AUTO-RELEASE
mode con: cols=100 lines=45

set "PROJECT_ROOT=%CD%"
set "CURRENT_CTX=%CD%"

:DISCOVERY_PHASE
cls
echo =====================================================================
echo    HORIZON UPDATE MANAGER (AUTO-RELEASE)
echo =====================================================================
echo    [INFO] Scanning for Horizon.Browser.exe...
echo.
if exist "Horizon.Browser.exe" (
    echo    [INFO] Found in current directory.
    goto :MENU
)

setlocal DisableDelayedExpansion
set "cnt=0"
for /f "delims=" %%F in ('dir /s /b "Horizon.Browser.exe" 2^>nul') do (
    set /a cnt+=1
    setlocal EnableDelayedExpansion
    for %%C in (!cnt!) do (
        endlocal
        set "loc[%%C]=%%~dpF"
        set "file[%%C]=%%F"
    )
)
setlocal EnableDelayedExpansion

if "!cnt!"=="0" (
    color 0C
    echo    [CRITICAL] Horizon.Browser.exe not found anywhere.
    echo    Please place this script in the Project Root.
    pause
    exit
)

for /L %%i in (1,1,!cnt!) do (
    
echo "!loc[%%i]!" | findstr /I "Release" >nul
    if !errorlevel! equ 0 (
        echo    [PRIORITY] Release version detected. Auto-selecting...
        cd /d "!loc[%%i]!"
        set "CURRENT_CTX=!CD!"
        goto :MENU
    )
)

if "!cnt!"=="1" (
    echo    [AUTO-DETECT] Found 1 instance. Switching context...
    cd /d "!loc[1]!"
    set "CURRENT_CTX=!CD!"
    goto :MENU
)

echo    [MULTIPLE VERSIONS DETECTED]
echo 
   (No 'Release' build found, please select manually)
echo.
for /L %%i in (1,1,!cnt!) do (
    echo    [%%i] !loc[%%i]!
)
echo.
set /p sel="Select Environment (1-!cnt!): "

if defined loc[%sel%] (
    cd /d "!loc[%sel%]!"
    set "CURRENT_CTX=!CD!"
    goto :MENU
) else (
    echo    Invalid selection.
    pause
    goto :DISCOVERY_PHASE
)

:MENU
cls
echo =====================================================================
echo    HORIZON STEALTH UPDATE MANAGER
echo =====================================================================
echo    ACTIVE: %CURRENT_CTX%
echo =====================================================================
echo.
echo    [1] BACKUP DATA (Smart Scan)
echo    [2] RESTORE DATA
echo    [3] EXIT
echo.
set /p choice="Select Option: "

if "%choice%"=="1" goto BACKUP
if "%choice%"=="2" goto RESTORE
if "%choice%"=="3" goto EXIT_SEQUENCE
goto MENU

:RESOLVE_BACKUP_LOCATION
set "DEFAULT_BACKUP_DIR=C:\Program Files\Horizon_Browser\Backup"

echo.
echo =====================================================================
echo    BACKUP LOCATION
echo =====================================================================
echo    [1] Default  --  %DEFAULT_BACKUP_DIR%
echo    [2] Custom   --  Choose a folder...
echo.
set /p "loc_choice=Select location (1/2, default=1): "

if "!loc_choice!"=="2" goto :PICK_FOLDER

set "BACKUP_DIR=%DEFAULT_BACKUP_DIR%"
echo [INFO] Using default location: %BACKUP_DIR%
goto :RESOLVE_DONE

:PICK_FOLDER
echo [INFO] Opening folder picker...
set "PICKED="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = 'Select backup destination for Horizon'; $d.ShowNewFolderButton = $true; if ($d.ShowDialog() -eq 'OK') { $d.SelectedPath } else { '' }"`) do (
    set "PICKED=%%P"
)

if not defined PICKED goto :PICK_CANCELLED
if "!PICKED!"=="" goto :PICK_CANCELLED

set "BACKUP_DIR=!PICKED!\Horizon_Migration_Package"
echo [INFO] Custom location selected: !BACKUP_DIR!
goto :RESOLVE_DONE

:PICK_CANCELLED
echo [WARN] No folder selected. Falling back to default location.
set "BACKUP_DIR=%DEFAULT_BACKUP_DIR%"

:RESOLVE_DONE
if not exist "%BACKUP_DIR%" mkdir "%BACKUP_DIR%"
exit /b

:BACKUP
cls
echo [BACKUP MODE - SMART SCAN]
echo.
if not exist "Horizon.Browser.exe" (
    color 0C
    echo [CRITICAL] Horizon.Browser.exe NOT found in Active Directory.
    pause
    exit
)

echo [INFO] Closing Horizon processes...
taskkill /F /IM Horizon.Browser.exe 2>nul
taskkill /F /IM msedgewebview2.exe /T 2>nul
echo [INFO] Ready.

if not defined LOCALAPPDATA set "LOCALAPPDATA=%USERPROFILE%\AppData\Local"
if not defined PROGRAMFILES set "PROGRAMFILES=C:\Program Files"

set "HORIZON_APPDATA=%LOCALAPPDATA%\Horizon_Browser"
set "HORIZON_DATA=%CURRENT_CTX%\HorizonData"

call :RESOLVE_BACKUP_LOCATION

echo.
echo [INFO] Source data : %HORIZON_DATA%
echo [INFO] Backup dest : %BACKUP_DIR%
echo.
echo [STEP 1/3] Checking Custom Exports
echo ---------------------------------------------------
call :CHECK_AND_COPY "vault.dat" "%BACKUP_DIR%\" "Vault Database"
call :CHECK_AND_COPY "settings.json" "%BACKUP_DIR%\" "User Settings"

if exist "cookies.json" (
    echo [OK] Cookies Export found [Local]. Backing up...
    copy "cookies.json" "%BACKUP_DIR%\" >nul
) else (
    echo [INFO] Cookies not in build folder. Checking Root...
    if exist "%PROJECT_ROOT%\cookies.json" (
        echo [OK] Cookies found in Project Root. Backing up...
        copy "%PROJECT_ROOT%\cookies.json" "%BACKUP_DIR%\" >nul
    ) else (
        echo [INFO] Cookies Export not found. Skipping.
    )
)

echo.
echo [STEP 2/3] Verifying HorizonData
echo ---------------------------------------------------

set "SRC="
if exist "%HORIZON_DATA%" (
    set "SRC=%HORIZON_DATA%"
) else if exist "%HORIZON_APPDATA%\HorizonData" (
    set "SRC=%HORIZON_APPDATA%\HorizonData"
    echo [INFO] HorizonData found in AppData.
)

if defined SRC (
    echo [FOUND] HorizonData at: %SRC%

    if exist "%SRC%\history.json" (
        echo         + History Found
    ) else (
        echo         - History NOT detected
    )

    if exist "%SRC%\Default\History" (
        echo         + Raw History Found
    ) else (
        echo         - Raw History NOT detected
    )

    if exist "%SRC%\Default\Network\Cookies" (
        echo         + Raw Cookies Found
    ) else (
        echo         - Raw Cookies NOT detected
    )

    if exist "%SRC%\Default\Login Data" (
        echo         + Raw Logins Found
    ) else (
        echo         - Raw Logins NOT detected
    )

    echo.
    echo [ACTION] Backing up Browser Profile...
    robocopy "%SRC%" "%BACKUP_DIR%\HorizonData" /E /XD "Cache" "Code Cache" "GPUCache" "ShaderCache" "Service Worker" "GrShaderCache" "Crashpad" /NFL /NDL /NJH /NJS /R:0 /W:0
    echo [SUCCESS] Browser Profile Saved.
) else (
    color 0E
    echo [WARNING] HorizonData folder NOT found at expected paths.
    echo           Checked: %HORIZON_DATA%
    echo           Checked: %HORIZON_APPDATA%\HorizonData
)

echo.
echo =====================================================================
echo    BACKUP COMPLETE  --  %BACKUP_DIR%
echo =====================================================================
goto AUTO_RELAUNCH

:RESTORE
cls
echo [RESTORE MODE]
echo.

call :RESOLVE_BACKUP_LOCATION

if not exist "%BACKUP_DIR%" (
    color 0C
    echo [ERROR] No backup found at: %BACKUP_DIR%
    pause
    goto MENU
)

if not defined LOCALAPPDATA set "LOCALAPPDATA=%USERPROFILE%\AppData\Local"
set "HORIZON_DATA=%CURRENT_CTX%\HorizonData"

echo [INFO] Terminating processes (Double-Tap)...
taskkill /F /IM Horizon.Browser.exe 2>nul
taskkill /F /IM msedgewebview2.exe /T 2>nul

echo [1/2] Restoring Exports...
if exist "%BACKUP_DIR%\vault.dat" copy /Y "%BACKUP_DIR%\vault.dat" . >nul
if exist "%BACKUP_DIR%\settings.json" copy /Y "%BACKUP_DIR%\settings.json" . >nul
if exist "%BACKUP_DIR%\cookies.json" copy /Y "%BACKUP_DIR%\cookies.json" . >nul

echo [2/2] Restoring Browser Identity...
if exist "%BACKUP_DIR%\HorizonData" (
    robocopy "%BACKUP_DIR%\HorizonData" "%HORIZON_DATA%" /E /NFL /NDL /NJH /NJS /R:0 /W:0
    echo [SUCCESS] Profile Injected.
) else (
    echo [WARNING] No HorizonData in backup.
)

echo.
echo    RESTORE COMPLETE.
goto AUTO_RELAUNCH

:AUTO_RELAUNCH
echo.
echo [INFO] Sequence Complete.
echo Relaunching Horizon in 1 seconds...
timeout /t 1 /nobreak >nul
start "" "Horizon.Browser.exe"
exit

:EXIT_SEQUENCE
cls
echo.
echo =====================================================================
echo    EXIT
echo =====================================================================
echo.
set /p "launch=Launch Horizon Browser? (Y/N): "
if /i "!launch!"=="Y" (
    echo [INFO] Starting Horizon.Browser.exe...
    start "" "Horizon.Browser.exe"
)
exit /b 0

:CHECK_AND_COPY
set "FNAME=%~1"
set "TARGET=%~2"
set "DESC=%~3"
if exist "%FNAME%" (
    echo [OK] %DESC% found. Backing up...
    copy /Y "%FNAME%" "%TARGET%" >nul
) else (
    echo [INFO] %DESC% [%FNAME%] not created yet. Skipping.
)
exit /b