#define MyAppName "SingTray"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "SingTray"
#define MyAppExeName "SingTray.Client.exe"
#define MyServiceExeName "SingTray.Service.exe"
#define MyServiceName "SingTray.Service"

[Setup]
AppId={{E11D4A17-4B8A-4AF9-9708-8D750F3121EA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
UninstallDisplayName=SingTray
DefaultDirName={autopf}\SingTray
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
UsePreviousAppDir=no
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputDir=output
OutputBaseFilename=SingTray-Setup

[Dirs]
Name: "C:\ProgramData\SingTray"

[Files]
Source: "staging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
