@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -DesktopShortcut
if errorlevel 1 echo Installation failed. See the message above.
pause
