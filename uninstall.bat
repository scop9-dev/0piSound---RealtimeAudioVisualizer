@echo off
echo ================================
echo 0piSound - Uninstall Script
echo ================================
echo.

REM Remove Auto Start registry key (if it exists)
echo Removing Auto Start registry entry...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "0piSound" /f >nul 2>&1

REM Remove AppData settings folder
echo Removing configuration files...
rmdir /s /q "%AppData%\0piSound" >nul 2>&1

echo.
echo Uninstallation completed.
echo You can now safely delete the 0piSound executable.
echo.
pause
