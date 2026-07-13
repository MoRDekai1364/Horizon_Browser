@echo off
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "LOG_DIR=%ROOT%logs"
set "LOG_FILE=%LOG_DIR%\Setup_Bootstrapper.log"
set "REDIST_DIR=%ROOT%redist"
set "TARGET_EXE=%ROOT%bin\Release\net8.0-windows10.0.19041.0\win-x64\Horizon.Stealth.exe"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"
echo [%DATE% %TIME%] [INFO] Bootstrapper initialized. > "%LOG_FILE%"
echo [%DATE% %TIME%] [INFO] Root: %ROOT% >> "%LOG_FILE%"

dotnet --list-runtimes | findstr "Microsoft.WindowsDesktop.App 8." >nul 2>&1
if %errorlevel% neq 0 (
    echo [%DATE% %TIME%] [WARN] .NET 8 Desktop Runtime missing. Initiating Phase 1 - Online Fetch... >> "%LOG_FILE%"
    call :INSTALL_DOTNET
    if !errorlevel! neq 0 (
        call :SHOW_ERROR "Critical Dependency Failure" "Failed to install .NET 8 Runtime. Please check internet connection or redist folder."
        exit /b 1
    )
) else (
    echo [%DATE% %TIME%] [INFO] .NET 8 Desktop Runtime found. >> "%LOG_FILE%"
)

reg query "HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" /v pv >nul 2>&1
if %errorlevel% neq 0 (
    echo [%DATE% %TIME%] [WARN] WebView2 Runtime missing. Initiating Phase 1 - Online Fetch... >> "%LOG_FILE%"
    call :INSTALL_WEBVIEW
    if !errorlevel! neq 0 (
        call :SHOW_ERROR "Critical Dependency Failure" "Failed to install WebView2 Runtime. Please check internet connection or redist folder."
        exit /b 1
    )
) else (
    echo [%DATE% %TIME%] [INFO] WebView2 Runtime found. >> "%LOG_FILE%"
)

if not exist "%TARGET_EXE%" (
    echo [%DATE% %TIME%] [FATAL] Target binary not found at: %TARGET_EXE% >> "%LOG_FILE%"
    call :SHOW_ERROR "Corrupt Installation" "Horizon.Stealth.exe is missing. Please reinstall."
    exit /b 1
)

echo [%DATE% %TIME%] [INFO] Dependencies met. Launching Core... >> "%LOG_FILE%"
echo [%DATE% %TIME%] [INFO] Arguments: %* >> "%LOG_FILE%"

:: CRITICAL FIX: The first set of quotes is the Window Title. 
:: We need to pass %* (all args) effectively.
start "Horizon" "%TARGET_EXE%" %*

exit /b 0

:INSTALL_DOTNET
echo [%DATE% %TIME%] [Attempting Online Install: .NET 8] >> "%LOG_FILE%"
powershell -Command "Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe' -OutFile '%TEMP%\dotnet_installer.exe'" >nul 2>&1
if %errorlevel% equ 0 (
    "%TEMP%\dotnet_installer.exe" /install /quiet /norestart
    if !errorlevel! equ 0 (
        echo [%DATE% %TIME%] [SUCCESS] .NET 8 Installed Online. >> "%LOG_FILE%"
        exit /b 0
    )
)

echo [%DATE% %TIME%] [Online Failed. Attempting Offline: .NET 8] >> "%LOG_FILE%"
if exist "%REDIST_DIR%\windowsdesktop-runtime-8.0.*-win-x64.exe" (
    for %%f in ("%REDIST_DIR%\windowsdesktop-runtime-8.0.*-win-x64.exe") do (
        "%%f" /install /quiet /norestart
        if !errorlevel! equ 0 (
            echo [%DATE% %TIME%] [SUCCESS] .NET 8 Installed Offline. >> "%LOG_FILE%"
            exit /b 0
        )
    )
)
echo [%DATE% %TIME%] [FAIL] .NET 8 Installation Failed. >> "%LOG_FILE%"
exit /b 1

:INSTALL_WEBVIEW
echo [%DATE% %TIME%] [Attempting Online Install: WebView2] >> "%LOG_FILE%"
powershell -Command "Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile '%TEMP%\webview2_installer.exe'" >nul 2>&1
if %errorlevel% equ 0 (
    "%TEMP%\webview2_installer.exe" /silent /install
    if !errorlevel! equ 0 (
        echo [%DATE% %TIME%] [SUCCESS] WebView2 Installed Online. >> "%LOG_FILE%"
        exit /b 0
    )
)

echo [%DATE% %TIME%] [Online Failed. Attempting Offline: WebView2] >> "%LOG_FILE%"
if exist "%REDIST_DIR%\MicrosoftEdgeWebView2RuntimeInstallerX64.exe" (
    "%REDIST_DIR%\MicrosoftEdgeWebView2RuntimeInstallerX64.exe" /silent /install
    if !errorlevel! equ 0 (
        echo [%DATE% %TIME%] [SUCCESS] WebView2 Installed Offline. >> "%LOG_FILE%"
        exit /b 0
    )
)
echo [%DATE% %TIME%] [FAIL] WebView2 Installation Failed. >> "%LOG_FILE%"
exit /b 1

:SHOW_ERROR
set "TITLE=%~1"
set "MSG=%~2"
echo MsgBox "%MSG%", 16, "%TITLE%" > "%TEMP%\error_popup.vbs"
cscript //nologo "%TEMP%\error_popup.vbs"
del "%TEMP%\error_popup.vbs"
exit /b