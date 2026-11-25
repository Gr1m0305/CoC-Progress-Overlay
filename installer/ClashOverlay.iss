; Clash Tracker installer (Inno Setup 6.3 or newer).
;
; Packages the single-file, self-contained publish output (win-x64) for a per-user install with no admin prompt:
;   dotnet publish src/ClashOverlay.App/ClashOverlay.App.csproj -c Release -p:PublishProfile=win-x64 -p:ClashVersion=1.2.3
;   iscc /DAppVersion=1.2.3 installer\ClashOverlay.iss
; The result is installer\Output\ClashOverlay-Setup-1.2.3.exe.
;
; Optional code signing (the release workflow does this when the signing secrets exist):
;   iscc /DSign "/Sclashsign=$qC:\path\signtool.exe$q sign /sha1 <thumbprint> /fd sha256 /tr <timestamp url> /td sha256 $f" ...
;
; Silent use: ClashOverlay-Setup-X.Y.Z.exe /SILENT (or /VERYSILENT). The uninstaller keeps your data when silent
; unless you pass /DELETEDATA: "%LOCALAPPDATA%\Programs\ClashOverlay\unins000.exe" /VERYSILENT /DELETEDATA

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "Clash Tracker"
#define AppExe "ClashOverlay.exe"
#define AppGuid "5527E2F2-FE91-4F64-A12C-8DB4951A814F"
; AppId needs "{{" (an escaped brace) in [Setup]; the uninstall key is "{GUID}_is1".
#define AppIdSetting "{{" + AppGuid + "}"
#define UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppGuid + "}_is1"
#define RepoUrl "https://github.com/Gr1m0305/CoC-Progress-Overlay"
; The same names the app uses (App.xaml.cs): the single-instance mutexes and the event that asks every copy to exit.
#define AppMutex "Local\ClashOverlay.SingleInstance"
#define DemoMutex "Local\ClashOverlay.Demo"
#define ExitEvent "Local\ClashOverlay.Exit"
; The per-user Run value the app's own "Start with Windows" setting writes (StartupRegistration.cs).
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunValue "ClashOverlay"

[Setup]
AppId={#AppIdSetting}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Gr1m
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
AppCopyright=Copyright (c) 2026 Gr1m
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
; Per user, no admin prompt, always %LOCALAPPDATA%\Programs\ClashOverlay.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\ClashOverlay
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=ClashOverlay-Setup-{#AppVersion}
SetupIconFile=..\src\ClashOverlay.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Running copies are closed through the app's exit event in [Code], not by the Restart Manager.
CloseApplications=no
RestartApplications=no
SetupLogging=yes
#ifdef Sign
SignTool=clashsign
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "&Start Clash Tracker when I sign in to Windows"; GroupDescription: "Start-up:"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\licenses\Inter-OFL.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}\licenses"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Clash of Clans progress overlay"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Clash of Clans progress overlay"; Tasks: desktopicon

[Run]
; Last page. With no API key saved yet the app opens its first-run setup.
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
; A silent upgrade starts the copy it closed again, quietly in the tray.
Filename: "{app}\{#AppExe}"; Parameters: "--startup"; Flags: nowait; Check: RestartAfterSilentUpgrade

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

var
  IsUpgrade: Boolean;
  HadRunValue: Boolean;
  WasRunning: Boolean;
  TasksPageSynced: Boolean;
  DeleteData: Boolean;

function AppIsRunning: Boolean;
begin
  Result := CheckForMutexes('{#AppMutex},{#DemoMutex}');
end;

procedure WaitWhileRunning(TimeoutMs: Integer);
var
  Waited: Integer;
begin
  Waited := 0;
  while AppIsRunning and (Waited < TimeoutMs) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
end;

{ Asks every running copy (the app and a demo) to exit through its exit event, so it saves its settings first.
  A copy that does not answer in 10 seconds, or an older one without the event, is ended with taskkill. }
function CloseRunningApp: Boolean;
var
  ExitSignal: THandle;
  ResultCode: Integer;
begin
  if AppIsRunning then
  begin
    Log('Clash Tracker is running; asking it to exit.');
    ExitSignal := OpenEvent(EVENT_MODIFY_STATE, False, '{#ExitEvent}');
    if ExitSignal <> 0 then
    begin
      SetEvent(ExitSignal);
      WaitWhileRunning(10000);
      CloseHandle(ExitSignal);
    end;

    if AppIsRunning then
    begin
      Log('Clash Tracker did not exit; ending it with taskkill.');
      Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      WaitWhileRunning(5000);
    end;
  end;
  Result := not AppIsRunning;
end;

function StartupCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\{#AppExe}') + '" --startup';
end;

function InitializeSetup: Boolean;
begin
  IsUpgrade := RegKeyExists(HKCU, '{#UninstallKey}');
  HadRunValue := RegValueExists(HKCU, '{#RunKey}', '{#RunValue}');
  Result := True;
end;

{ On an upgrade the box shows what the app's own "Start with Windows" setting says now. }
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and IsUpgrade and not TasksPageSynced then
  begin
    TasksPageSynced := True;
    if HadRunValue then
      WizardSelectTasks('startup')
    else
      WizardSelectTasks('!startup');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  WasRunning := AppIsRunning;
  if WasRunning and not CloseRunningApp then
    Result := 'Clash Tracker is still running. Right-click its icon in the notification area, choose Exit, and run Setup again.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Startup: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    { A silent upgrade keeps the current choice; otherwise the box on the tasks page decides. }
    if WizardSilent and IsUpgrade then
      Startup := HadRunValue
    else
      Startup := WizardIsTaskSelected('startup');

    if Startup then
      RegWriteStringValue(HKCU, '{#RunKey}', '{#RunValue}', StartupCommand)
    else if RegValueExists(HKCU, '{#RunKey}', '{#RunValue}') then
      RegDeleteValue(HKCU, '{#RunKey}', '{#RunValue}');
  end;
end;

function RestartAfterSilentUpgrade: Boolean;
begin
  Result := WizardSilent and WasRunning;
end;

{ ---------- uninstall ---------- }

function UninstallParam(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
      Result := True;
end;

function UserDataDir: String;
begin
  Result := ExpandConstant('{localappdata}\ClashOverlay');
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if AppIsRunning and not CloseRunningApp then
  begin
    SuppressibleMsgBox('Clash Tracker is still running. Right-click its icon in the notification area, choose Exit, and try again.',
      mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteData := False;
    if DirExists(UserDataDir) then
    begin
      if UninstallSilent then
        DeleteData := UninstallParam('/DELETEDATA')
      else
        DeleteData := MsgBox('Do you also want to delete your Clash Tracker data?' + #13#10#13#10 +
          'Yes deletes your settings, the saved API key, your accounts, imported village exports and the downloaded game data and images in:' + #13#10 +
          UserDataDir + #13#10#13#10 +
          'No keeps them, so a later install picks up where you left off.',
          mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Remove "Start with Windows" when it points at this install. }
    if RegQueryStringValue(HKCU, '{#RunKey}', '{#RunValue}', Command) and
       (Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Command)) > 0) then
      RegDeleteValue(HKCU, '{#RunKey}', '{#RunValue}');

    if DeleteData then
    begin
      Log('Deleting ' + UserDataDir);
      if not DelTree(UserDataDir, True, True, True) then
        SuppressibleMsgBox('Some files in ' + UserDataDir + ' could not be deleted. You can delete the folder yourself.',
          mbInformation, MB_OK, IDOK);
    end
    else
      Log('Keeping ' + UserDataDir);
  end;
end;
