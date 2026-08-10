#ifndef AppVersion
  #error AppVersion must be provided by package.ps1
#endif
#ifndef BinaryVersion
  #error BinaryVersion must be provided by package.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be provided by package.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by package.ps1
#endif

[Setup]
AppId={{C80A3088-A351-4D76-B9E1-FD9C123386A7}
AppName=MacBook Eco
AppVersion={#AppVersion}
AppPublisher=MacBook Eco contributors
AppCopyright=Copyright (c) 2026 MacBook Eco contributors
AppPublisherURL=https://github.com/stlk0/MacBookEco
AppSupportURL=https://github.com/stlk0/MacBookEco/issues
AppUpdatesURL=https://github.com/stlk0/MacBookEco/releases
DefaultDirName={localappdata}\Programs\MacBookEco
DefaultGroupName=MacBook Eco
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=MacBookEco-{#AppVersion}-win-x64-setup
VersionInfoCompany=MacBook Eco contributors
VersionInfoCopyright=Copyright (c) 2026 MacBook Eco contributors
VersionInfoDescription=MacBook Eco installer
VersionInfoOriginalFileName=MacBookEco-{#AppVersion}-win-x64-setup.exe
VersionInfoProductName=MacBook Eco
VersionInfoProductTextVersion={#AppVersion}
VersionInfoProductVersion={#BinaryVersion}
VersionInfoVersion={#BinaryVersion}
SetupIconFile=..\src\App\MacBookEco.ico
UninstallDisplayIcon={app}\MacBookEco.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
AppMutex=Local\MacBookEco.Tray.2E6FB97C-78E6-4DFB-AB6E-A8BE8E5B4DBA

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion notimestamp recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MacBook Eco"; Filename: "{app}\MacBookEco.exe"; WorkingDir: "{app}"

[Registry]
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "MacBookEco"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MacBookEco.exe"; Description: "Launch MacBook Eco"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
var
  RecoveryRestartRequired: Boolean;

function HasCommandLineParameter(const Value: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do begin
    if CompareText(ParamStr(Index), Value) = 0 then begin
      Result := True;
      exit;
    end;
  end;
end;

function IsSilentUninstall: Boolean;
begin
  Result := HasCommandLineParameter('/SILENT') or
    HasCommandLineParameter('/VERYSILENT');
end;

function ForceUninstallRequested: Boolean;
begin
  Result := HasCommandLineParameter('/FORCEUNINSTALL');
end;

function ConfirmForcedUninstall(const Reason: String): Boolean;
begin
  if not ForceUninstallRequested then begin
    Result := False;
    exit;
  end;

  if IsSilentUninstall then begin
    Result := True;
    exit;
  end;

  Result := MsgBox(
    Reason + #13#10 + #13#10 +
    'Force removal deletes only MacBook Eco and its startup entry. It does not confirm that EDID or the app-owned power plan was restored.' + #13#10 + #13#10 +
    'Remove anyway?',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
  RecoveryExitCode: Integer;
  Started: Boolean;
  RecoveryStarted: Boolean;
  CheckPath: String;
begin
  CheckPath := ExpandConstant('{app}\MacBookEco.exe');
  Started := Exec(
    CheckPath,
    '--check-uninstall-safe',
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode);

  if Started and (ExitCode = 0) then begin
    Result := True;
    exit;
  end;

  if ForceUninstallRequested then begin
    Result := ConfirmForcedUninstall(
      'MacBook Eco recovery is required or could not be verified.');
    exit;
  end;

  if IsSilentUninstall then begin
    Result := False;
    exit;
  end;

  if MsgBox(
      'MacBook Eco must restore its display and power changes before removal.' + #13#10 + #13#10 +
      'Continue with automatic recovery now? Windows may show administrator and display-mode confirmation prompts.',
      mbConfirmation,
      MB_YESNO or MB_DEFBUTTON1) <> IDYES then begin
    Result := False;
    exit;
  end;

  RecoveryStarted := Exec(
    CheckPath,
    '--recover-for-uninstall',
    ExpandConstant('{app}'),
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    RecoveryExitCode);
  if RecoveryStarted and ((RecoveryExitCode = 0) or
      (RecoveryExitCode = 3)) then begin
    RecoveryRestartRequired := RecoveryExitCode = 3;
    Result := True;
    exit;
  end;

  MsgBox(
    'MacBook Eco was not removed because automatic recovery did not complete. No unverified state is treated as restored.',
    mbInformation,
    MB_OK);
  Result := False;
end;

function UninstallNeedRestart(): Boolean;
begin
  Result := RecoveryRestartRequired;
end;
