@echo off
setlocal EnableDelayedExpansion

cd /d "%~dp0"
color 0B
title Horizon // Build File Manager
set "MANIFEST=extra_files.txt"

if not exist "%MANIFEST%" type nul > "%MANIFEST%"

:MENU
cls
echo =====================================================================
echo    HORIZON BUILD FILE MANAGER
echo =====================================================================
echo    These files are copied into bin\Release on every Build_Release.bat run.
echo =====================================================================
echo.
echo    CURRENT FILES:
echo    ---------------------------------------------------------------
set "idx=0"
for /f "usebackq delims=" %%F in ("%MANIFEST%") do (
    if not "%%F"=="" (
        set /a idx+=1
        echo    [!idx!] %%F
    )
)
if "!idx!"=="0" echo    (none yet)
echo.
echo =====================================================================
echo    [1] ADD FILE  (opens file picker)
echo    [2] ADD FILE  (type relative path manually)
echo    [3] REMOVE FILE
echo    [4] EXIT
echo =====================================================================
set /p "choice=Select Option: "

if "%choice%"=="1" goto ADD_PICKER
if "%choice%"=="2" goto ADD_MANUAL
if "%choice%"=="3" goto REMOVE
if "%choice%"=="4" exit /b 0
goto MENU

:ADD_PICKER
echo.
echo [INFO] Opening file picker (select one or more files in this project)...
set "PICKED="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.InitialDirectory = (Get-Location).Path; $f.Multiselect = $true; $f.Title = 'Select files to bundle into the build'; if ($f.ShowDialog() -eq 'OK') { $f.FileNames -join '|' } else { '' }"`) do set "PICKED=%%P"

if "!PICKED!"=="" (
    echo [INFO] No files selected.
    pause
    goto MENU
)

for %%P in ("!PICKED:|=" "!") do call :ADD_ENTRY "%%~P"
pause
goto MENU

:ADD_MANUAL
echo.
set /p "manual=Enter relative path from project root (e.g. backup.bat): "
if "!manual!"=="" goto MENU
call :ADD_ENTRY "!manual!"
pause
goto MENU

:ADD_ENTRY
set "RAW=%~1"
set "PROJECT_ROOT=%CD%\"
set "REL=%RAW%"
if "!RAW:~0,2!"=="!PROJECT_ROOT:~0,2!" (
    call set "REL=%%RAW:!PROJECT_ROOT!=%%"
)

if not exist "!REL!" (
    echo [WARN] File not found relative to project root, skipping: !REL!
    exit /b
)

findstr /I /X /C:"!REL!" "%MANIFEST%" >nul 2>&1
if !errorlevel! equ 0 (
    echo [INFO] Already in list: !REL!
    exit /b
)

echo !REL!>> "%MANIFEST%"
echo [OK] Added: !REL!
exit /b

:REMOVE
echo.
set /p "rmidx=Enter number to remove (or 0 to cancel): "
if "!rmidx!"=="0" goto MENU

set "tmpfile=%MANIFEST%.tmp"
if exist "%tmpfile%" del "%tmpfile%"
set "i=0"
for /f "usebackq delims=" %%F in ("%MANIFEST%") do (
    if not "%%F"=="" (
        set /a i+=1
        if not "!i!"=="!rmidx!" echo %%F>> "%tmpfile%"
    )
)
if exist "%tmpfile%" (
    move /Y "%tmpfile%" "%MANIFEST%" >nul
) else (
    type nul > "%MANIFEST%"
)
echo [OK] Removed entry #!rmidx!
pause
goto MENU