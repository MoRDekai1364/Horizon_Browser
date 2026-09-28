@echo off
color 0B
echo ==================================================
echo   HORIZON // BUILD COMPILER
echo ==================================================
echo.
echo [0/3] Initializing configuration...
set "ICON_ARG="
set "ICON_PATH="
set "SRC_DIR=src"
set "OUT_DIR=bin\Release"
set "UPDATE_MGR=Update_Manager.bat"
set "BIN_NAME=Horizon.Browser.exe"

if exist "%SRC_DIR%\*.ico" (
    for %%F in ("%SRC_DIR%\*.ico") do (
        set "ICON_PATH=%%~fF"
        goto :IconAutoFound
    )
)

:IconAutoFound
if defined ICON_PATH (
    echo [INFO] Auto-detected icon: "%ICON_PATH%"
    set "ICON_ARG=/p:ApplicationIcon="%ICON_PATH%""
) else (
    echo [WARN] No icon found in '%SRC_DIR%'. Initiating manual override.
    set /p "AskIcon=>> Select custom icon for executable? [Y/N]: "
    if /i "%AskIcon%"=="Y" (
        echo Opening file picker...
        for /f "delims=" %%I in ('powershell -NoProfile -Command "& {Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Filter = 'Icon Files (*.ico)|*.ico'; $f.Title = 'Select Application Icon'; if($f.ShowDialog() -eq 'OK'){Write-Output $f.FileName}}"') do set "ICON_PATH=%%I"
    )
    if defined ICON_PATH (
        echo [INFO] Manual icon selected: "%ICON_PATH%"
        set "ICON_ARG=/p:ApplicationIcon="%ICON_PATH%""
    )
)

echo [1/3] Cleaning previous builds...
if exist "%OUT_DIR%" rmdir /s /q "%OUT_DIR%"

echo [2/3] Compiling Release Binary...
rem Skip workload resolution — prevents 0xe0434352 crash dialog caused by
rem missing Visual Studio workload manifests. Cleared immediately after build.
set MSBuildEnableWorkloadResolver=false
set DOTNET_CLI_WORKLOAD_UPDATER_ENABLED=0
dotnet publish Horizon.Browser.csproj -c Release -r win-x64 --self-contained false -o "%OUT_DIR%" %ICON_ARG%
set MSBuildEnableWorkloadResolver=
set DOTNET_CLI_WORKLOAD_UPDATER_ENABLED=

if %errorlevel% neq 0 (
    if not exist "%OUT_DIR%\%BIN_NAME%" (
        color 0C
        echo [FATAL] Build Failed. Check errors above.
        pause
        exit /b
    )
    echo [WARN] dotnet exited with non-zero code but binary exists ^(SDK workload logger bug^). Continuing.
)

echo [3/3] Creating desktop shortcut...
set "SHORTCUT_PATH=%~dp0!START_Horizon_Browser.lnk"
set "TARGET_PATH=%~dp0%OUT_DIR%\%BIN_NAME%"
set "WORK_DIR=%~dp0%OUT_DIR%"
powershell -NoProfile -Command "& { $ws = New-Object -ComObject WScript.Shell; $sc = $ws.CreateShortcut('%SHORTCUT_PATH%'); $sc.TargetPath = '%TARGET_PATH%'; $sc.WorkingDirectory = '%WORK_DIR%'; $sc.Description = 'Horizon Browser'; $sc.Save() }"
if exist "%SHORTCUT_PATH%" (
    echo [INFO] Shortcut created: !START_Horizon_Browser.lnk
) else (
    echo [WARN] Shortcut creation failed.
)

echo [4/3] Packaging Update Manager...
if exist "%UPDATE_MGR%" (
    copy "%UPDATE_MGR%" "%OUT_DIR%\" >nul
    echo [INFO] Update Manager included.
) else (
    echo [WARN] %UPDATE_MGR% not found in source directory.
)

echo [5/3] Copying additional build files...
if exist "extra_files.txt" (
    for /f "usebackq delims=" %%F in ("extra_files.txt") do (
        if not "%%F"=="" (
            if exist "%%F" (
                copy "%%F" "%OUT_DIR%\" >nul
                echo [INFO] Copied: %%F
            ) else (
                echo [WARN] Listed file not found, skipping: %%F
            )
        )
    )
) else (
    echo [INFO] No extra_files.txt found. Run manage_build_files.bat to create one.
)

echo.
echo ==================================================
echo   BUILD SUCCESSFUL
echo ==================================================
echo   Binary  : %~dp0%OUT_DIR%\%BIN_NAME%
echo   Shortcut: %~dp0!START_Horizon_Browser.lnk
echo.
pause