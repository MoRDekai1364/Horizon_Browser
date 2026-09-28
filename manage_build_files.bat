@echo off
setlocal EnableDelayedExpansion

cd /d "%~dp0"
color 0B
title Horizon // Build File Manager
set "FILE_MANIFEST=extra_files.txt"
set "FOLDER_MANIFEST=extra_folders.txt"

if not exist "%FILE_MANIFEST%" type nul > "%FILE_MANIFEST%"
if not exist "%FOLDER_MANIFEST%" type nul > "%FOLDER_MANIFEST%"

set "CSPROJ="
for %%C in (*.csproj) do set "CSPROJ=%%C"
if not defined CSPROJ (
    echo [FATAL] No .csproj found in project root.
    pause
    exit /b 1
)

:MENU
cls
echo =====================================================================
echo    HORIZON BUILD FILE MANAGER
echo =====================================================================
echo    Files below are copied into bin\Release on every Build_Release.bat run.
echo    Folders below are copied automatically by dotnet publish via %CSPROJ%.
echo =====================================================================
echo.
echo    CURRENT FILES:
echo    ---------------------------------------------------------------
set "idx=0"
for /f "usebackq delims=" %%F in ("%FILE_MANIFEST%") do (
    if not "%%F"=="" (
        set /a idx+=1
        echo    [!idx!] %%F
    )
)
if "!idx!"=="0" echo    (none yet)
echo.
echo    CURRENT FOLDERS:
echo    ---------------------------------------------------------------
set "fidx=0"
for /f "usebackq delims=" %%D in ("%FOLDER_MANIFEST%") do (
    if not "%%D"=="" (
        set /a fidx+=1
        echo    [!fidx!] %%D
    )
)
if "!fidx!"=="0" echo    (none yet)
echo.
echo =====================================================================
echo    [1] ADD FILE    (opens file picker)
echo    [2] ADD FILE    (type relative path manually)
echo    [3] ADD FOLDER  (opens folder picker)
echo    [4] ADD FOLDER  (type relative path manually)
echo    [5] REMOVE FILE
echo    [6] REMOVE FOLDER
echo    [7] EXIT
echo =====================================================================
set /p "choice=Select Option: "

if "%choice%"=="1" goto ADD_FILE_PICKER
if "%choice%"=="2" goto ADD_FILE_MANUAL
if "%choice%"=="3" goto ADD_FOLDER_PICKER
if "%choice%"=="4" goto ADD_FOLDER_MANUAL
if "%choice%"=="5" goto REMOVE_FILE
if "%choice%"=="6" goto REMOVE_FOLDER
if "%choice%"=="7" exit /b 0
goto MENU

:ADD_FILE_PICKER
echo.
echo [INFO] Opening file picker (select one or more files in this project)...
set "PICKED="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.InitialDirectory = (Get-Location).Path; $f.Multiselect = $true; $f.Title = 'Select files to bundle into the build'; if ($f.ShowDialog() -eq 'OK') { $f.FileNames -join '|' } else { '' }"`) do set "PICKED=%%P"

if "!PICKED!"=="" (
    echo [INFO] No files selected.
    pause
    goto MENU
)

for %%P in ("!PICKED:|=" "!") do call :ADD_FILE_ENTRY "%%~P"
pause
goto MENU

:ADD_FILE_MANUAL
echo.
set /p "manual=Enter relative path from project root (e.g. backup.bat): "
if "!manual!"=="" goto MENU
call :ADD_FILE_ENTRY "!manual!"
pause
goto MENU

:ADD_FILE_ENTRY
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

findstr /I /X /C:"!REL!" "%FILE_MANIFEST%" >nul 2>&1
if !errorlevel! equ 0 (
    echo [INFO] Already in list: !REL!
    exit /b
)

echo !REL!>> "%FILE_MANIFEST%"
call :CSPROJ_ADD "!REL!" "!REL!"
echo [OK] Added file: !REL!
exit /b

:ADD_FOLDER_PICKER
echo.
echo [INFO] Opening folder picker (select a folder in this project)...
set "PICKEDDIR="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.SelectedPath = (Get-Location).Path; if ($f.ShowDialog() -eq 'OK') { $f.SelectedPath } else { '' }"`) do set "PICKEDDIR=%%P"

if "!PICKEDDIR!"=="" (
    echo [INFO] No folder selected.
    pause
    goto MENU
)

call :ADD_FOLDER_ENTRY "!PICKEDDIR!"
pause
goto MENU

:ADD_FOLDER_MANUAL
echo.
set /p "manualdir=Enter relative folder path from project root (e.g. wallpapers): "
if "!manualdir!"=="" goto MENU
call :ADD_FOLDER_ENTRY "!manualdir!"
pause
goto MENU

:ADD_FOLDER_ENTRY
set "RAWDIR=%~1"
set "PROJECT_ROOT=%CD%\"
set "RELDIR=%RAWDIR%"
if "!RAWDIR:~0,2!"=="!PROJECT_ROOT:~0,2!" (
    call set "RELDIR=%%RAWDIR:!PROJECT_ROOT!=%%"
)
if "!RELDIR:~-1!"=="\" set "RELDIR=!RELDIR:~0,-1!"

if not exist "!RELDIR!\" (
    echo [WARN] Folder not found relative to project root, skipping: !RELDIR!
    exit /b
)

findstr /I /X /C:"!RELDIR!" "%FOLDER_MANIFEST%" >nul 2>&1
if !errorlevel! equ 0 (
    echo [INFO] Already in list: !RELDIR!
    exit /b
)

echo !RELDIR!>> "%FOLDER_MANIFEST%"
call :CSPROJ_ADD "!RELDIR!\**" "!RELDIR!"
echo [OK] Added folder: !RELDIR!
exit /b

:CSPROJ_ADD
set "INCLUDEVAL=%~1"
set "LABEL=%~2"
findstr /I /C:"Include=\"!INCLUDEVAL!\"" "%CSPROJ%" >nul 2>&1
if !errorlevel! equ 0 (
    echo [INFO] csproj already references: !LABEL!
    exit /b
)

powershell -NoProfile -Command ^
    "$path = '%CSPROJ%';" ^
    "$content = Get-Content -Raw -LiteralPath $path;" ^
    "$insert = '  <ItemGroup>' + [Environment]::NewLine + '    <None Include=\"!INCLUDEVAL!\" CopyToOutputDirectory=\"PreserveNewest\" />' + [Environment]::NewLine + '  </ItemGroup>' + [Environment]::NewLine + '</Project>';" ^
    "$content = $content.Replace('</Project>', $insert);" ^
    "Set-Content -LiteralPath $path -Value $content -NoNewline"

if !errorlevel! neq 0 (
    echo [WARN] Failed to update csproj for: !LABEL!
) else (
    echo [OK] csproj updated with: !INCLUDEVAL!
)
exit /b

:CSPROJ_REMOVE
set "INCLUDEVAL=%~1"

powershell -NoProfile -Command ^
    "$path = '%CSPROJ%';" ^
    "$lines = Get-Content -LiteralPath $path;" ^
    "$filtered = $lines | Where-Object { $_ -notmatch [regex]::Escape('Include=\"!INCLUDEVAL!\"') };" ^
    "Set-Content -LiteralPath $path -Value $filtered"

if !errorlevel! neq 0 (
    echo [WARN] Failed to remove csproj entry for: !INCLUDEVAL!
) else (
    echo [OK] csproj entry removed: !INCLUDEVAL!
)
exit /b

:REMOVE_FILE
echo.
set /p "rmidx=Enter file number to remove (or 0 to cancel): "
if "!rmidx!"=="0" goto MENU

set "TARGETREL="
set "i=0"
for /f "usebackq delims=" %%F in ("%FILE_MANIFEST%") do (
    if not "%%F"=="" (
        set /a i+=1
        if "!i!"=="!rmidx!" set "TARGETREL=%%F"
    )
)

if not defined TARGETREL (
    echo [WARN] No such entry.
    pause
    goto MENU
)

set "tmpfile=%FILE_MANIFEST%.tmp"
if exist "%tmpfile%" del "%tmpfile%"
set "i=0"
for /f "usebackq delims=" %%F in ("%FILE_MANIFEST%") do (
    if not "%%F"=="" (
        set /a i+=1
        if not "!i!"=="!rmidx!" echo %%F>> "%tmpfile%"
    )
)
if exist "%tmpfile%" (
    move /Y "%tmpfile%" "%FILE_MANIFEST%" >nul
) else (
    type nul > "%FILE_MANIFEST%"
)

call :CSPROJ_REMOVE "!TARGETREL!"
echo [OK] Removed file entry #!rmidx!: !TARGETREL!
pause
goto MENU

:REMOVE_FOLDER
echo.
set /p "rmfidx=Enter folder number to remove (or 0 to cancel): "
if "!rmfidx!"=="0" goto MENU

set "TARGETDIR="
set "i=0"
for /f "usebackq delims=" %%D in ("%FOLDER_MANIFEST%") do (
    if not "%%D"=="" (
        set /a i+=1
        if "!i!"=="!rmfidx!" set "TARGETDIR=%%D"
    )
)

if not defined TARGETDIR (
    echo [WARN] No such entry.
    pause
    goto MENU
)

set "tmpfile=%FOLDER_MANIFEST%.tmp"
if exist "%tmpfile%" del "%tmpfile%"
set "i=0"
for /f "usebackq delims=" %%D in ("%FOLDER_MANIFEST%") do (
    if not "%%D"=="" (
        set /a i+=1
        if not "!i!"=="!rmfidx!" echo %%D>> "%tmpfile%"
    )
)
if exist "%tmpfile%" (
    move /Y "%tmpfile%" "%FOLDER_MANIFEST%" >nul
) else (
    type nul > "%FOLDER_MANIFEST%"
)

call :CSPROJ_REMOVE "!TARGETDIR!\**"
echo [OK] Removed folder entry #!rmfidx!: !TARGETDIR!
pause
goto MENU