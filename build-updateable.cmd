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
set "RELEASES_DIR=%ROOT_DIR%\releases"

echo.
echo ==========================================
echo Building EnfyLiveScreenClient with Velopack
echo ==========================================
echo Version: %VERSION%
echo Runtime: %RUNTIME%
echo.

if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"
if not exist "%RELEASES_DIR%" mkdir "%RELEASES_DIR%"

echo Publishing app files...
dotnet publish "%PROJECT_FILE%" ^
  -c %CONFIGURATION% ^
  -r %RUNTIME% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:Version=%VERSION% ^
  -o "%PUBLISH_DIR%"

if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

echo Packaging with Velopack...
vpk pack ^
  -u EnfyLiveScreenClient ^
  -v %VERSION% ^
  -p "%PUBLISH_DIR%" ^
  -o "%RELEASES_DIR%" ^
  -e EnfyLiveScreenClient.exe

if errorlevel 1 (
  echo Velopack packaging failed.
  pause
  exit /b 1
)

echo.
echo Success! Velopack files created in: %RELEASES_DIR%
echo Upload the contents of this folder to your GitHub Releases.
echo.
pause
exit /b 0
