@echo off
timeout /t 2 /nobreak > NUL
rmdir /s /q "E:\Programming\Personal_projects\Horizon_Browser"
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Horizon_Browser" /f
del "%~f0" & exit