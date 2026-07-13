@echo off
setlocal

set /p msg="Enter commit message: "

:: Handle submodule if it exists and has changes
if exist "code_files\.git" (
    echo [INFO] Detected submodule. Checking for changes...
    cd code_files
    git add .
    git commit -m "Auto-commit: %msg%"
    cd ..
    git add code_files
)

:: Stage and commit main repo
git add .
git commit -m "%msg%"

:: Push to the currently checked-out branch
git push

if %errorlevel% equ 0 (
    echo [#########] 100%% - Successfully pushed to remote.
) else (
    echo [ERROR] Push failed. Check your network or credentials.
    pause
)

if %errorlevel% neq 0 (
    echo Error occurred at %date% %time% >> "%TEMP%\git_deploy_error.log"
    echo Log saved to %TEMP%\git_deploy_error.log
)

pause
endlocal