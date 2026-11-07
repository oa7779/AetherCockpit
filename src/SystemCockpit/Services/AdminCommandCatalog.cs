namespace SystemCockpit.Services;

public sealed record AdminParameter(string Text, string Description);
public sealed record AdminCommand(string Category, string Command, string Description, string Impact,
    IReadOnlyList<AdminParameter> Parameters)
{
    public string SearchText => $"{Category} {Command} {Description} {Impact} " +
        string.Join(" ", Parameters.Select(p => $"{p.Text} {p.Description}"));
    // 0 = lesend, 1 = Netzwerkkontakt, 2 = aendernd; direkt aus der Impact-Kennzeichnung abgeleitet.
    public int RiskLevel => Impact.StartsWith("LESEND", StringComparison.Ordinal) ? 0
        : Impact.StartsWith("NETZWERK", StringComparison.Ordinal) ? 1 : 2;
    public string RiskBadge => RiskLevel switch { 0 => "RISIKO: LESEND", 1 => "RISIKO: NETZWERK", _ => "RISIKO: AENDERND" };
    public System.Windows.Media.Brush RiskAccent => RiskLevel switch
    {
        0 => System.Windows.Media.Brushes.MediumAquamarine,
        1 => System.Windows.Media.Brushes.Khaki,
        _ => System.Windows.Media.Brushes.Orange
    };
}

public static class AdminCommandCatalog
{
    public static IReadOnlyList<AdminCommand> All { get; } = Build();

    // Kategorie-Leitfaden im Detailbereich: wann ein Befehl sinnvoll ist und was zu beachten ist.
    // Bewusst je Kategorie gepflegt statt pro Befehl geraten; die Befehlsbeschreibung bleibt die Quelle fuer Details.
    public static (string WhenUseful, string Caution) Guidance(string category) => category switch
    {
        "WinGet" => ("Software gezielt suchen, installieren, aktualisieren oder entfernen - oder komfortabel im Modul Pakete arbeiten.",
            "Aendernde Befehle installieren/entfernen sofort. Exakte IDs verwenden; laufende Programme vorher schliessen."),
        "DISM" => ("Wenn Windows-Komponenten beschaedigt wirken: erst CheckHealth/ScanHealth lesen, dann gezielt RestoreHealth.",
            "RestoreHealth veraendert Systemdateien und kann Windows Update kontaktieren. Vorher Wiederherstellungspunkt anlegen."),
        "SFC" => ("Bei fehlerhaften Systemdateien oder nach Abstuerzen: verifyonly liest, scannow repariert.",
            "scannow ersetzt Systemdateien. Ergebnis im CBS.log pruefen; Prozessende ist kein Reparaturbeweis."),
        "Ereignisse" => ("Ursachen fuer Abstuerze, Hardwarefehler oder Dienstprobleme in den Windows-Protokollen finden.",
            "Nur lesend bis auf Export. Keine Treffer beweisen keine Fehlerfreiheit; Kanaele koennen Rechte erfordern."),
        "System" => ("Systemstand erfassen: Windows-Version, BIOS, Updates, Energiezustaende, Standby-Blocker.",
            "shutdown-Befehle starten das System neu; vorher speichern und mit shutdown /a abbrechen koennen."),
        "Prozesse / Dienste" => ("Haengende Prozesse identifizieren, Dienststatus pruefen, Autostarts und Aufgaben inventarisieren.",
            "Stop-Process verwirft ungespeicherte Daten; Restart-Service unterbricht Verbindungen. -WhatIf zuerst nutzen."),
        "Speicher / Treiber" => ("Datentraegergesundheit, Volumes, Treiberprobleme und NTFS-Zustand pruefen.",
            "chkdsk /scan ist die sichere Onlinepruefung. Vollstaendiges SMART im Modul SMART lesen."),
        "Netzwerk" => ("Verbindungsprobleme eingrenzen: IP-Konfiguration, DNS, Routen, offene Verbindungen, WLAN-Status.",
            "Test-/Resolve-Befehle kontaktieren externe Ziele; flushdns leert den lokalen Cache. Nur eigene Ziele pruefen."),
        "Sicherheit" => ("Schutzstatus pruefen: Defender, Firewall, BitLocker, Secure Boot, TPM, lokale Konten.",
            "Scans/Signaturupdates erzeugen Last und Netzwerkverkehr. Ein gruener Einzelwert ist kein Gesamturteil."),
        "Verwaltung" => ("Die originalen Windows-Verwaltungskonsolen direkt oeffnen.",
            "Aenderungen in den Konsolen erfolgen eigenverantwortlich und sofort; AETHER protokolliert sie nicht."),
        "Hilfe" => ("Den vollen lokalen PowerShell-Funktionsumfang entdecken: Befehle, Syntax, Module.",
            "Nur lesend. Get-Help zeigt ggf. nur Basishilfe, wenn keine Hilfedateien installiert sind."),
        _ => ("Befehl und Beschreibung lesen; bei Unsicherheit zuerst die -WhatIf-/Hilfe-Parameter nutzen.",
            "Platzhalter ersetzen; Enter fuehrt direkt aus.")
    };

    private static IReadOnlyList<AdminCommand> Build()
    {
        var items = new List<AdminCommand>();
        void Add(string group, string command, string description, string impact, params AdminParameter[] parameters) =>
            items.Add(new(group, command, description, impact, parameters));
        AdminParameter P(string text, string description) => new(text, description);
        const string read = "LESEND / kann sensible Systemdaten anzeigen";
        const string change = "AENDERND / Enter fuehrt ohne weitere Bestaetigung aus";
        const string network = "NETZWERK / kann externe Dienste kontaktieren";

        var package = new[] { P("--id 'PAKET-ID'", "Exakte Paket-ID einsetzen, z.B. aus winget search."),
            P("--exact", "Nur exakt passende ID/Name verwenden."),
            P("--source winget", "Quelle auswaehlen; mit winget source list pruefen."),
            P("--version 'VERSION'", "Bestimmte angebotene Version auswaehlen."),
            P("--scope machine", "Installation fuer alle Benutzer, falls vom Installer unterstuetzt."),
            P("--disable-interactivity", "Rueckfragen deaktivieren; fehlende Zustimmung wird als Fehler gemeldet."),
            P("--help", "Installierte Version erklaert alle unterstuetzten Optionen.") };
        Add("WinGet", "winget --info", "Version, Richtlinien, Verzeichnisse und Diagnoseinformationen.", read);
        foreach (var (cmd, desc, impact) in new[]
        {
            ("search 'SUCHBEGRIFF'", "Pakete in konfigurierten Quellen suchen.", network),
            ("show --id 'PAKET-ID' --exact", "Paketdetails, Installer und Version anzeigen.", network),
            ("list", "Installierte Pakete und verfuegbare Aktualisierungen anzeigen.", network),
            ("install --id 'PAKET-ID' --exact", "Ein Paket installieren. ID vor Ausfuehrung ersetzen.", change),
            ("upgrade", "Aktualisierbare Pakete anzeigen; installiert ohne Auswahl nichts.", network),
            ("upgrade --id 'PAKET-ID' --exact", "Ein ausgewaehltes Paket aktualisieren.", change),
            ("upgrade --all", "Alle angebotenen Paketupdates starten; laufende Apps vorher schliessen.", change),
            ("uninstall --id 'PAKET-ID' --exact", "Ausgewaehltes Paket deinstallieren.", change),
            ("source list", "Konfigurierte Paketquellen anzeigen.", read),
            ("source update", "Metadaten der Paketquellen aktualisieren.", network),
            ("export --output \"$env:USERPROFILE\\Desktop\\winget-packages.json\"", "Paketliste als JSON auf den Desktop schreiben.", change),
            ("import --import-file \"$env:USERPROFILE\\Desktop\\winget-packages.json\"", "Pakete aus einer zuvor geprueften JSON installieren.", change),
            ("pin list", "Gesperrte Paketaktualisierungen anzeigen.", read),
            ("settings", "WinGet-Konfiguration im zugeordneten Editor oeffnen.", change),
            ("repair --id 'PAKET-ID' --exact", "Installiertes Paket reparieren, sofern dessen Installer dies unterstuetzt.", change),
            ("download --id 'PAKET-ID' --exact --download-directory \"$env:USERPROFILE\\Downloads\"", "Paketinstaller herunterladen, nicht installieren.", network),
            ("hash 'C:\\PFAD\\installer.exe'", "SHA-256 einer lokalen Installerdatei berechnen; Pfad ersetzen.", read),
            ("validate --manifest 'C:\\PFAD\\manifest.yaml'", "Lokales WinGet-Paketmanifest validieren; Pfad ersetzen.", read),
            ("features", "Experimentelle Features und Verfuegbarkeit anzeigen.", read),
            ("source --help", "Alle lokalen Source-Unterbefehle, z.B. add/remove/reset/update.", read),
            ("pin --help", "Alle lokalen Pin-Unterbefehle und Optionen.", read),
            ("configure --help", "DSC-Konfiguration: lokale Hilfe fuer show/test/validate/apply; apply aendert das System.", read),
            ("dscv3 --help", "Versionsabhaengige DSC-v3-Ressourcenbefehle anzeigen.", read),
            ("mcp --help", "Versionsabhaengige MCP-Informationen; startet keinen Server.", read),
            ("--help", "Alle Befehle der lokal installierten WinGet-Version anzeigen.", read)
        }) Add("WinGet", "winget " + cmd, desc, impact, package);

        var dism = new[] { P("/Online", "Aktuell gestartete Windows-Installation bearbeiten."),
            P("/Cleanup-Image", "Komponentenspeicher analysieren oder reparieren."),
            P("/CheckHealth", "Bereits bekannte Beschaedigungen abfragen."),
            P("/ScanHealth", "Komponentenspeicher auf Beschaedigung untersuchen."),
            P("/RestoreHealth", "Beschaedigung reparieren; ggf. Windows Update nutzen."),
            P("/Source:'WIM:D:\\sources\\install.wim:1'", "Passende Reparaturquelle und Imageindex einsetzen."),
            P("/LimitAccess", "Windows Update nicht als Reparaturquelle verwenden."),
            P("/LogPath:'C:\\Temp\\dism.log'", "Vorhandenes Zielverzeichnis fuer Detailprotokoll verwenden."),
            P("/?", "Versionsabhaengige Optionen anzeigen.") };
        foreach (var (cmd, desc, impact) in new[]
        {
            ("/Online /Cleanup-Image /CheckHealth", "Bekannten Komponentenstatus lesen.", read),
            ("/Online /Cleanup-Image /ScanHealth", "Komponentenspeicher ausfuehrlich pruefen.", read),
            ("/Online /Cleanup-Image /RestoreHealth", "Windows-Komponentenspeicher reparieren.", change),
            ("/Online /Cleanup-Image /AnalyzeComponentStore", "Groesse und Bereinigungsbedarf analysieren.", read),
            ("/Online /Cleanup-Image /StartComponentCleanup", "Alte Komponenten bereinigen. Kann lange dauern.", change),
            ("/Online /Get-Packages /Format:Table", "Installierte Windows-Pakete anzeigen.", read),
            ("/Online /Get-Features /Format:Table", "Windows-Features und Status anzeigen.", read),
            ("/Online /Get-Drivers /Format:Table", "Drittanbieter-Treiber anzeigen.", read),
            ("/Online /Enable-Feature /FeatureName:'FEATURE-NAME' /All", "Ausgewaehltes Windows-Feature aktivieren; Neustart moeglich.", change),
            ("/Online /Disable-Feature /FeatureName:'FEATURE-NAME'", "Ausgewaehltes Windows-Feature deaktivieren.", change),
            ("/?", "Alle lokal verfuegbaren DISM-Befehlsgruppen anzeigen.", read)
        }) Add("DISM", "dism.exe " + cmd, desc, impact, dism);
        var sfc = new[] { P("/verifyonly", "Alle geschuetzten Systemdateien pruefen, nicht reparieren."),
            P("/scannow", "Geschuetzte Systemdateien pruefen und ggf. reparieren."),
            P("/verifyfile=C:\\Windows\\System32\\kernel32.dll", "Nur angegebene Datei pruefen."),
            P("/scanfile=C:\\Windows\\System32\\kernel32.dll", "Nur angegebene Datei pruefen und reparieren."),
            P("/offbootdir=D:\\", "Offline-Startpartition; Laufwerk korrekt einsetzen."),
            P("/offwindir=D:\\Windows", "Offline-Windows-Verzeichnis; nicht pauschal D: annehmen."),
            P("/?", "Alle SFC-Parameter der installierten Version anzeigen.") };
        Add("SFC", "sfc /verifyonly", "Systemdateien pruefen. Details: %WINDIR%\\Logs\\CBS\\CBS.log.", read, sfc);
        Add("SFC", "sfc /scannow", "Systemdateien pruefen/reparieren. Details im CBS.log beachten.", change, sfc);
        Add("SFC", "sfc /verifyfile=C:\\Windows\\System32\\kernel32.dll", "Eine einzelne geschuetzte Datei pruefen.", read, sfc);
        Add("SFC", "sfc /?", "Vollstaendige lokale SFC-Hilfe.", read, sfc);

        var events = new[] { P("-LogName System", "Ereigniskanal auswaehlen, z.B. System oder Application."),
            P("-MaxEvents 100", "Get-WinEvent: Anzahl begrenzen."),
            P("-Newest 100", "Get-EventLog: Anzahl begrenzen."),
            P("-EntryType Error,Warning", "Get-EventLog: auf Fehler und Warnungen beschraenken."),
            P("-After (Get-Date).AddDays(-1)", "Get-EventLog: Ereignisse seit gestern."),
            P("| Format-List *", "Alle Felder der gelieferten Objekte anzeigen."),
            P("| Export-Csv -Path \"$env:USERPROFILE\\Desktop\\events.csv\" -NoTypeInformation -Encoding UTF8", "Ergebnis exportieren; sensible Inhalte vorher pruefen.") };
        Add("Ereignisse", "Get-EventLog -LogName System -Newest 100 -EntryType Error,Warning", "Klassische Windows-Ereignislogs (Windows PowerShell 5.1).", read, events);
        Add("Ereignisse", "Get-EventLog -List", "Klassische Ereignislogs auflisten.", read, events);
        Add("Ereignisse", "Get-WinEvent -ListLog * | Select-Object LogName,RecordCount,IsEnabled", "Alle Ereigniskanaele auflisten; einzelne Kanaele koennen unzugreifbar sein.", read, events);
        Add("Ereignisse", "Get-WinEvent -FilterHashtable @{LogName='System'; Level=1,2,3; StartTime=(Get-Date).AddDays(-1)} -MaxEvents 100 | Format-List TimeCreated,Id,ProviderName,Message", "Kritische, Fehler- und Warnereignisse seit gestern.", read, events);
        Add("Ereignisse", "Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-WHEA-Logger'} -MaxEvents 50 | Format-List TimeCreated,Id,Message", "Hardwarefehler/WHEA lesen; keine Treffer sind kein Fehlerfreiheitbeweis.", read, events);
        Add("Ereignisse", "wevtutil.exe el", "Alle Kanaele ueber das Windows-Kommandozeilenwerkzeug auflisten.", read, P("/?", "wevtutil-Befehle erklaeren."));
        Add("Ereignisse", "wevtutil.exe epl System \"$env:USERPROFILE\\Desktop\\System.evtx\"", "System-Ereignislog als EVTX exportieren; bestehende Datei nicht ueberschreiben.", change);

        foreach (var (group, cmd, desc) in new[]
        {
            ("System", "Get-ComputerInfo", "Windows-, BIOS- und Systeminformationen."),
            ("System", "systeminfo.exe", "Systemkonfiguration und Updates."),
            ("System", "Get-CimInstance Win32_BIOS | Format-List *", "BIOS-/Firmwaredaten."),
            ("System", "Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,LastBootUpTime", "Windows-Version und letzter Start."),
            ("System", "Get-HotFix | Sort-Object InstalledOn -Descending", "Per WMI erfasste Updates; keine vollstaendige Windows-Update-Historie."),
            ("System", "powercfg.exe /a", "Verfuegbare Energiesparzustaende."),
            ("System", "powercfg.exe /requests", "Prozesse/Treiber, die Standby verhindern."),
            ("System", "powercfg.exe /lastwake", "Letzten Aufweckgrund anzeigen."),
            ("Prozesse / Dienste", "Get-Process | Sort-Object CPU -Descending | Select-Object -First 30 Name,Id,CPU,WorkingSet", "Prozesse nach kumulierter CPU-Zeit; kein aktueller CPU-Prozentwert."),
            ("Prozesse / Dienste", "Get-Service | Sort-Object Status,Name", "Dienststatus anzeigen."),
            ("Prozesse / Dienste", "Get-ScheduledTask | Select-Object TaskPath,TaskName,State", "Geplante Aufgaben und Status."),
            ("Prozesse / Dienste", "Get-CimInstance Win32_StartupCommand | Select-Object Name,Command,Location,User", "Autostartkommandos anzeigen."),
            ("Speicher / Treiber", "Get-Disk | Format-Table Number,FriendlyName,HealthStatus,OperationalStatus,Size", "Datentraegerstatus (kein vollstaendiger SMART-Bericht)."),
            ("Speicher / Treiber", "Get-Volume | Format-Table DriveLetter,FileSystemLabel,FileSystem,HealthStatus,SizeRemaining,Size", "Volumes und freier Platz."),
            ("Speicher / Treiber", "Get-PhysicalDisk | Get-StorageReliabilityCounter | Format-List *", "Vom Speichertreiber bereitgestellte Zuverlaessigkeitswerte."),
            ("Speicher / Treiber", "chkdsk.exe C: /scan", "NTFS-Onlinepruefung; System kann Onlinekorrekturen vornehmen."),
            ("Speicher / Treiber", "pnputil.exe /enum-drivers", "Treiberpakete im Driver Store."),
            ("Speicher / Treiber", "pnputil.exe /enum-devices /problem", "Geraete mit gemeldeten Problemen."),
            ("Speicher / Treiber", "driverquery.exe /v", "Geladene Treiber ausfuehrlich anzeigen."),
            ("Netzwerk", "Get-NetIPConfiguration -Detailed", "IP-, DNS- und Gatewaykonfiguration."),
            ("Netzwerk", "Get-NetAdapter | Format-Table Name,Status,LinkSpeed,MacAddress", "Netzwerkadapterstatus."),
            ("Netzwerk", "Get-NetTCPConnection | Sort-Object State | Format-Table LocalAddress,LocalPort,RemoteAddress,RemotePort,State,OwningProcess", "TCP-Verbindungen und Prozess-IDs."),
            ("Netzwerk", "Get-DnsClientCache", "Lokalen DNS-Cache lesen."),
            ("Netzwerk", "ipconfig.exe /all", "Vollstaendige IP-Konfiguration."),
            ("Netzwerk", "route.exe print", "Routingtabellen."),
            ("Netzwerk", "netstat.exe -ano", "Verbindungen/Listener und Prozess-IDs."),
            ("Netzwerk", "netsh.exe wlan show interfaces", "WLAN-Verbindungsstatus."),
            ("Sicherheit", "Get-MpComputerStatus | Format-List *", "Defender-Status; Verfuegbarkeit abhaengig vom Schutzprodukt."),
            ("Sicherheit", "Get-MpThreatDetection | Format-List *", "Lokale Defender-Erkennungen."),
            ("Sicherheit", "Get-NetFirewallProfile | Format-List Name,Enabled,DefaultInboundAction,DefaultOutboundAction", "Firewallprofile und Standardregeln."),
            ("Sicherheit", "Get-BitLockerVolume", "BitLocker-Volume-Status ohne Recovery-Schluessel."),
            ("Sicherheit", "Confirm-SecureBootUEFI", "Secure-Boot-Status; UEFI-Unterstuetzung erforderlich."),
            ("Sicherheit", "Get-Tpm", "TPM-Status."),
            ("Sicherheit", "Get-LocalUser | Select-Object Name,Enabled,LastLogon", "Lokale Benutzerkonten."),
            ("Sicherheit", "Get-LocalGroupMember -SID 'S-1-5-32-544'", "Lokale Administratorgruppe sprachunabhaengig lesen."),
            ("Verwaltung", "services.msc", "Windows-Diensteverwaltung oeffnen."),
            ("Verwaltung", "eventvwr.msc", "Windows-Ereignisanzeige oeffnen."),
            ("Verwaltung", "devmgmt.msc", "Geraetemanager oeffnen."),
            ("Verwaltung", "diskmgmt.msc", "Datentraegerverwaltung oeffnen; Aenderungen dort eigenverantwortlich."),
            ("Verwaltung", "taskschd.msc", "Aufgabenplanung oeffnen."),
            ("Verwaltung", "perfmon.exe /rel", "Zuverlaessigkeitsverlauf oeffnen."),
            ("Verwaltung", "resmon.exe", "Ressourcenmonitor oeffnen."),
            ("Hilfe", "Get-Command | Sort-Object CommandType,Name | Format-Table CommandType,Name,Source", "Alle lokal verfuegbaren PowerShell-Befehle entdecken."),
            ("Hilfe", "Get-Help Get-WinEvent -Full", "Lokale Funktions-/Parameterhilfe; ggf. nur Basishilfe installiert."),
            ("Hilfe", "Get-Command Get-WinEvent -Syntax", "Exakte lokal unterstuetzte Parametersaetze."),
            ("Hilfe", "Get-Module -ListAvailable | Select-Object Name,Version,Path", "Lokal verfuegbare PowerShell-Module.")
        }) Add(group, cmd, desc, read, P("| Format-List *", "Objekte mit allen Eigenschaften darstellen."),
            P("| Out-String -Width 240", "Breitere tabellarische Textausgabe."));
        Add("Prozesse / Dienste", "Restart-Service -Name 'DIENSTNAME'", "Exakten Dienstnamen einsetzen; Verbindungen/Apps koennen unterbrochen werden.", change,
            P("-Name 'DIENSTNAME'", "Dienstname aus Get-Service uebernehmen."), P("-WhatIf", "Geplante PowerShell-Aktion anzeigen statt ausfuehren."));
        Add("Prozesse / Dienste", "Stop-Process -Id 12345", "Nur zuvor gepruefte Prozess-ID einsetzen; ungespeicherte Daten gehen verloren.", change,
            P("-Id 12345", "Exakte aktuelle Prozess-ID aus Get-Process einsetzen."), P("-WhatIf", "Aktion nur anzeigen."));
        Add("Netzwerk", "Test-NetConnection -ComputerName 'HOSTNAME' -Port 443 -InformationLevel Detailed", "DNS/TCP-Verbindung zu eigenem/freigegebenem Ziel pruefen.", network,
            P("-ComputerName 'HOSTNAME'", "Freigegebenen Host einsetzen."), P("-Port 443", "Einen einzelnen Zielport pruefen."));
        Add("Netzwerk", "Resolve-DnsName 'HOSTNAME'", "DNS-Namensaufloesung testen.", network, P("-Server 'DNS-IP'", "Expliziten DNS-Server einsetzen."));
        Add("Netzwerk", "ipconfig.exe /flushdns", "Lokalen DNS-Resolvercache leeren.", change, P("/?", "Alle ipconfig-Optionen anzeigen."));
        Add("Sicherheit", "Update-MpSignature", "Defender-Signaturen aktualisieren.", network);
        Add("Sicherheit", "Start-MpScan -ScanType QuickScan", "Defender-Schnellscan starten; App-Abbruch stoppt nicht zwingend den Defender-Scan.", change,
            P("-ScanType FullScan", "Vollstaendiger Scan statt Schnellscan; kann lange dauern."));
        Add("System", "shutdown.exe /r /t 60", "Neustart in 60 Sekunden planen; vorher speichern. Mit shutdown /a abbrechen.", change,
            P("/r", "Neustarten."), P("/t 60", "Wartezeit in Sekunden."), P("/a", "Geplanten Neustart innerhalb der Wartezeit abbrechen."));
        Add("System", "shutdown.exe /a", "Geplanten Neustart/Shutdown abbrechen.", change);
        return items.AsReadOnly();
    }
}
