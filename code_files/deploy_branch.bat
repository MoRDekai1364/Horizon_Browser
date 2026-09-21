@echo off
setlocal


set /p msg="Enter commit message: "


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