#define AppVersion "0.7.0-beta.33"
#define PublishDir "..\dist\AetherCockpit-0.7.0-beta.33-win-x64"

#ifdef SetupUninstallValidation
#ifndef SetupValidation
#error SetupUninstallValidation requires the isolated SetupValidation installer.
#endif
#endif

#ifdef SetupTaskRegistrationValidation
#ifndef SetupValidation
#error SetupTaskRegistrationValidation requires the isolated SetupValidation installer.
#endif
#endif

[Setup]
#ifdef SetupValidation
#ifdef SetupUninstallValidation
AppId=AetherUninstallValidation
#else
AppId=AetherSetupValidation
#endif
UsePreviousAppDir=no
UsePreviousTasks=no
#else
AppId={{7B0C30A1-7DB4-45B7-A872-F94664465F40}
UsePreviousAppDir=yes
UsePreviousTasks=yes
#endif
AppName=AETHER System Control
AppVersion={#AppVersion}
AppVerName=AETHER System Control {#AppVersion}
AppPublisher=Olaf Weins
AppCopyright=Copyright (c) 2026 Olaf Weins
AppPublisherURL=https://github.com/oa7779/AetherCockpit
#ifdef SetupValidation
DefaultDirName={tmp}\AetherSetupValidation
#ifndef SetupUninstallValidation
Uninstallable=no
#endif
#else
DefaultDirName={autopf}\AETHER System Control
#endif
DefaultGroupName=AETHER System Control
DisableProgramGroupPage=yes
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
#ifdef SetupValidation
#ifdef SetupTaskRegistrationValidation
PrivilegesRequired=admin
#else
PrivilegesRequired=lowest
#endif
#else
PrivilegesRequired=admin
#endif
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\dist
#ifdef SetupValidation
OutputBaseFilename=AetherSetupValidation
#else
OutputBaseFilename=AetherCockpit-Setup-{#AppVersion}-win-x64
#endif
SetupIconFile=..\src\SystemCockpit\Assets\aether.ico
UninstallDisplayIcon={app}\SystemCockpit.exe
VersionInfoVersion=0.7.0.33
VersionInfoProductVersion=0.7.0.33
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknuepfung erstellen"; GroupDescription: "Zusaetzliche Verknuepfungen:"; Flags: unchecked
Name: "autostart"; Description: "Gadgets und Tray bei Anmeldung starten (erhoehte Aufgabe fuer dieses Administratorkonto)"; GroupDescription: "Autostart:"
Name: "screensaver"; Description: "AETHER Mainboard-Bildschirmschoner (.scr) mitinstallieren (nicht automatisch aktivieren)"; GroupDescription: "Optional:"; Flags: unchecked

[Files]
#ifndef SetupValidation
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "Aether.scr,AetherMainboard.scr,AetherScreensaver.*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\Aether.scr"; DestDir: "{app}"; Tasks: screensaver; Flags: ignoreversion
Source: "{#PublishDir}\Aether.scr"; DestDir: "{sys}"; Tasks: screensaver; Flags: ignoreversion
Source: "{#PublishDir}\AetherMainboard.scr"; DestDir: "{app}"; Tasks: screensaver; Flags: ignoreversion
Source: "{#PublishDir}\AetherScreensaver.*"; DestDir: "{app}"; Tasks: screensaver; Flags: ignoreversion
Source: "Uninstall-Aether.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "aether-install.id"; DestDir: "{app}"; Flags: ignoreversion
#else
Source: "..\tests\SetupValidation.Code.iss"; DestDir: "{app}"; DestName: "update-probe.txt"; Flags: ignoreversion
Source: "..\tests\SetupValidation.Code.iss"; DestDir: "{app}"; DestName: "screensaver-selection-probe.txt"; Tasks: screensaver; Flags: ignoreversion
#ifdef SetupUninstallValidation
Source: "Uninstall-Aether.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "aether-install.id"; DestDir: "{app}"; Flags: ignoreversion
#else
Source: "Uninstall-Aether.ps1"; Flags: dontcopy
#endif
#endif

[Icons]
#ifndef SetupValidation
Name: "{autoprograms}\AETHER System Control"; Filename: "{app}\SystemCockpit.exe"
Name: "{autodesktop}\AETHER System Control"; Filename: "{app}\SystemCockpit.exe"; Tasks: desktopicon
Name: "{autoprograms}\AETHER Bildschirmschoner - Einstellungen"; Filename: "{app}\AetherMainboard.scr"; Parameters: "/c"; Tasks: screensaver
Name: "{autoprograms}\AETHER Bildschirmschoner - Datei anzeigen"; Filename: "{sys}\explorer.exe"; Parameters: "/select,""{app}\AetherMainboard.scr"""; Tasks: screensaver
#endif

[Registry]
#ifndef SetupValidation
Root: HKLM64; Subkey: "Software\AetherCockpit"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Tasks: screensaver; Flags: uninsdeletevalue uninsdeletekeyifempty
#endif

[Run]
#ifndef SetupValidation
Filename: "https://npcap.com/#download"; Description: "Offizielle Npcap-Downloadseite oeffnen / separat installieren"; StatusMsg: "Offizielle Npcap-Downloadseite wird geoeffnet ..."; Flags: postinstall shellexec skipifsilent unchecked; Check: NpcapDownloadAvailable
Filename: "{app}\SystemCockpit.exe"; Description: "AETHER jetzt starten / EULA beim ersten Start bestaetigen"; Flags: postinstall nowait skipifsilent runascurrentuser
#endif

[Code]
var
  DiagramPage, DesktopPage, CapturePage, UpgradePage: TInputOptionWizardPage;
  CaptureAdapter: TNewComboBox;
  AdapterNames: TStringList;
  InstalledPath, InstalledVersion, RecoveryApprovedPath: String;

#ifdef SetupValidation
procedure VerifySetup; forward;
#endif

function SetForegroundWindow(hWnd: HWND): BOOL;
  external 'SetForegroundWindow@user32.dll stdcall';

function TaskName: String;
begin
#ifdef SetupTaskRegistrationValidation
  Result := ExpandConstant('{param:TestTaskName}');
  if (Copy(Result, 1, 24) <> 'AETHER Setup Regression ') or (Length(Result) <> 56) then
    RaiseException('Invalid isolated test task name.');
#else
  Result := 'AETHER Desktop';
#endif
end;

function BoolJson(Value: Boolean): String;
begin
  if Value then Result := 'true' else Result := 'false';
end;

function PreserveInstalledConfiguration: Boolean;
begin
  Result := (InstalledPath <> '') and not UpgradePage.Values[0];
end;

function AutostartConsentGiven: Boolean;
begin
  // Interaktiv zaehlt nur das Zustimmungs-Kontrollkaestchen. Silent kann die
  // ausdrueckliche Zustimmung nur per Kommandozeile /AutostartConsent=1 erteilen.
  Result := DesktopPage.Values[1];
  if WizardSilent and not Result then
    Result := ExpandConstant('{param:AutostartConsent|0}') = '1';
end;

procedure DetectInstallation;
var
  Key: String;
begin
  InstalledPath := '';
  InstalledVersion := '';
#ifndef SetupValidation
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7B0C30A1-7DB4-45B7-A872-F94664465F40}_is1';
  if not RegQueryStringValue(HKLM64, Key, 'Inno Setup: App Path', InstalledPath) then
    RegQueryStringValue(HKLM32, Key, 'Inno Setup: App Path', InstalledPath);
  if not RegQueryStringValue(HKLM64, Key, 'DisplayVersion', InstalledVersion) then
    RegQueryStringValue(HKLM32, Key, 'DisplayVersion', InstalledVersion);
  if (InstalledPath <> '') and
    (not FileExists(InstalledPath + '\SystemCockpit.exe') or
     not FileExists(InstalledPath + '\unins000.exe')) then begin
    Log('AETHER: obsolete installation entry without complete installed application: ' + InstalledPath);
    InstalledPath := '';
  end;
#else
  if ExpandConstant('{param:TestUpgrade|0}') = '1' then begin
    InstalledPath := ExpandConstant('{tmp}\AetherSetupValidation');
    InstalledVersion := '0.7.0-beta.10';
    if not ForceDirectories(InstalledPath) or
      not SaveStringToFile(InstalledPath + '\desktop-defaults.json', '{"Id":"existing-profile","Gadgets":false}', False) then
      RaiseException('Cannot create isolated previous-installation fixture.');
    if not SaveStringToFile(InstalledPath + '\update-probe.txt', 'old-version-payload', False) then
      RaiseException('Cannot create isolated previous-version file.');
  end;
#endif
end;

procedure InitializeWizard;
var
  Wmi, Rows, Row: Variant;
  Count: Integer;
begin
  SetForegroundWindow(WizardForm.Handle);
  DetectInstallation;
  UpgradePage := CreateInputOptionPage(wpWelcome, 'Installation / Update',
    'Vorhandene AETHER-Installation pruefen',
    'Bei einem Update werden Programmdateien im bisherigen Ordner ersetzt. Benutzerdaten bleiben erhalten. ' +
    'AETHER vorher im Tray vollstaendig beenden; nicht zuvor deinstallieren.', False, False);
  UpgradePage.Add('Desktop-/Diagramm-/Autostart-Konfiguration neu festlegen (sonst unveraendert behalten)');
#ifdef SetupValidation
  UpgradePage.Values[0] := ExpandConstant('{param:TestReconfigure|0}') = '1';
#endif
  if InstalledPath <> '' then begin
    WizardForm.DirEdit.Text := InstalledPath;
    UpgradePage.SubCaptionLabel.Caption := 'Update: ' + InstalledVersion + ' -> {#AppVersion}';
    UpgradePage.Caption := 'Vorhandene Installation: ' + InstalledPath;
    WizardForm.WelcomeLabel2.Caption := 'AETHER ' + InstalledVersion + ' ist installiert in:' + #13#10 +
      InstalledPath + #13#10#13#10 + 'Dieses Setup aktualisiert auf {#AppVersion}, ohne vorherige Deinstallation. ' +
      'Ihre Einstellungen, Logs und Aufzeichnungen bleiben erhalten.';
  end;
  DiagramPage := CreateInputOptionPage(wpSelectTasks, 'Desktop-Parameter',
    'Welche Diagramme sollen angezeigt werden?', 'Spaeter in AETHER unter Einstellungen aenderbar.', False, False);
  DiagramPage.Add('CPU'); DiagramPage.Add('RAM'); DiagramPage.Add('GPU');
  DiagramPage.Add('CPU-/GPU-/SSD-Temperaturen'); DiagramPage.Add('Netzwerk Download/Upload');
  DiagramPage.Add('Laufwerksbelegung als Torten');
  for Count := 0 to 5 do DiagramPage.Values[Count] := True;
  DesktopPage := CreateInputOptionPage(DiagramPage.ID, 'Gadgets und Autostart',
    'Desktop-Gadgets bleiben unabhaengig vom Hauptfenster aktiv.',
    'Die erhoehte Aufgabe startet bei Anmeldung Gadgets und Tray, nicht das Hauptfenster. ' +
    'Sie gilt fuer dieses Administratorkonto: ' + GetUserNameString + '. ' +
    'Zum Beenden das Symbol bei der Uhr verwenden. Beim ersten manuellen Start EULA bestaetigen.', False, False);
  DesktopPage.Add('Desktop-Gadgets aktivieren'); DesktopPage.Values[0] := True;
  DesktopPage.Add('Ich genehmige ausdruecklich die erhoehte Autostart-Aufgabe fuer dieses Konto.');
  CapturePage := CreateInputOptionPage(DesktopPage.ID, 'Optionaler Npcap-Mitschnitt',
    'Npcap wird NICHT mitgeliefert. Bei ausdruecklicher Auswahl kann Setup nach der Installation die offizielle Downloadseite oeffnen; Download und Installation erfolgen durch Sie.',
    'Nur eigene/berechtigte Schnittstelle. Maximal 128 Bytes/Paket, 100 MB oder 15 Minuten, ' +
    'ein Mitschnitt je Anmeldung. Lokale Dateien fuer 7 Tage, Gesamtbestand ca. 500 MB begrenzt. ' +
    'Auch Paketkoepfe koennen vertrauliche Inhalte enthalten. Keine Cloud, Entschluesselung oder Blockierung.', False, False);
  CapturePage.Add('Netzwerkmitschnitt gewuenscht / Npcap-Downloadseite nach Setup anbieten');
  CapturePage.Add('Automatischen Mitschnitt bei Anmeldung aktivieren');
  CapturePage.Add('Ich genehmige diesen Mitschnitt auch fuer zukuenftige Anmeldungen bis zur Deaktivierung.');
  CaptureAdapter := TNewComboBox.Create(WizardForm);
  CapturePage.CheckListBox.Height := ScaleY(70);
  CaptureAdapter.Parent := CapturePage.Surface;
  CaptureAdapter.Top := CapturePage.CheckListBox.Top + CapturePage.CheckListBox.Height + ScaleY(8);
  CaptureAdapter.Width := CapturePage.SurfaceWidth;
  CaptureAdapter.Style := csDropDownList;
  AdapterNames := TStringList.Create;
  try
    Wmi := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Wmi.ConnectServer('.', 'root\CIMV2');
    Rows := Wmi.ExecQuery('SELECT GUID, NetConnectionID FROM Win32_NetworkAdapter WHERE NetEnabled=True');
    for Count := 0 to Rows.Count - 1 do begin
      Row := Rows.ItemIndex(Count);
      if not VarIsNull(Row.GUID) then begin
        AdapterNames.Add('\Device\NPF_' + String(Row.GUID));
        if VarIsNull(Row.NetConnectionID) then CaptureAdapter.Items.Add(String(Row.GUID))
        else CaptureAdapter.Items.Add(String(Row.NetConnectionID));
      end;
    end;
    if CaptureAdapter.Items.Count > 0 then CaptureAdapter.ItemIndex := 0;
  except
    Log('Netzwerkschnittstellen nicht lesbar: ' + GetExceptionMessage);
    CapturePage.SubCaptionLabel.Caption := 'Keine Schnittstellen lesbar. Mitschnitt spaeter in der App konfigurieren.';
  end;
#ifdef SetupValidation
  DesktopPage.Values[1] := ExpandConstant('{param:TestPageConsent|1}') = '1';
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Entry: TFindRec;
  HasFiles, CheckDirectory, Approved: Boolean;
  Code, Index: Integer;
  Output: TExecOutput;
  Directory, Parameters: String;
begin
  Result := '';
  Directory := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
#ifdef SetupValidation
  CheckDirectory := ExpandConstant('{param:TestRecovery|0}') = '1';
#else
  CheckDirectory := True;
#endif
  HasFiles := False;
  if CheckDirectory and FindFirst(Directory + '\*', Entry) then begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then HasFiles := True;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
  if CheckDirectory and (InstalledPath = '') and HasFiles then begin
    ExtractTemporaryFile('Uninstall-Aether.ps1');
    Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{tmp}\Uninstall-Aether.ps1') + '" -InspectRecoveryDirectory -InstallDirectory "' + Directory + '"';
    if not ExecAndCaptureOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters,
      '', SW_HIDE, ewWaitUntilTerminated, Code, Output) then begin
      Result := 'Der vorhandene Ordner konnte nicht sicher geprueft werden.';
      Exit;
    end;
    for Index := 0 to GetArrayLength(Output.StdOut) - 1 do Log(Output.StdOut[Index]);
    for Index := 0 to GetArrayLength(Output.StdErr) - 1 do Log(Output.StdErr[Index]);
    if Output.Error or (Code <> 0) then begin
      Result := 'Eine Neuinstallation braucht einen leeren, eigenen AETHER-Ordner. ' +
        'Der vorhandene Ordner wurde nicht als sicherer AETHER-Restordner erkannt (Details im Setup-Log). ' +
        'Fremde Dateien zuerst pruefen oder einen anderen leeren Ordner waehlen.';
      Exit;
    end;
    if CompareText(RecoveryApprovedPath, Directory) <> 0 then begin
#ifdef SetupValidation
      Approved := ExpandConstant('{param:TestRecoveryConsent|0}') = '1';
#else
      Approved := False;
      if WizardSilent then begin
        // Silent gilt die Wiederherstellung nur mit ausdruecklichem /RecoveryConsent=1.
        Approved := ExpandConstant('{param:RecoveryConsent|0}') = '1';
        if Approved then
          Log('AETHER SILENT RECOVERY CONSENT: per Kommandozeile erteilt fuer ' + Directory);
      end
      else
        Approved := MsgBox('Unvollstaendige AETHER-Installation wiederherstellen?' + #13#10#13#10 +
          Directory + #13#10#13#10 +
          'AETHER-Programmdateien wurden erkannt, aber kein vollstaendiger Installationseintrag. ' +
          'AETHER vorher im Tray vollstaendig beenden. Dieses Setup ersetzt Programmdateien und richtet ' +
          'den Uninstaller neu ein. Kontoeinstellungen und andere vorhandene Dateien werden jetzt nicht geloescht. ' +
          'Ihre hier gewaehlte Setup-Konfiguration wird angewendet.' + #13#10#13#10 +
          'Wichtig: Bei einer spaeteren bestaetigten Deinstallation werden der GESAMTE Zielordner und ' +
          'die AETHER-Kontodaten entfernt. Nur fortfahren, wenn dieser Ordner ausschliesslich fuer AETHER bestimmt ist.',
          mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
#endif
      if not Approved then begin
        Result := 'Wiederherstellung nicht bestaetigt. Es wurden keine Programmdateien oder Nutzerdaten geaendert.';
        Exit;
      end;
      RecoveryApprovedPath := Directory;
      Log('AETHER RECOVERY CONSENT PASS: ' + Directory);
    end;
  end;
  if (InstalledPath <> '') and
    (CompareText(RemoveBackslashUnlessRoot(ExpandConstant('{app}')), RemoveBackslashUnlessRoot(InstalledPath)) <> 0) then begin
    Result := 'Ein Update muss im erkannten Installationsordner ausgefuehrt werden: ' + InstalledPath;
    Exit;
  end;
  if PreserveInstalledConfiguration then Exit;
  if WizardIsTaskSelected('autostart') and not AutostartConsentGiven then begin
    if WizardSilent then
      // Silent-Installation bricht nicht ab: Die erhoehte Aufgabe wird ohne
      // ausdrueckliche Zustimmung schlicht nicht eingerichtet.
      Log('AETHER SILENT AUTOSTART SKIPPED: keine ausdrueckliche Zustimmung (/AutostartConsent=1 fehlt); Aufgabe wird nicht eingerichtet.')
    else
      Result := 'Keine ausdrueckliche Zustimmung zur erhoehten Autostart-Aufgabe.';
  end;
  if CapturePage.Values[1] and not CapturePage.Values[2] then
    Result := 'Keine ausdrueckliche Zustimmung zum automatischen Mitschnitt.';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = UpgradePage.ID) and (InstalledPath = '')) or
    (PreserveInstalledConfiguration and ((PageID = wpSelectTasks) or (PageID = DiagramPage.ID) or
      (PageID = DesktopPage.ID) or (PageID = CapturePage.ID)));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if PreserveInstalledConfiguration then Exit;
  if not WizardSilent and (CurPageID = DesktopPage.ID) and WizardIsTaskSelected('autostart') and not DesktopPage.Values[1] then begin
    MsgBox('Autostart benoetigt Ihre ausdrueckliche Zustimmung. Alternativ die Autostart-Aufgabe abwaehlen.', mbError, MB_OK);
    Result := False;
  end;
  if (CurPageID = CapturePage.ID) and CapturePage.Values[1] then begin
    if not CapturePage.Values[2] or not WizardIsTaskSelected('autostart') or
      (CaptureAdapter.ItemIndex < 0) or not FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) then begin
      MsgBox('Automatischer Mitschnitt erfordert Zustimmung, Autostart, eine Schnittstelle und separat installiertes Npcap. Es wird kein Treiber installiert.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function NpcapDownloadAvailable: Boolean;
begin
  Result := not WizardSilent and CapturePage.Values[0] and
    not FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll'));
end;

procedure RunTaskCommand(Params: String);
var
  Output: TExecOutput;
  Code, Index: Integer;
  ErrorText: String;
begin
  if not ExecAndCaptureOutput(ExpandConstant('{sys}\schtasks.exe'), Params, '', SW_HIDE,
    ewWaitUntilTerminated, Code, Output) then
    RaiseException('Windows-Aufgabenplanung konnte nicht gestartet werden: ' + SysErrorMessage(Code));
  ErrorText := '';
  for Index := 0 to GetArrayLength(Output.StdOut) - 1 do begin
    Log(Output.StdOut[Index]);
    ErrorText := ErrorText + Output.StdOut[Index] + #13#10;
  end;
  for Index := 0 to GetArrayLength(Output.StdErr) - 1 do begin
    Log(Output.StdErr[Index]);
    ErrorText := ErrorText + Output.StdErr[Index] + #13#10;
  end;
  if Output.Error or (Code <> 0) then
    RaiseException('Windows-Aufgabenplanung, Exitcode ' + IntToStr(Code) + ': ' + ErrorText);
end;

procedure RegisterDesktopTask(Definition: Variant);
var
  FileName: String;
  Xml: Variant;
begin
  FileName := ExpandConstant('{tmp}\aether-desktop-task.xml');
  Xml := CreateOleObject('Msxml2.DOMDocument.6.0');
  Xml.async := False;
  if not Xml.loadXML(Definition.XmlText) then
    RaiseException('Autostart-Aufgabendefinition ist ungueltig: ' + String(Xml.parseError.reason));
  try
    Xml.save(FileName);
#if defined(SetupValidation) && !defined(SetupTaskRegistrationValidation)
    Log('VALIDATION: TASK_XML_VALIDATION passed; no task registered.');
#else
    // XML avoids passing credential variants through Pascal Script's COM bridge.
    RunTaskCommand('/Create /TN "' + TaskName + '" /XML "' + FileName + '" /F');
#endif
  finally
    if not DeleteFile(FileName) then Log('AETHER: temporary task XML could not be removed: ' + FileName);
  end;
end;

procedure ConfigureTask(Enable: Boolean);
var
  Service, Folder, Definition, Trigger, Action, Tasks, Existing: Variant;
  User: String;
  Index: Integer;
#ifdef SetupTaskRegistrationValidation
  Registered: Variant;
  RegistrationSucceeded: Boolean;
#endif
begin
  Log('AETHER task: connecting to Task Scheduler.');
  Service := CreateOleObject('Schedule.Service');
  Service.Connect;
  Folder := Service.GetFolder('\');
  Log('AETHER task: connected.');
  if not Enable then begin
#if defined(SetupValidation) && !defined(SetupTaskRegistrationValidation)
    Log('VALIDATION: task deletion skipped; no scheduler changes.');
    Exit;
#endif
    Tasks := Folder.GetTasks(1);
    for Index := 1 to Tasks.Count do begin
      Existing := Tasks.Item(Index);
      if CompareText(String(Existing.Name), TaskName) = 0 then begin
        RunTaskCommand('/Delete /TN "' + TaskName + '" /F');
        Exit;
      end;
    end;
    Log('Keine bestehende AETHER-Autostart-Aufgabe fuer dieses Konto.');
    Exit;
  end;
  User := GetEnv('USERDOMAIN') + '\' + GetUserNameString;
  Log('AETHER task: building task definition.');
  Definition := Service.NewTask(0);
  Definition.RegistrationInfo.Description := 'AETHER: explizit genehmigter Desktop-/Tray-Start bei Benutzeranmeldung.';
  Definition.Principal.UserId := User;
  Definition.Principal.LogonType := 3;
  Definition.Principal.RunLevel := 1;
  Definition.Settings.Enabled := True;
  Definition.Settings.ExecutionTimeLimit := 'PT0S';
  Definition.Settings.DisallowStartIfOnBatteries := False;
  Definition.Settings.StopIfGoingOnBatteries := False;
  Definition.Settings.MultipleInstances := 2;
#ifdef SetupTaskRegistrationValidation
  Definition.Settings.Enabled := False;
#else
  Trigger := Definition.Triggers.Create(9);
  Trigger.UserId := User;
#endif
  Action := Definition.Actions.Create(0);
  Action.Path := ExpandConstant('{app}\SystemCockpit.exe');
  Action.Arguments := '--tray';
  Action.WorkingDirectory := ExpandConstant('{app}');
  Log('AETHER task: definition complete.');
#ifdef SetupValidation
#ifdef SetupTaskRegistrationValidation
  RegistrationSucceeded := False;
  try
    Log('AETHER task: registering isolated disabled task.');
    RegisterDesktopTask(Definition);
    RegistrationSucceeded := True;
    Registered := Folder.GetTask(TaskName);
    if Registered.Enabled then
      RaiseException('Isolated test task must remain disabled.');
    if Registered.Definition.Triggers.Count <> 0 then
      RaiseException('Isolated test task must not contain triggers.');
    if (Registered.Definition.Principal.LogonType <> 3) or
      (Registered.Definition.Principal.RunLevel <> 1) then
      RaiseException('Registered task must use the approved elevated interactive token.');
    if (Registered.Definition.Actions.Count <> 1) or
      (String(Registered.Definition.Actions.Item(1).Arguments) <> '--tray') then
      RaiseException('Registered task must contain only the tray startup action.');
    Log('AETHER TASK REGISTRATION PASS');
  finally
    if RegistrationSucceeded then begin
      Log('AETHER task: removing isolated task.');
      ConfigureTask(False);
      Log('AETHER TASK CLEANUP PASS');
    end;
  end;
#else
  Log('AETHER task: validating task XML without registration.');
  RegisterDesktopTask(Definition);
#endif
#else
  Log('AETHER task: registering approved logon task.');
  RegisterDesktopTask(Definition);
  Log('AETHER task: registration completed.');
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Json, Device: String;
begin
  if CurStep <> ssPostInstall then Exit;
  if PreserveInstalledConfiguration then begin
    Log('AETHER: update preserves desktop profile, user settings and existing autostart task.');
#ifdef SetupValidation
    VerifySetup;
#endif
    Exit;
  end;
  Log('AETHER: preparing desktop profile.');
  Device := '';
  if CapturePage.Values[1] then begin
    Device := AdapterNames[CaptureAdapter.ItemIndex];
    StringChangeEx(Device, '\', '\\', True);
  end;
  Log('AETHER: serializing desktop profile.');
  Json := '{"Id":"' + GetDateTimeString('yyyymmddhhnnss', '-', ':') + '",' +
    '"Gadgets":' + BoolJson(DesktopPage.Values[0]) +
    ',"Cpu":' + BoolJson(DiagramPage.Values[0]) + ',"Ram":' + BoolJson(DiagramPage.Values[1]) +
    ',"Gpu":' + BoolJson(DiagramPage.Values[2]) + ',"Temperatures":' + BoolJson(DiagramPage.Values[3]) +
    ',"Network":' + BoolJson(DiagramPage.Values[4]) + ',"Drives":' + BoolJson(DiagramPage.Values[5]) +
    ',"CaptureAtLogon":' + BoolJson(CapturePage.Values[1]) + ',"CaptureDevice":"' + Device + '"}';
  Log('AETHER: writing desktop profile.');
  if not SaveStringToFile(ExpandConstant('{app}\desktop-defaults.json'), Json, False) then
    RaiseException('Desktop-Konfiguration konnte nicht gespeichert werden.');
  Log('AETHER: configuring desktop autostart.');
  try
    if WizardIsTaskSelected('autostart') and not AutostartConsentGiven then
      Log('AETHER: Autostart-Aufgabe ohne ausdrueckliche Zustimmung nicht eingerichtet.');
    ConfigureTask(WizardIsTaskSelected('autostart') and AutostartConsentGiven);
  except
    Log('AETHER task configuration failed: ' + GetExceptionMessage);
    RaiseException('Die Programmdateien wurden installiert, aber die Autostart-Aufgabe konnte nicht eingerichtet werden. ' +
      'Windows-Aufgabenplanung: ' + GetExceptionMessage);
  end;
#ifdef SetupValidation
  VerifySetup;
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode, Index: Integer;
  Parameters, ScriptPath, ErrorText: String;
  Output: TExecOutput;
begin
  if CurUninstallStep = usUninstall then begin
    try
      ConfigureTask(False);
    except
      Log('AETHER task removal failed: ' + GetExceptionMessage);
      RaiseException('Die AETHER-Autostart-Aufgabe konnte nicht entfernt werden. Windows-Aufgabenplanung: ' +
        GetExceptionMessage);
    end;
    ScriptPath := ExpandConstant('{app}\Uninstall-Aether.ps1');
    if not FileExists(ScriptPath) then
      RaiseException('AETHER-Deinstallationshilfe fehlt. Aktuelles Setup zuerst als Update installieren.');
    Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath +
      '" -InstallDirectory "' + ExpandConstant('{app}') + '"';
#ifdef SetupUninstallValidation
    Parameters := Parameters + ' -DataDirectory "' + ExpandConstant('{app}\..\data') +
      '" -ValidationRoot "' + ExpandConstant('{app}\..') + '"';
#else
    Parameters := Parameters + ' -DataDirectory "' + ExpandConstant('{localappdata}\AetherCockpit') + '"';
#endif
    if not ExecAndCaptureOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters,
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Output) then
      RaiseException('AETHER konnte die Deinstallationshilfe nicht starten.');
    ErrorText := '';
    for Index := 0 to GetArrayLength(Output.StdOut) - 1 do Log(Output.StdOut[Index]);
    for Index := 0 to GetArrayLength(Output.StdErr) - 1 do begin
      Log(Output.StdErr[Index]);
      ErrorText := ErrorText + Output.StdErr[Index] + #13#10;
    end;
    if Output.Error or (ResultCode <> 0) then
      RaiseException('AETHER-Prozesse oder Nutzerdaten konnten nicht vollstaendig entfernt werden (Exit ' +
        IntToStr(ResultCode) + '). ' + ErrorText);
  end;
end;

function InitializeUninstall: Boolean;
begin
  // Silent (/SILENT oder /VERYSILENT) gilt die Kommandozeile des Administrators
  // als ausdrueckliche Bestaetigung; interaktiv bleibt die Abfrage bestehen.
  Result := UninstallSilent;
  if Result then
    Log('AETHER SILENT UNINSTALL: Bestaetigung per Silent-Kommandozeile erteilt; Installationsordner und AETHER-Kontodaten werden entfernt.')
  else
    Result := MsgBox('AETHER vollstaendig entfernen?' + #13#10#13#10 +
      'Laufende AETHER-App, Gadgets, Tray und Bildschirmschoner dieser Installation werden beendet. ' +
      'Installationsordner sowie Einstellungen, Logs, Aufzeichnungen und Sicherungen dieses Windows-Kontos werden geloescht. ' +
      'Externe Dateien und andere portable Versionen bleiben erhalten.',
      mbConfirmation, MB_YESNO) = IDYES;
end;

#ifdef SetupValidation
#include "..\tests\SetupValidation.Code.iss"
#endif

[UninstallDelete]
#if !defined(SetupValidation) || defined(SetupUninstallValidation)
Type: files; Name: "{app}\desktop-defaults.json"
Type: filesandordirs; Name: "{app}"
#endif
