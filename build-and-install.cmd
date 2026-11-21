@echo off
setlocal

set "GAME_DIR=%~1"
if not defined GAME_DIR set "GAME_DIR=D:\Hry\Steam\steamapps\common\Vivat Slovakia"
set "BEPINEX_DIR=%GAME_DIR%\BepInEx"
set "PLUGIN_DIR=%BEPINEX_DIR%\plugins"

if not exist "%BEPINEX_DIR%\core\BepInEx.Core.dll" (
    echo BepInEx nenalezen v "%BEPINEX_DIR%".
    exit /b 1
)
if not exist "%BEPINEX_DIR%\interop\Assembly-CSharp.dll" (
    echo Chybi herni interop DLL. Spust hru s BepInEx aspon jednou.
    exit /b 1
)

tasklist /fi "IMAGENAME eq Vivat Slovakia.exe" /nh | find /i "Vivat Slovakia.exe" >nul
if not errorlevel 1 (
    echo Pred instalaci zavri Vivat Slovakia.
    exit /b 1
)

set "FOUND="
for /d %%D in ("%~dp0*") do (
    if exist "%%~fD\%%~nxD.csproj" (
        set "FOUND=1"
        dotnet build "%%~fD\%%~nxD.csproj" -c Release "-p:BepInExDir=%BEPINEX_DIR%"
        if errorlevel 1 exit /b 1
        call :plugin "%%~fD\%%~nxD.csproj" check
        if errorlevel 1 exit /b 1
    )
)
if not defined FOUND (
    echo V repozitari neni zadny projekt modu.
    exit /b 1
)

if not exist "%PLUGIN_DIR%\" mkdir "%PLUGIN_DIR%"
if not exist "%PLUGIN_DIR%\" exit /b 1
for /d %%D in ("%~dp0*") do (
    if exist "%%~fD\%%~nxD.csproj" (
        call :plugin "%%~fD\%%~nxD.csproj" install
        if errorlevel 1 exit /b 1
    )
)

if exist "%~dp0VivatCombat\VivatCombat.csproj" (
    for %%V in (charon fenrir) do (
        call :voices %%V
        if errorlevel 1 exit /b 1
    )
)
echo Hotovo.
exit /b 0

:plugin
setlocal
set "PROJECT_FILE=%~1"
set "PROJECT_DIR=%~dp1"
set "ASSEMBLY_NAME=%~n1"
set "PLUGIN_VERSION="
for /f "delims=" %%A in ('powershell -NoProfile -Command "([xml](Get-Content -Raw -LiteralPath $env:PROJECT_FILE)).Project.PropertyGroup.AssemblyName"') do set "ASSEMBLY_NAME=%%A"
for /f "delims=" %%V in ('powershell -NoProfile -Command "Get-ChildItem -LiteralPath $env:PROJECT_DIR -Filter '*.cs' -File | Select-String -Pattern 'BepInPlugin.*?([0-9]+\.[0-9]+\.[0-9]+)' | ForEach-Object { $_.Matches[0].Groups[1].Value }"') do set "PLUGIN_VERSION=%%V"
if not defined PLUGIN_VERSION (
    echo Verze modu %~n1 nenalezena.
    exit /b 1
)
set "BUILT_DLL=%PROJECT_DIR%bin\Release\net6.0\%ASSEMBLY_NAME%.dll"
if not exist "%BUILT_DLL%" (
    echo Chybi vystup "%BUILT_DLL%".
    exit /b 1
)
if "%~2"=="check" exit /b 0
set "DESTINATION=%PLUGIN_DIR%\%ASSEMBLY_NAME%.%PLUGIN_VERSION%.dll"
copy /y /b "%BUILT_DLL%" "%DESTINATION%" >nul
if errorlevel 1 exit /b 1
echo Nainstalovano: "%DESTINATION%"
exit /b 0

:voices
set "VOICE_SOURCE=%~dp0VivatCombat\PoliceVoiceSamples\Relay\%~1"
set "VOICE_DEST=%PLUGIN_DIR%\VivatPoliceVoices\%~1"
if not exist "%VOICE_DEST%\" mkdir "%VOICE_DEST%"
copy /y /b "%VOICE_SOURCE%\*.wav" "%VOICE_DEST%\" >nul
if errorlevel 1 exit /b 1
echo Nainstalovany policejni hlasy: %~1
exit /b 0
