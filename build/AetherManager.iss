#ifndef AppVersion
#error Build with Publish-ManagerSetup.ps1.
#endif
#ifndef AppFileVersion
#error AppFileVersion is required.
#endif
#ifndef PublishDir
#error PublishDir is required.
#endif

[Setup]
AppId={{E5405CEE-80F0-4C6C-A0A9-5F9149C92D71}
AppName=AETHER Management Server
AppVersion={#AppVersion}
AppVerName=AETHER Management Server {#AppVersion}
AppPublisher=Olaf Weins
AppPublisherURL=https://github.com/oa7779/AetherCockpit
AppCopyright=Copyright (c) 2026 Olaf Weins
DefaultDirName={autopf}\AETHER Management Server
DisableDirPage=yes
UsePreviousAppDir=no
DefaultGroupName=AETHER Management Server
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
OutputDir=..\dist
OutputBaseFilename=AetherManager-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\SystemCockpit\Assets\aether.ico
UninstallDisplayIcon={app}\AetherManager.Server.exe
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppFileVersion}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=no
RestartApplications=no

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "docs\*,src\AetherManager.Server\README.md"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\AetherManager.Server\README.md"; DestDir: "{app}\src\AetherManager.Server"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\AETHER Management Server\Serverdienst einrichten (als Administrator)"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NoExit -File ""{app}\Install-ManagerServer.ps1"""; WorkingDir: "{app}"
Name: "{autoprograms}\AETHER Management Server\Anleitung"; Filename: "{app}\docs\CENTRAL-MANAGEMENT.md"

[Registry]
Root: HKLM64; Subkey: "Software\AetherManagerServer"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NoExit -File ""{app}\Install-ManagerServer.ps1"""; WorkingDir: "{app}"; Description: "Dienstassistenten als Administrator oeffnen (HTTPS-Zertifikat erforderlich; Einrichtung erst nach JA)"; Flags: postinstall nowait skipifsilent unchecked

[Code]
const
  ServiceKey = 'SYSTEM\CurrentControlSet\Services\AetherManagerServer';

function GetFileAttributes(lpFileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function SafeDirectory(Directory: String): Boolean;
var
  Attributes: LongWord;
  Parent: String;
begin
  Result := True;
  while Directory <> '' do begin
    Attributes := GetFileAttributes(Directory);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then begin
      Result := False;
      Exit;
    end;
    Parent := ExtractFileDir(Directory);
    if CompareText(Parent, Directory) = 0 then Break;
    Directory := Parent;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Directory, Registered: String;
begin
  Result := '';
  if RegKeyExists(HKLM64, ServiceKey) then begin
    Result := 'Der Serverdienst ist noch registriert. Vor einem Update Daten sichern und ' +
      'den Dienst mit Install-ManagerServer.ps1 -HostName localhost ' +
      '-CertificateThumbprint unused -Remove aus seinem bisherigen Installationsordner ' +
      'als Administrator entfernen. ProgramData bleibt erhalten. Danach Setup erneut starten. ' +
      'Es werden keine laufenden Serverdateien ersetzt.';
    Exit;
  end;
  Directory := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  if not SafeDirectory(Directory) then begin
    Result := 'Der Installationspfad darf keine Verknuepfung oder Junction enthalten.';
    Exit;
  end;
  if DirExists(Directory) then begin
    if not RegQueryStringValue(HKLM64, 'Software\AetherManagerServer', 'InstallPath', Registered) then
      Registered := '';
    if CompareText(RemoveBackslashUnlessRoot(Registered), Directory) <> 0 then
      Result := 'Der Zielordner existiert ohne passenden Server-Setup-Eintrag. ' +
        'Dateien zuerst sichern und den Ordner manuell pruefen. Er wird nicht ueberschrieben.';
  end;
end;

procedure InitializeWizard;
var
  Page: TOutputMsgWizardPage;
begin
  Page := CreateOutputMsgPage(wpWelcome, 'Serverbetrieb bewusst einrichten',
    'Programmdateien und Windows-Dienst sind getrennte Schritte.',
    'Dieses Setup kopiert den selbstenthaltenen Server nach Program Files.' + #13#10#13#10 +
    'Es erstellt kein Zertifikat, oeffnet keine Firewall und startet keinen Server automatisch. ' +
    'Am Ende kann der Dienstassistent optional geoeffnet werden. Er fragt DNS-Hostname und ' +
    'Zertifikatfingerabdruck ab; der HTTPS-Port ist 5187. Der Dienst laeuft als LocalSystem ' +
    'und wird erst nach Eingabe von JA eingerichtet.' + #13#10#13#10 +
    'Voraussetzung: gueltiges vertrauenswuerdiges Serverzertifikat mit privatem Schluessel ' +
    'unter Lokaler Computer / Eigene Zertifikate. DNS/SAN, Schluesselrechte, VPN und ' +
    'Netzwerkzugriff muessen separat eingerichtet werden.' + #13#10#13#10 +
    'Vor Updates: Daten sichern und den bisherigen Dienst entfernen. Bei der ' +
    'Deinstallation wird nur der zu diesem Ordner gehoerende Dienst entfernt. ' +
    'Berichte, Konten, Audit und Zugangsdaten unter ProgramData bleiben erhalten.');
end;

function InitializeUninstall: Boolean;
var
  ImagePath, Expected, Parameters: String;
  Code: Integer;
begin
  Result := False;
  if not SafeDirectory(ExpandConstant('{app}')) then begin
    MsgBox('Unsicherer Installationspfad. Deinstallation abgebrochen.', mbError, MB_OK);
    Exit;
  end;
  if RegKeyExists(HKLM64, ServiceKey) then begin
    Expected := '"' + ExpandConstant('{app}\AetherManager.Server.exe') + '"';
    if not RegQueryStringValue(HKLM64, ServiceKey, 'ImagePath', ImagePath) then ImagePath := '';
    if CompareText(ImagePath, Expected) <> 0 then begin
      MsgBox('Der Serverdienst gehoert nicht eindeutig zu diesem Ordner. ' +
        'Deinstallation abgebrochen; Dienst und Dateien bleiben erhalten.', mbError, MB_OK);
      Exit;
    end;
    // Silent removal cannot silently authorize stopping an infrastructure service.
    if UninstallSilent then begin
      Log('Serverdienst zuerst ausdruecklich mit dem Dienstassistenten entfernen.');
      Exit;
    end;
    if MsgBox('AETHER-Serverdienst stoppen und entfernen und die Programmdateien deinstallieren?' + #13#10 +
      'Verbindungen werden unterbrochen. ProgramData mit Berichten, Konten, Audit ' +
      'und Zugangsdaten bleibt erhalten.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then Exit;
    Parameters := '-NoLogo -NoProfile -NonInteractive -File "' +
      ExpandConstant('{app}\Install-ManagerServer.ps1') +
      '" -HostName localhost -CertificateThumbprint unused -Remove';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      Parameters, ExpandConstant('{app}'), SW_SHOW, ewWaitUntilTerminated, Code) then begin
      MsgBox('Dienstentfernung konnte nicht gestartet werden. Deinstallation abgebrochen.', mbError, MB_OK);
      Exit;
    end;
    if (Code <> 0) or RegKeyExists(HKLM64, ServiceKey) then begin
      MsgBox('Dienstentfernung nicht abgeschlossen. Deinstallation abgebrochen; ' +
        'Programmdateien bleiben erhalten. Dienststatus als Administrator pruefen.', mbError, MB_OK);
      Exit;
    end;
  end;
  Result := True;
end;
