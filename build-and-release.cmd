@echo off
setlocal
set "GAME_DIR=%~1"
if not defined GAME_DIR set "GAME_DIR=D:\Hry\Steam\steamapps\common\Vivat Slovakia"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish-local-releases.ps1" -GameDir "%GAME_DIR%"
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" echo Release nebyl dokoncen. Zkontroluj chybu vyse.
exit /b %RESULT%
