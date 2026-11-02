@echo off
color 0A
title Horizon Browser // Deploy to Daily Driver
echo =====================================================================
echo   HORIZON BROWSER // DAILY DRIVER DEPLOYER
echo =====================================================================
echo.

set "SOURCE_DIR=%~dp0bin\Release"
set "TARGET_DIR=%LOCALAPPDATA%\Programs\Horizon_Browser"

if not exist "%SOURCE_DIR%\Horizon.Browser.exe" (
    color 0C
    echo [ERROR] Release build not found in:
    echo         %SOURCE_DIR%
    echo.
    echo Please run Build_Release.bat first to compile a release binary!
    echo.
    pause
    exit /b 1
)

echo [1/3] Preparing Daily Driver folder:
echo       %TARGET_DIR%
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

echo [2/3] Deploying release files...
robocopy "%SOURCE_DIR%" "%TARGET_DIR%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS >nul
if %errorlevel% geq 8 (
    color 0E
    echo [WARN] Some files could not be overwritten (your daily browser might be currently running).
    echo        Tip: Close your daily browser momentarily if you are updating it.
) else (
    echo [INFO] All binaries and assets deployed successfully.
)

echo [3/3] Creating Daily Driver Desktop and Start Menu shortcuts...
set "DESKTOP_LNK=%USERPROFILE%\Desktop\Horizon Browser.lnk"
set "START_LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Horizon Browser.lnk"
set "EXE_PATH=%TARGET_DIR%\Horizon.Browser.exe"

powershell -NoProfile -Command "& { $ws = New-Object -ComObject WScript.Shell; $sc = $ws.CreateShortcut('%DESKTOP_LNK%'); $sc.TargetPath = '%EXE_PATH%'; $sc.WorkingDirectory = '%TARGET_DIR%'; $sc.Description = 'Horizon Browser (Daily Driver)'; $sc.Save() }"
powershell -NoProfile -Command "& { $ws = New-Object -ComObject WScript.Shell; $sc = $ws.CreateShortcut('%START_LNK%'); $sc.TargetPath = '%EXE_PATH%'; $sc.WorkingDirectory = '%TARGET_DIR%'; $sc.Description = 'Horizon Browser (Daily Driver)'; $sc.Save() }"

echo [INFO] Desktop Shortcut    : %DESKTOP_LNK%
echo [INFO] Start Menu Shortcut : %START_LNK%
echo.
echo =====================================================================
echo   DEPLOYMENT COMPLETE!
echo =====================================================================
echo   Your Daily Driver is now installed and isolated in:
echo   %TARGET_DIR%\Horizon.Browser.exe
echo.
echo   - Run your Daily Driver from Desktop or Start Menu.
echo   - It uses your stable profile and stays open 24/7.
echo   - You can compile, test, and debug in your repo anytime
echo     WITHOUT ever closing your Daily Driver!
echo =====================================================================
echo.
pause
