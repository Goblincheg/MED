; MED 1.0.0 — per-user, self-contained only. Never requires elevation.
; ISCC.exe /DPublishDir=<absolute installed payload> installer/EmojiPicker.iss
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\MED-1.0.0\installed"
#endif
#define AppName "MED"
#define AppExe "MED.exe"

[Setup]
; Permanent MED identity, distinct from upstream Classic Emoji Picker.
AppId={{64F54C6E-208D-4A1C-B37E-04A748DCE153}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
AppVerName=MED {#AppVersion} — Mikhalych Emoji Dock
AppPublisher=MED
DefaultDirName={localappdata}\Programs\MED
DefaultGroupName=MED
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayName=MED
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=output
OutputBaseFilename=MED-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UsePreviousAppDir=yes
UsePreviousTasks=yes
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Запускать MED при входе в Windows (для Win + .)"; GroupDescription: "Автозагрузка:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "portable.txt,Data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MED"; Filename: "{app}\{#AppExe}"
Name: "{group}\MED — настройки"; Filename: "{app}\{#AppExe}"; Parameters: "--settings"

[Registry]
; No automatic uninstall flag: the code below removes only this install's command,
; preserving a portable registration if the user has explicitly switched to it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MED"; ValueData: """{app}\{#AppExe}"" --background"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--background"; Description: "Запустить MED в фоне (панель: Win + .)"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{64F54C6E-208D-4A1C-B37E-04A748DCE153}_is1';

procedure InitializeWizard();
var
  Command: String;
begin
  { Preserve a user's disabled autostart on upgrade; fresh install defaults on. }
  if RegKeyExists(HKCU, UninstallKey) and
    (not RegQueryStringValue(HKCU, RunKey, 'MED', Command)) then
    WizardSelectTasks('!startup');
end;

function StopOwnedApp(): Boolean;
var
  Code: Integer;
  Exe: String;
begin
  Result := True;
  Exe := ExpandConstant('{app}\MED.exe');
  if FileExists(Exe) then
    Result := Exec(Exe, '--shutdown', ExpandConstant('{app}'), SW_HIDE,
      ewWaitUntilTerminated, Code) and (Code = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopOwnedApp() then
    Result := 'Не удалось завершить установленную копию MED. Выбери «Выход из MED» в настройках и повтори.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := StopOwnedApp();
  if not Result then
    MsgBox('Закрой MED через настройки и повтори удаление. Принудительное завершение не выполняется.', mbError, MB_OK);
end;

procedure RemoveOwnedStartup();
var
  Command, Expected: String;
begin
  Expected := '"' + ExpandConstant('{app}\MED.exe') + '" --background';
  if RegQueryStringValue(HKCU, RunKey, 'MED', Command) and
    (CompareText(Command, Expected) = 0) then
    RegDeleteValue(HKCU, RunKey, 'MED');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('startup')) then
    RemoveOwnedStartup();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemoveOwnedStartup();
  { User settings/history in AppData\MED intentionally survive uninstall. }
end;
