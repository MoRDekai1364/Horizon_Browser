@echo off
setlocal

echo [System] Checking for Python environment...
where python >nul 2>&1
if %errorlevel% NEQ 0 (
    goto :INSTALL_PYTHON
) else (
    goto :RUN_SCRIPT
)

:INSTALL_PYTHON
echo [System] Python not found.
echo [System] Checking Administrative Privileges...

net session >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [Action] Requesting Admin rights to install Python...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo [System] Admin rights confirmed.
echo [System] Downloading Python 3.12...
curl -L -o python_installer.exe https://www.python.org/ftp/python/3.12.1/python-3.12.1-amd64.exe

if not exist python_installer.exe (
    echo [Error] Download failed. Check your internet connection.
    pause
    exit /b
)

echo [System] Installing Python...
python_installer.exe /quiet InstallAllUsers=1 PrependPath=1 Include_test=0 Include_pip=1

echo [System] Installation Complete.
echo [Action] You may need to restart this script (or your PC) for changes to take effect.
del python_installer.exe
pause
exit /b

:RUN_SCRIPT
echo [System] Python detected. Launching Mapper...
echo ---------------------------------------------------
python "project_mapper.py" %*

if %errorlevel% NEQ 0 (
    echo.
    echo [Error] The Python script crashed or was interrupted.
    echo [Tip] Check if 'project_mapper.py' is in the same folder.
) else (
    echo.
    echo [Success] Script finished successfully.
)

echo.
pause