@echo off
setlocal enabledelayedexpansion

set "ROOT_DIR=%~dp0.."
for %%I in ("%ROOT_DIR%") do set "ROOT_DIR=%%~fI"

set "PROJECT_FILE=%ROOT_DIR%\WindowsClient\EnfyLiveScreenClient.csproj"
set "INSTALLER_SCRIPT=%ROOT_DIR%\WindowsClient\installer\EnfyLiveScreenClient.iss"
set "VERSION=%~1"
set "RUNTIME=%~2"
set "CONFIGURATION=%~3"

if "%VERSION%"=="" set "VERSION=1.0.0"
if "%RUNTIME%"=="" set "RUNTIME=win-x64"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

set "PUBLISH_DIR=%ROOT_DIR%\artifacts\windows\publish\%RUNTIME%"
set "INSTALLER_DIR=%ROOT_DIR%\artifacts\windows\installer"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo dotnet SDK was not found on PATH.
  exit /b 1
)

set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_PATH%" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%ISCC_PATH%" (
  echo Inno Setup 6 was not found.
  echo Install it from https://jrsoftware.org/isinfo.php and run this script again.
  exit /b 1
)

if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"
if not exist "%INSTALLER_DIR%" mkdir "%INSTALLER_DIR%"

echo Publishing Windows app to %PUBLISH_DIR%
dotnet publish "%PROJECT_FILE%" ^
  -c %CONFIGURATION% ^
  -r %RUNTIME% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -p:Version=%VERSION% ^
  -o "%PUBLISH_DIR%"

if errorlevel 1 exit /b 1

echo Building installer exe
"%ISCC_PATH%" ^
  "/DMyAppVersion=%VERSION%" ^
  "/DSourceDir=%PUBLISH_DIR%" ^
  "/DOutputDir=%INSTALLER_DIR%" ^
  "%INSTALLER_SCRIPT%"

if errorlevel 1 exit /b 1

echo Installer created in %INSTALLER_DIR%
exit /b 0
