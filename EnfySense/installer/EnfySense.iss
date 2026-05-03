#define MyAppId "{{64D4FA90-13D1-4EA5-901A-0F2CC3C44C80}}"
#define MyAppName "EnfySense"
#define MyAppExeName "EnfySense.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.15"
#endif

#ifndef MyAppPublisher
  #define MyAppPublisher "Enfy"
#endif

#ifndef SourceDir
  #define SourceDir "..\\..\\artifacts\\windows\\publish\\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\\..\\artifacts\\windows\\installer"
#endif

; The TOTP secret used to verify the admin identity for admin unlock and uninstall.
; This must match the secret configured in the EnfySense backend (sense.enfysense_admins).
; To update, replace the value below and rebuild the installer.
#ifndef AdminTotpSecret
  #define AdminTotpSecret "DMTSDL3Y7XT5M36HQ2ELBSTTQ65SLUTO"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=EnfySense-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=none
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}

[Registry]
Root: HKA; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EnfySense"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Write AdminTotpSecrets to appsettings.json during installation.
// This ensures admin unlock and uninstall verification works immediately,
// without requiring the user to log in or have internet access.
procedure WriteAdminConfig();
var
  ConfigDir: string;
  ConfigPath: string;
  ConfigJson: AnsiString;
begin
  ConfigDir := ExpandConstant('{commonappdata}\EnfySense\Config');
  ConfigPath := ConfigDir + '\appsettings.json';

  // Ensure the directory exists
  if not DirExists(ConfigDir) then
    ForceDirectories(ConfigDir);

  // Always write a fresh config with the pre-seeded TOTP secret.
  // The app will preserve any user-specific settings (DeviceNameOverride, etc.)
  // on first launch by merging/saving from AppConfig.Load().
  // We do NOT attempt to merge the existing file to avoid AnsiString complexity.
  ConfigJson :=
    '{' + #13#10 +
    '  "BackendUrl": "http://192.168.56.1:3000",' + #13#10 +
    '  "AutoConnect": true,' + #13#10 +
    '  "DeviceNameOverride": "",' + #13#10 +
    '  "KeycloakIssuer": "https://auth.enfycon.com/realms/submission_tracker",' + #13#10 +
    '  "KeycloakClientId": "submission_tracker_app",' + #13#10 +
    '  "SsoRedirectUri": "http://localhost:3001/callback",' + #13#10 +
    '  "AdminTotpSecrets": ["{#AdminTotpSecret}"]' + #13#10 +
    '}';

  SaveStringToFile(ConfigPath, ConfigJson, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteAdminConfig();
end;
