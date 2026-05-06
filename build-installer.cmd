@echo off
setlocal

set "ROOT_DIR=%~dp0"
for %%I in ("%ROOT_DIR%") do set "ROOT_DIR=%%~fI"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"

set "PROJECT_FILE=%ROOT_DIR%\EnfySense\EnfySense.csproj"
set "INSTALLER_SCRIPT=%ROOT_DIR%\EnfySense\installer\EnfySense.iss"
set "VERSION=1.0.15"
set "RUNTIME=win-x64"
set "CONFIGURATION=Release"

set "PUBLISH_DIR=%ROOT_DIR%\artifacts\windows\publish\%RUNTIME%"
set "INSTALLER_DIR=%ROOT_DIR%\artifacts\windows\installer"

echo.
echo ==========================================
echo Building EnfySense Setup EXE
echo ==========================================
echo Using fixed settings:
echo Version: %VERSION%
echo Runtime: %RUNTIME%
echo Config : %CONFIGURATION%
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found on PATH.
  echo Install .NET 8 SDK, then run this file again.
  echo.
  pause
  exit /b 1
)

set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_PATH%" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%ISCC_PATH%" (
  echo Inno Setup 6 was not found.
  echo Install Inno Setup 6, then run this file again.
  echo https://jrsoftware.org/isinfo.php
  echo.
  pause
  exit /b 1
)

if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"
if not exist "%INSTALLER_DIR%" mkdir "%INSTALLER_DIR%"

echo Publishing app files...
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

if errorlevel 1 goto :failed

echo Publishing watchdog files...
dotnet publish "%ROOT_DIR%\EnfySense.Watchdog\EnfySense.Watchdog.csproj" ^
  -c %CONFIGURATION% ^
  -r %RUNTIME% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -p:Version=%VERSION% ^
  -o "%PUBLISH_DIR%"

if errorlevel 1 goto :failed
goto :build_installer

:failed
echo.
echo Build failed.
echo.
pause
exit /b 1

:build_installer

echo Building installer...
"%ISCC_PATH%" ^
  "/DMyAppVersion=%VERSION%" ^
  "/DSourceDir=%PUBLISH_DIR%" ^
  "/DOutputDir=%INSTALLER_DIR%" ^
  "%INSTALLER_SCRIPT%"

if errorlevel 1 (
  echo.
  echo Installer build failed.
  echo.
  pause
  exit /b 1
)

echo.
echo Installer created successfully:
echo %INSTALLER_DIR%\EnfySense-Setup-%VERSION%.exe
echo.
echo Just double-click this file on Windows whenever you need a fresh installer.
echo.
pause
exit /b 0
