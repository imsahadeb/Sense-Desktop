#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_FILE="$ROOT_DIR/WindowsClient/EnfyLiveScreenClient.csproj"
INSTALLER_SCRIPT="$ROOT_DIR/WindowsClient/installer/EnfyLiveScreenClient.iss"

RUNTIME="win-x64"
VERSION="1.0.0"
CONFIGURATION="Release"
SKIP_INSTALLER="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime)
      RUNTIME="$2"
      shift 2
      ;;
    --version)
      VERSION="$2"
      shift 2
      ;;
    --configuration)
      CONFIGURATION="$2"
      shift 2
      ;;
    --skip-installer)
      SKIP_INSTALLER="true"
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Usage: $0 [--version 1.0.0] [--runtime win-x64] [--configuration Release] [--skip-installer]" >&2
      exit 1
      ;;
  esac
done

PUBLISH_DIR="$ROOT_DIR/artifacts/windows/publish/$RUNTIME"
PORTABLE_DIR="$ROOT_DIR/artifacts/windows/portable"
INSTALLER_DIR="$ROOT_DIR/artifacts/windows/installer"
PORTABLE_ZIP="$PORTABLE_DIR/EnfyLiveScreenClient-$VERSION-$RUNTIME-portable.zip"

mkdir -p "$PUBLISH_DIR" "$PORTABLE_DIR" "$INSTALLER_DIR"

echo "Publishing Windows app to $PUBLISH_DIR"
dotnet publish "$PROJECT_FILE" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:Version="$VERSION" \
  -o "$PUBLISH_DIR"

echo "Creating portable zip at $PORTABLE_ZIP"
(
  cd "$PUBLISH_DIR"
  zip -qr "$PORTABLE_ZIP" .
)

if [[ "$SKIP_INSTALLER" == "true" ]]; then
  echo "Installer generation skipped."
  exit 0
fi

if command -v iscc >/dev/null 2>&1; then
  echo "Building installer exe with Inno Setup"
  iscc \
    "/DMyAppVersion=$VERSION" \
    "/DSourceDir=$PUBLISH_DIR" \
    "/DOutputDir=$INSTALLER_DIR" \
    "$INSTALLER_SCRIPT"
  echo "Installer created in $INSTALLER_DIR"
else
  echo "Inno Setup (iscc) not found on PATH."
  echo "Portable package is ready: $PORTABLE_ZIP"
  echo "To build an installable .exe, run scripts/build-windows-installer.cmd on a Windows machine with Inno Setup 6 installed."
fi
