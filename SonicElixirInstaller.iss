; Sonics Elixir Installer Script (Optimized)

[Setup]
AppName=Sonics Elixir
AppVersion=0.1.0
AppPublisher=Blast752
DefaultDirName={commonpf}\Sonics Elixir
DefaultGroupName=Sonics Elixir
OutputDir=InstallerOutput
OutputBaseFilename=SonicsElixir_x64_v0.1.0 
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Uncomment and set the following if you have custom wizard images:
;WizardImageFile=Wizardslogo.bmp
SetupIconFile=Images\icon.ico
WizardSmallImageFile=WizardSmallslogo.bmp
UninstallDisplayIcon=Images\icon.ico
VersionInfoCompany=Blast752
VersionInfoDescription=Android optimizer for all
VersionInfoCopyright=Copyright © 2025 Blast752
VersionInfoProductName=Sonics Elixir Installer
VersionInfoProductVersion=0.1.0.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Copies all published application files
Source: bin\Release\net6.0-windows\*; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; --- ADB Files ---
Source: "adb\adb.exe"; DestDir: "{app}\adb"; Flags: ignoreversion
Source: "adb\AdbWinApi.dll"; DestDir: "{app}\adb"; Flags: ignoreversion
Source: "adb\AdbWinUsbApi.dll"; DestDir: "{app}\adb"; Flags: ignoreversion
Source: "adb\libwinpthread-1.dll"; DestDir: "{app}\adb"; Flags: ignoreversion

[Icons]
; Create Start Menu shortcut only if the user selects the corresponding task
Name: "{group}\Sonics Elixir"; Filename: "{app}\SonicsElixir.exe"; Tasks: startmenuicon
; Create Desktop shortcut only if the user selects the corresponding task
Name: "{commondesktop}\Sonics Elixir"; Filename: "{app}\SonicsElixir.exe"; Tasks: desktopicon

[Tasks]
; These tasks allow the user to decide whether to create shortcuts
Name: "desktopicon"; Description: "Create a Desktop Shortcut"; GroupDescription: "Additional Tasks:"; Flags: unchecked
Name: "startmenuicon"; Description: "Add a Start Menu Shortcut"; GroupDescription: "Additional Tasks:"; Flags: unchecked

[Run]
; Optionally launch the application after installation
Filename: "{app}\SonicsElixir.exe"; Description: "Launch Sonics Elixir"; Flags: nowait postinstall skipifsilent

[Code]
const
  WM_SETTINGCHANGE = $001A;

var
  ADBInfoPage: TWizardPage;
  InfoLabel: TNewStaticText;
  RadioAccept: TRadioButton;
  RadioDecline: TRadioButton;

{ Sends a message to all top-level windows about an environment change }
function SendMessage(hWnd: LongWord; Msg: LongWord; wParam: LongWord; lParam: string): LongWord;
  external 'SendMessageW@user32.dll stdcall';

{ Notifies the system that the environment has changed }
procedure NotifyEnvironmentChange;
begin
  SendMessage(HWND_BROADCAST, WM_SETTINGCHANGE, 0, 'Environment');
end;

{ Checks if adb.exe is already available in the system PATH by executing "adb.exe version" }
function IsAdbAvailable: Boolean;
var
  ResultCode: Integer;
begin
  if Exec('adb.exe', 'version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0)
  else
    Result := False;
end;

{ Adds the bundled ADB folder to the system PATH if adb is not already available }
procedure AddAdbToSystemPath;
var
  CurrentPath, NewPath, AppAdbDir: string;
begin
  AppAdbDir := ExpandConstant('{app}\adb');
  if not IsAdbAvailable then
  begin
    if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', CurrentPath) then
      CurrentPath := '';
    if Pos(AppAdbDir, CurrentPath) = 0 then
    begin
      NewPath := CurrentPath + ';' + AppAdbDir;
      if RegWriteStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', 'Path', NewPath) then
        NotifyEnvironmentChange;
    end;
  end;
end;

{ Adds firewall rules to allow adb.exe inbound and outbound connections }
procedure AddFirewallRules;
var
  ResultCode: Integer;
  ADBPath: string;
begin
  ADBPath := '"' + ExpandConstant('{app}\adb\adb.exe') + '"';
  Exec('netsh', 'advfirewall firewall add rule name="SonicElixir ADB Inbound" dir=in action=allow program=' + ADBPath + ' enable=yes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('netsh', 'advfirewall firewall add rule name="SonicElixir ADB Outbound" dir=out action=allow program=' + ADBPath + ' enable=yes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Creates a custom page to inform the user about ADB installation and firewall configuration }
procedure InitializeADBInfoPage;
begin
  ADBInfoPage := CreateCustomPage(wpWelcome, 
    'ADB Installation Notice',   // Page Title
    '');                         // Leave description blank to avoid text truncation

  { Create a static text label to display the detailed information }
  InfoLabel := TNewStaticText.Create(WizardForm);
  InfoLabel.Parent := ADBInfoPage.Surface;
  InfoLabel.Left := ScaleX(8);
  InfoLabel.Top := ScaleY(8);
  InfoLabel.Width := ADBInfoPage.SurfaceWidth - ScaleX(16);
  InfoLabel.Height := ScaleY(50);  
  InfoLabel.AutoSize := True;
  InfoLabel.WordWrap := True;
  InfoLabel.Caption := 
    'Sonic''s Elixir requires the installation of ADB along with necessary firewall ' +
    'configurations for optimal performance.' + #13#10#13#10 +
    'The following actions will be performed:' + #13#10 +
    ' - Install ADB (Android Debug Bridge)' + #13#10 +
    ' - Configure firewall rules to allow proper ADB functionality' + #13#10#13#10 +
    'Do you accept these changes?';

  { Create the "Accept" radio button below the label }
  RadioAccept := TNewRadioButton.Create(WizardForm);
  RadioAccept.Parent := ADBInfoPage.Surface;
  RadioAccept.Left := InfoLabel.Left;
  RadioAccept.Top := InfoLabel.Top + InfoLabel.Height + ScaleY(12);
  RadioAccept.Width := InfoLabel.Width;
  RadioAccept.Caption := 'Accept and continue installation';
  RadioAccept.Checked := True;

  { Create the "Decline" radio button below the "Accept" button }
  RadioDecline := TNewRadioButton.Create(WizardForm);
  RadioDecline.Parent := ADBInfoPage.Surface;
  RadioDecline.Left := InfoLabel.Left;
  RadioDecline.Top := RadioAccept.Top + RadioAccept.Height + ScaleY(8);
  RadioDecline.Width := InfoLabel.Width;
  RadioDecline.Caption := 'Do not accept – cancel installation';
end;

{ Checks the user’s response on the custom ADB information page }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ADBInfoPage.ID then
  begin
    if not RadioAccept.Checked then
    begin
      MsgBox(
        'You must accept the installation of ADB and the firewall configurations ' +
        'to proceed with Sonic''s Elixir installation.',
        mbInformation, MB_OK
      );
      Result := False; // Cancels the installation if the user declines
    end;
  end;
end;

{ During post-installation, add ADB to PATH and create firewall rules }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AddAdbToSystemPath;
    AddFirewallRules;
  end;
end;

{ Optional preliminary checks before the installation begins }
function InitializeSetup(): Boolean;
begin
  Result := True;
  // Insert any preliminary checks here, if needed
end;

{ Initializes the custom wizard page }
procedure InitializeWizard();
begin
  InitializeADBInfoPage;
end;
