#define MyAppId "{64D4FA90-13D1-4EA5-901A-0F2CC3C44C80}"
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
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Allow the user to choose between "Install for all users" and "Install for me only"
PrivilegesRequiredOverridesAllowed=dialog
; Default to user-level but allow the dialog to elevate to admin
PrivilegesRequired=none
; Ensure the directory page is shown
DisableDirPage=no
; Do not skip pages based on previous installations (good for testing)
UsePreviousAppDir=no
UsePreviousPrivileges=no
OutputDir={#OutputDir}
OutputBaseFilename=EnfySense-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; SetupIconFile=logo.ico
; WizardSmallImageFile=logo_small.bmp
UninstallDisplayIcon={app}\{#MyAppExeName}
; Used to detect if the app is running
AppMutex=Global\EnfySense-SingleInstance-73b9e40f-7b7e-4d8e-90f7-920f0f7f3f3f
CloseApplications=yes

[Registry]
Root: HKA; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EnfySense"; ValueData: """{app}\{#MyAppExeName}"" --startup"; Flags: uninsdeletevalue
Root: HKA; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EnfySenseWatchdog"; ValueData: """{app}\EnfySenseWatchdog.exe"""; Flags: uninsdeletevalue

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
Filename: "{app}\EnfySenseWatchdog.exe"; Description: "Launch Watchdog"; Flags: nowait postinstall skipifsilent

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
  if IsAdminInstallMode then
    ConfigDir := ExpandConstant('{commonappdata}\EnfySense\Config')
  else
    ConfigDir := ExpandConstant('{localappdata}\EnfySense\Config');

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
    '  "BackendUrl": "https://backend.enfycon.com",' + #13#10 +
    '  "AutoConnect": true,' + #13#10 +
    '  "DeviceNameOverride": "",' + #13#10 +
    '  "KeycloakIssuer": "https://auth.enfycon.com/realms/submission_tracker",' + #13#10 +
    '  "KeycloakClientId": "submission_tracker_app",' + #13#10 +
    '  "SsoRedirectUri": "http://localhost:3001/callback",' + #13#10 +
    '  "AdminTotpSecrets": ["{#AdminTotpSecret}"]' + #13#10 +
    '}';

  SaveStringToFile(ConfigPath, ConfigJson, False);
end;

function InitializeUninstall(): Boolean;
var
  Form: TSetupForm;
  OKButton, CancelButton: TNewButton;
  Edit: TNewEdit;
  PromptLabel: TLabel;
  Code: String;
  ResultCode: Integer;
begin
  Result := False;
  
  Form := TSetupForm.CreateNew(nil, 0);
  Form.ClientWidth := 380;
  Form.ClientHeight := 160;
  Form.Caption := 'Admin Verification Required';
  Form.Position := poScreenCenter;

  PromptLabel := TLabel.Create(Form);
  PromptLabel.Parent := Form;
  PromptLabel.AutoSize := True;
  PromptLabel.Left := 20;
  PromptLabel.Top := 20;
  PromptLabel.Caption := 'This application is protected. Enter the 6-digit Admin TOTP code' + #13#10 + 'to proceed with uninstallation:';

  Edit := TNewEdit.Create(Form);
  Edit.Parent := Form;
  Edit.Left := 20;
  Edit.Top := PromptLabel.Top + PromptLabel.Height + 15;
  Edit.Width := Form.ClientWidth - 40;
  Edit.PasswordChar := '*';

  OKButton := TNewButton.Create(Form);
  OKButton.Parent := Form;
  OKButton.Caption := 'OK';
  OKButton.Default := True;
  OKButton.ModalResult := mrOk;
  OKButton.Left := Form.ClientWidth - 2 * (80 + 10);
  OKButton.Top := Form.ClientHeight - 35 - 10;
  OKButton.Width := 80;

  CancelButton := TNewButton.Create(Form);
  CancelButton.Parent := Form;
  CancelButton.Caption := 'Cancel';
  CancelButton.ModalResult := mrCancel;
  CancelButton.Left := Form.ClientWidth - (80 + 10);
  CancelButton.Top := OKButton.Top;
  CancelButton.Width := 80;

  if Form.ShowModal() = mrOk then
  begin
    Code := Edit.Text;
    if Code = '' then
    begin
      MsgBox('Verification code cannot be empty.', mbError, MB_OK);
      exit;
    end;

    // Call the app's EXE with the verify-totp flag. 
    // The EXE is still present in {app} during InitializeUninstall.
    if Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--verify-totp ' + Code, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode = 0 then
      begin
        // Kill watchdog and app processes
        Exec(ExpandConstant('{app}\EnfySenseWatchdog.exe'), '--stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Exec('taskkill.exe', '/f /im EnfySenseWatchdog.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Exec('taskkill.exe', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        Result := True;
      end
      else
        MsgBox('Invalid verification code. Uninstallation aborted.', mbError, MB_OK);
    end
    else
      MsgBox('Failed to verify code (could not launch verification utility).', mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteAdminConfig();
end;
