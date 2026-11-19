@echo off
setlocal DisableDelayedExpansion
if not exist "%~dp0RayXDesktop.exe" (
    echo Placez ce fichier BAT a cote de RayXDesktop.exe.
    pause
    exit /b 1
)
start "" /d "%~dp0." "%~dp0RayXDesktop.exe" "--account=.\Admin" "--password:PASSW_WORD!"
