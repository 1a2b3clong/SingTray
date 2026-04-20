#ifndef MyAppName
#define MyAppName "SingTray"
#endif
#ifndef MyAppVersion
#define MyAppVersion "1.0"
#endif
#ifndef MyAppPublisher
#define MyAppPublisher "SingTray"
#endif
#ifndef MyAppExeName
#define MyAppExeName "SingTray.Client.exe"
#endif
#ifndef MyServiceExeName
#define MyServiceExeName "SingTray.Service.exe"
#endif
#ifndef MyServiceName
#define MyServiceName "SingTray.Service"
#endif
#ifndef MyAppIcon
#define MyAppIcon "..\SingTray.Client\Assets\SingTray.ico"
#endif
#ifndef MyWizardSmallImage
#define MyWizardSmallImage "..\SingTray.Client\Assets\SingTray.png"
#endif
#ifndef MyOutputDir
#define MyOutputDir "output"
#endif
#ifndef MyOutputBaseFilename
#define MyOutputBaseFilename "SingTray-Setup"
#endif
#ifndef MyStagingDir
#define MyStagingDir "staging\framework"
#endif
#ifndef MyClientStagingDir
#define MyClientStagingDir AddBackslash(MyStagingDir) + "client"
#endif
#ifndef MyServiceStagingDir
#define MyServiceStagingDir AddBackslash(MyStagingDir) + "service"
#endif

[Setup]
AppId={{E11D4A17-4B8A-4AF9-9708-8D750F3121EA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
UninstallDisplayName=SingTray
DefaultDirName={autopf}\SingTray
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
DisableProgramGroupPage=yes
UsePreviousAppDir=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#MyAppIcon}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
WizardSmallImageFile={#MyWizardSmallImage}

[Dirs]
Name: "C:\ProgramData\SingTray"

[Files]
Source: "{#MyClientStagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyServiceStagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SingTray GUI client"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SingTray"; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SingTray.Client"; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SingTray Client"; Flags: deletevalue

[Icons]
Name: "{autoprograms}\SingTray"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create ""{#MyServiceName}"" binPath= ""{app}\{#MyServiceExeName}"" start= auto DisplayName= ""SingTray Service"""; Flags: runhidden waituntilterminated; Check: not ServiceExists('{#MyServiceName}')
Filename: "{sys}\sc.exe"; Parameters: "description ""{#MyServiceName}"" ""SingTray background controller for sing-box."""; Flags: runhidden waituntilterminated; Check: ServiceExists('{#MyServiceName}')
Filename: "{sys}\sc.exe"; Parameters: "start ""{#MyServiceName}"""; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Launch SingTray"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#MyServiceName}"""; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "StopSingTrayService"
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#MyServiceName}"""; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "DeleteSingTrayService"

[Code]
var
  ShowOptionsPage: Boolean;

function ServiceExists(const ServiceName: string): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query "' + ServiceName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function QueryServiceState(const ServiceName: string): string;
var
  ResultCode: Integer;
  TempFile: string;
  Output: AnsiString;
begin
  Result := '';
  TempFile := ExpandConstant('{tmp}\service-query.txt');
  if Exec(
    ExpandConstant('{cmd}'),
    '/C sc query "' + ServiceName + '" > "' + TempFile + '"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and (ResultCode = 0) and LoadStringFromFile(TempFile, Output) then
  begin
    if Pos('RUNNING', Uppercase(Output)) > 0 then Result := 'RUNNING'
    else if Pos('STOPPED', Uppercase(Output)) > 0 then Result := 'STOPPED'
    else if Pos('STOP_PENDING', Uppercase(Output)) > 0 then Result := 'STOP_PENDING'
    else if Pos('START_PENDING', Uppercase(Output)) > 0 then Result := 'START_PENDING'
    else Result := 'UNKNOWN';
  end;
end;

procedure StopExistingService();
var
  ResultCode: Integer;
  Attempts: Integer;
  State: string;
begin
  if not ServiceExists('{#MyServiceName}') then
    exit;

  Exec(ExpandConstant('{sys}\sc.exe'), 'stop "{#MyServiceName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  for Attempts := 1 to 20 do
  begin
    State := QueryServiceState('{#MyServiceName}');
    if (State = 'STOPPED') or (State = '') then
      exit;
    Sleep(1000);
  end;
end;

procedure StopExistingClient();
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
    Exec(
      ExpandConstant('{cmd}'),
      '/C taskkill /IM "{#MyAppExeName}" /F >NUL 2>NUL',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    StopExistingClient();
    StopExistingService();
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if PageID = wpSelectDir then
    Result := not ShowOptionsPage
  else
    Result := False;
end;

function BackButtonClick(CurPageID: Integer): Boolean;
begin
  if CurPageID = wpReady then
    ShowOptionsPage := True;

  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  WizardForm.Caption := '{#MyAppName} Setup';
  WizardForm.WelcomeLabel1.Caption := '{#MyAppName}';
  WizardForm.WelcomeLabel2.Caption := 'Install SingTray for this device.';

  if CurPageID = wpReady then
  begin
    WizardForm.BackButton.Caption := '&Options';
    WizardForm.NextButton.Caption := '&Install';
  end
  else
  begin
    WizardForm.BackButton.Caption := SetupMessage(msgButtonBack);
    WizardForm.NextButton.Caption := SetupMessage(msgButtonNext);
  end;
end;
