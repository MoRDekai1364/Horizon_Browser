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
set "BIN_NAME=Horizon.Stealth.exe"

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
dotnet publish -c Release -r win-x64 --self-contained false -o "%OUT_DIR%" %ICON_ARG%

if %errorlevel% neq 0 (
    color 0C
    echo [FATAL] Build Failed. Check errors above.
    pause
    exit /b
)

echo [3/3] Packaging Update Manager...
if exist "%UPDATE_MGR%" (
    copy "%UPDATE_MGR%" "%OUT_DIR%\" >nul
    echo [INFO] Update Manager included.
) else (
    echo [WARN] %UPDATE_MGR% not found in source directory.
)

echo.
echo ==================================================
echo   BUILD SUCCESSFUL
echo ==================================================
echo   Your official binary is located at:
echo   %~dp0%OUT_DIR%\%BIN_NAME%
echo.
pause