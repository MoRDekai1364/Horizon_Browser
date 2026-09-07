@echo off
TITLE Horizon Stealth
CLS

echo [Horizon] Bootstrapping Environment...
python bootstrapper.py

IF %ERRORLEVEL% EQU 0 (
    echo [Horizon] Environment Secure. Launching Shell...
    python horizon_shell.py
) ELSE (
    echo.
    echo [Horizon] Initialization Failed. Check logs/system.log.
    PAUSE
)