procedure VerifySetup;
var
  Json: AnsiString;
  Value: String;
  Restart: Boolean;
  Index: Integer;
  OriginalPath: String;
begin
  if FileExists(ExpandConstant('{app}\screensaver-selection-probe.txt')) <> WizardIsTaskSelected('screensaver') then
    RaiseException('Optional screensaver payload does not match explicit selection.');
  Log('AETHER OPTIONAL SCREENSAVER PASS');
  if not LoadStringFromFile(ExpandConstant('{app}\update-probe.txt'), Json) or
    (Pos('procedure VerifySetup;', String(Json)) = 0) then
    RaiseException('Setup did not replace the previous-version payload at the detected location.');
  Log('AETHER FILE REPLACEMENT PASS');
  if not LoadStringFromFile(ExpandConstant('{app}\desktop-defaults.json'), Json) then
    RaiseException('Installed profile was not written.');
  Value := String(Json);
  if PreserveInstalledConfiguration then begin
    if Value <> '{"Id":"existing-profile","Gadgets":false}' then
      RaiseException('Update overwrote existing desktop settings.');
    if not ShouldSkipPage(DiagramPage.ID) or not ShouldSkipPage(wpSelectTasks) then
      RaiseException('Preserving update must skip configuration/task pages.');
    Restart := False;
    if PrepareToInstall(Restart) <> '' then
      RaiseException('Preserving update must not request new task consent.');
    OriginalPath := InstalledPath;
    InstalledPath := OriginalPath + '\wrong-directory';
    if PrepareToInstall(Restart) = '' then
      RaiseException('Update to a different directory was accepted.');
    InstalledPath := OriginalPath;
    Log('AETHER UPDATE PRESERVATION PASS');
    Log('AETHER SETUP VALIDATION PASS');
    Exit;
  end;
  if Pos('"CaptureAtLogon":false', Value) = 0 then
    RaiseException('Profile did not preserve the no-capture default.');
  if Copy(Value, 1, 7) <> '{"Id":"' then
    RaiseException('Setup profile ID is missing.');
  for Index := 8 to 21 do
    if (Value[Index] < '0') or (Value[Index] > '9') then
      RaiseException('Setup profile ID must contain 14 date/time digits.');
  if Pos('"Gadgets":true,"Cpu":true,"Ram":true,"Gpu":true', Value) = 0 then
    RaiseException('Selected diagram defaults were not serialized.');
  if Pos('"CaptureDevice":""}', Value) = 0 then
    RaiseException('No-capture profile contains an unexpected device.');
  Restart := False;
  DesktopPage.Values[1] := False;
  if WizardIsTaskSelected('autostart') then begin
    if ExpandConstant('{param:AutostartConsent|0}') = '1' then begin
      if not AutostartConsentGiven then
        RaiseException('Explicit silent autostart consent parameter was ignored.');
    end
    else begin
      if AutostartConsentGiven then
        RaiseException('Silent setup must not imply elevated-task consent.');
      if PrepareToInstall(Restart) <> '' then
        RaiseException('Silent install without autostart consent must proceed while skipping the task.');
    end;
  end;
  DesktopPage.Values[1] := True;
  CapturePage.Values[0] := True;
  CapturePage.Values[1] := False;
  CapturePage.Values[2] := False;
  if PrepareToInstall(Restart) <> '' then
    RaiseException('Npcap download offer must not require capture consent.');
  CapturePage.Values[1] := True;
  if PrepareToInstall(Restart) = '' then
    RaiseException('Missing capture consent was accepted.');
  CapturePage.Values[0] := False;
  CapturePage.Values[1] := False;
  if PrepareToInstall(Restart) <> '' then
    RaiseException('Valid no-capture consent was rejected.');
  Log('AETHER SETUP VALIDATION PASS');
end;
