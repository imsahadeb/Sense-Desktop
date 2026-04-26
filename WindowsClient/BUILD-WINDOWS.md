# Windows build and installer

Use the provided scripts to create Windows output for this app.

## Double-click build on Windows

If you want a simple Windows file you can double-click, use:

```bat
build-exe.cmd
```

No arguments are needed. It always builds:

- Version: `1.0.0`
- Runtime: `win-x64`
- Configuration: `Release`

This creates:

- `artifacts\windows\publish\win-x64\EnfyLiveScreenClient.exe`

## Double-click installer build on Windows

If you want a one-click installer build, use:

```bat
build-installer.cmd
```

No arguments are needed. It always builds:

- Version: `1.0.0`
- Runtime: `win-x64`
- Configuration: `Release`

This creates:

- `artifacts\windows\installer\EnfyLiveScreenClient-Setup-1.0.0.exe`

Requirements:

- `.NET 8 SDK`
- `Inno Setup 6`

## Portable package

From the project root:

```bash
./scripts/build-windows-installer.sh --version 1.0.0 --skip-installer
```

This produces:

- `artifacts/windows/publish/win-x64/EnfyLiveScreenClient.exe`
- `artifacts/windows/portable/EnfyLiveScreenClient-1.0.0-win-x64-portable.zip`

## Installable `.exe`

Run this on a Windows machine with:

- .NET 8 SDK
- Inno Setup 6

Command:

```bat
scripts\build-windows-installer.cmd 1.0.0 win-x64 Release
```

This produces an installer in:

- `artifacts\windows\installer\EnfyLiveScreenClient-Setup-1.0.0.exe`
