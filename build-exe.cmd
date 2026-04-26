@echo off
setlocal

set "ROOT_DIR=%~dp0"
for %%I in ("%ROOT_DIR%") do set "ROOT_DIR=%%~fI"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"

set "PROJECT_FILE=%ROOT_DIR%\WindowsClient\EnfyLiveScreenClient.csproj"
set "VERSION=1.0.0"
set "RUNTIME=win-x64"
set "CONFIGURATION=Release"

set "PUBLISH_DIR=%ROOT_DIR%\artifacts\windows\publish\%RUNTIME%"
set "OUTPUT_EXE=%PUBLISH_DIR%\EnfyLiveScreenClient.exe"

echo.
echo ==========================================
echo Building EnfyLiveScreenClient.exe
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

if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"

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

if errorlevel 1 (
  echo.
  echo Build failed.
  echo.
  pause
  exit /b 1
)

echo.
echo Build completed successfully.
echo EXE file:
echo %OUTPUT_EXE%
echo.
echo No arguments are needed for this file. Just double-click it on Windows.
echo You can now copy or run that .exe file.
echo.
pause
exit /b 0
