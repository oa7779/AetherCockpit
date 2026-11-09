# UX-Redesign-Plan

## Status und Freigabegrenze

**Aktuelle Korrektur auf Nutzerauftrag:** Der Übersichtsumbau aus Paket 2 ist
zurückgenommen. Das Cockpit behält seine ursprüngliche Anzeigenanordnung ohne
neue Zusammenfassung oder zusätzliche Detailklappe. Navigation und neue
Wartungsfunktionen bleiben erhalten. Die folgenden Vorschläge dokumentieren
den früheren Plan und sind keine Freigabe für weitere Anzeigenänderungen.

Dieser Plan baut auf der Bestandsaufnahme in
[UX-INFORMATION-ARCHITECTURE-AUDIT.md](./UX-INFORMATION-ARCHITECTURE-AUDIT.md)
auf. Die Entscheidungen des Auftraggebers vom 9. November 2026:

- UX-Arbeit soll vor der noch ausstehenden Lokalisierung stattfinden.
- Der Expertenmodus bleibt für die Zielgruppe Power-User Standard.
- Das Cockpit selbst wird zur priorisierten Übersichtsseite umgebaut; es
  entsteht keine zusätzliche Startseite.
- Zentrale Verwaltung erhält einen eigenen, sichtbaren Navigationsbereich.
- Vor produktiven Änderungen sind überprüfbare UX-Proofs vorzulegen.

**Gate 0, Paket 1 und Paket 2 sind freigegeben und abgeschlossen. Paket 3
ist noch nicht freigegeben und wurde nicht begonnen.** Der Proof ist ein separat
ausführbares HTML-Mockup mit ausschließlich
gekennzeichneten Beispieldaten. Er ist kein Bestandteil der Anwendung und
führt keine Systemaktion aus.

## 1. Festgestellte Hauptprobleme

1. Das Cockpit präsentiert viele Live-Panels und Kacheln gleichrangig, ohne
   eine klare Antwort auf „Gibt es etwas, worauf ich achten sollte?“
2. Hauptfenster und Systemkontrolle teilen Aufgaben ohne sichtbares,
   vorhersehbares Zuordnungsprinzip. Ähnliche Navigationsbegriffe öffnen
   unterschiedliche Fenster und Tabs.
3. Einstellungen, Desktopfunktionen und zentrale Verwaltung sind auf
   mehrere Einstiegspunkte verteilt.
4. Technische oder englische Bezeichnungen erklären den Zweck nicht immer.
5. In mehreren Bereichen stehen Anzeige und Systemänderung nebeneinander;
   Admin- und Irreversibilitätsmerkmale sind nicht einheitlich sichtbar.
6. Der Expertenmodus ist bereits Standard. Das bleibt für die Power-User-
   Zielgruppe unverändert; Übersichtlichkeit darf keine implizite
   Sicherheitsgrenze oder Funktionsbeschränkung schaffen.

## 2. Vorgeschlagene Informationsarchitektur

Die vom Auftraggeber bestätigte Struktur ordnet Funktionen nach Aufgaben:

1. **Überblick** — Cockpit in priorisierter Form; aktueller Systemzustand,
   konkrete Auffälligkeiten, begrenzte Live-Kernwerte, Schnellzugriffe und
   prominente Funktionensuche.
2. **Beobachten** — Prozesse, Sensoren, Mainframe/Board und Laufwerke.
3. **Analysieren** — Messauswertung, SMART, Logs/Gaming, Verläufe und Berichte.
4. **Diagnostizieren** — Windows-Diagnose, Schutzstatus, Ereignisse,
   Netzwerkdiagnose und geführte Problemsuche.
5. **Verwalten** — Systemverwaltung, Autostart, Pakete und Aufträge.
6. **Warten & reparieren** — Wartungspläne, Cleaner und Windows-Werkzeuge.
7. **Desktop** — xBar, Gadgets, Temperatur-Overlay, Bildschirmschoner und
   Darstellung.
8. **Zentrale Verwaltung** — Enrollment, Geräte-/Verbindungsstatus,
   Berichte und Übertragungsfreigaben; klar als optionaler Managed-Modus
   beschrieben.
9. **Einstellungen** — Verhalten, Warnschwellen, Datenschutz, Design,
   Berechtigungs- und Expertenoptionen.
10. **Hilfe** — bestehendes lokales Hilfe-Center und Suche.

Das Cockpit wird selbst umgebaut, nicht durch einen zusätzlichen
Übersichtsbereich ergänzt. Bestehende Funktionen bleiben erreichbar.
Hauptfenster und Systemkontrolle werden für die ersten Pakete nicht
technisch zusammengelegt; die Navigationszuordnung und das Öffnungsverhalten
werden verständlicher gemacht. Der Expertenmodus bleibt Standard und wird
nicht als Schutzgrenze dargestellt.

## 3. Navigationsbaum vorher und nachher

**Vorher (vereinfacht):**

```text
Hauptfenster
├─ Übersicht: Cockpit, Prozesse
├─ Hardware: Board, Sensoren, Laufwerke, ↗ SMART
├─ Diagnose: Windows-Diagnose, Schutzstatus, Ereignisse, ↗ Netzwerk,
│            Messauswertung, ↗ Logs/Gaming
├─ Wartung: Cleaner, Pläne, ↗ Windows-Werkzeuge, ↗ Pakete
├─ Verwaltung: Systemverwaltung, ↗ Autostart, ↗ Aufträge,
│              ↗ Ordnerspuren, ↗ Schlüssel
└─ Hilfe: Anleitung/Suche, ↗ Parameter/Server

Systemkontrolle (separates Fenster): 10 Tabs, darunter Wartung,
Einstellungen und Management Reports.
```

**Nachher (Zielbild):**

```text
Hauptfenster
├─ Überblick (umgebautes Cockpit)
├─ Beobachten
├─ Analysieren
├─ Diagnostizieren
├─ Verwalten
├─ Warten & reparieren
├─ Desktop
├─ Zentrale Verwaltung
├─ Einstellungen
└─ Hilfe

Details und bestehende Systemkontrollmodule bleiben erreichbar.
Navigation in externe Module wird als bewusster Fenster-/Bereichswechsel
benannt; der sichtbare Einstieg entspricht möglichst dem Zielmodul.
```

## 4. Betroffene Dateien und Komponenten

### Erwartete Produktionsdateien (erst nach Freigabe)

- `src/SystemCockpit/MainWindow.xaml`
- `src/SystemCockpit/MainWindow.Navigation.cs`
- `src/SystemCockpit/MainWindow.xaml.cs`
- `src/SystemCockpit/ControlCenterWindow.xaml`
- `src/SystemCockpit/ControlCenterWindow.xaml.cs`
- `src/SystemCockpit/DesktopSettingsWindow.cs`
- `src/SystemCockpit/Services/CockpitSettings.cs` — nur falls ein neuer
  Darstellungszustand für die Übersicht wirklich benötigt wird; keine
  Veränderung des Expertenmodus-Standards ohne neue Freigabe
- Ressourcen und lokalisierbare Texte unter
  `src/SystemCockpit/Resources/`
- zielgerichtete Tests im bestehenden STA-Konsolenrunner unter `tests/`
- betroffene Bedienhinweise in `docs/USER-GUIDE.md`,
  `docs/FEATURES.md` und gegebenenfalls
  `docs/CENTRAL-MANAGEMENT.md`

Welche Dateien tatsächlich geändert werden, wird vor jedem Paket anhand des
Quellstands ermittelt; diese Liste ist keine Behauptung, dass alle Dateien
zwingend betroffen sein müssen.

### Vorab-Proof

- Eigenständiges Mockup außerhalb des produktiven Quellbaums.
- Es enthält keine echten Sensorwerte, keine Backend-Verbindung und keine
  ausführbaren Systemaktionen.
- Der Proof zeigt Cockpit/Überblick, Aufgaben-Navigation, Desktop,
  Zentralverwaltung und die semantische Trennung der Aktionsrisiken.

## 5. Pakete, Reihenfolge und Abnahmepunkte

### Gate 0 — Vorab-Proof (jetzt)

- Klickbares Mockup und dieser Plan vorlegen.
- Interaktionspfade prüfen: Cockpit → Beobachten/Diagnostizieren,
  Funktion finden, Desktop, Zentralverwaltung.
- Anzeige, lokale Einstellung, Systemänderung und administrative/
  kritische Aktion unterscheidbar darstellen.
- **Gate bestanden:** Mockup und Plan wurden ausdrücklich freigegeben.
- Vor jedem weiteren Paket bleibt eine separate Freigabe erforderlich.

### Paket 1 — Benennungen und Orientierung

- Navigationsbezeichnungen, Titel und kurze Erklärungen angleichen.
- Eintrag und Zielmodul der Systemkontrolle verständlich und konsistent
  benennen.
- Bestehende Funktionen, Routen, Sicherheitsabfragen und Expertenmodus
  unverändert lassen.
- Abnahme: alle bisherigen Routen erreichbar; Text-/Navigationstests
  bestehen; geänderte Doku stimmt mit der UI überein.

### Paket 2 — Cockpit als Überblick

- Bestehendes Cockpit priorisieren, keine neue Startseite hinzufügen.
- Belegbaren Systemzustand und konkrete Befunde hervorheben.
- Kernwerte bewusst begrenzen; Detailwerte über bestehende Wege erreichbar
  lassen.
- Schnellzugriffe und Funktionensuche sichtbar, aber ohne erfundene
  Health-Scores oder Messwerte.
- Abnahme: synthetische Zustände für verfügbar, nicht verfügbar, Hinweis und
  Warnung; keine Funktion wird entfernt; kleine Fenster/DPI prüfen.

#### Prüfbericht Paket 2

Das Cockpit beginnt nun mit vorhandener Windows-Prüfzusammenfassung,
Abfragestatus, gemeldeten Befunden und vier Kernwerten (CPU, RAM, GPU und
Download). Funktionensuche und drei Schnellzugriffe öffnen bestehende Bereiche.
CPU-Kerne, Sensoren, Datenträger, Prozesse, Ereignisse und
Datenerfassungssteuerung bleiben unter einem anfangs geschlossenen
Detailbereich erreichbar. Es wurden weder Sensor-/Diagnoselogik noch
Systemaktionen geändert; nicht verfügbare Werte erhalten keinen Ersatzwert.

Prüfungen auf dem aktuellen Arbeitsstand:

- Releasebuild von `SystemCockpit.csproj`: 0 Warnungen, 0 Fehler.
- `--interface-ui`: 4148 STA-Assertions bestanden; einschließlich
  verfügbarer, nicht verfügbarer, informativer und warnender Befunde,
  Schnellzugriff, Detailzugriff und Mindestfensterbreite ohne horizontalen
  Überlauf.
- `--desktop-ui-smoke`: 3605 Assertions für Windows Modern hell/dunkel und
  Neon-Kompatibilität bestanden. Summary und Details wurden in synthetischen
  Screenshots kontrolliert.
- Lokalisierungs-Audit: 3 Unit-Tests bestanden, keine fehlenden expliziten
  Ressourcen. Die englische Produktoberfläche bleibt weiterhin gesperrt.
- `git diff --check` ohne Befund.

Keine Hardwareabfrage, Reparatur, Installation oder Änderung an
Produktionsdaten wurde ausgeführt. Paket 3 bleibt bis zu einer separaten
Freigabe unangetastet.

### Paket 3 — Aufgabenorientierte Navigation

- Sichtbare Gruppen nach bestätigtem Navigationsbaum umordnen.
- Eigener sichtbarer Bereich „Zentrale Verwaltung“.
- Bestehendes Zwei-Fenster-Modell zunächst erhalten und Wechsel verständlich
  kennzeichnen.
- Abnahme: vollständiger Vorher/Nachher-Routenabgleich und automatisierte
  Navigationstests.

### Paket 4 — Progressive Offenlegung

- Seltene Details einklappen oder in klar bezeichnete Detailbereiche legen.
- Expertenmodus bleibt Standard und bleibt Darstellung/Bedienkomfort,
  nicht Berechtigungsgrenze.
- Vollständige Erreichbarkeit aller Sensoren, Diagnose- und Spezialwerte
  nachweisen.
- Abnahme: Auf-/Zuklappen per Maus und Tastatur; unveränderte Bestätigungen.

### Paket 5 — Visuelle Hierarchie

- Bestehendes Theme-/Skin-System nutzen; keine neue hartcodierte
  Oberflächenfarbe hinzufügen.
- Semantische Farbrollen pro Skin prüfen; Neonfarben als Markenakzent
  erhalten.
- Tag/Nacht, Retro-Neon, vorhandene Skins und Windows Modern abdecken.
- Abnahme: synthetische Screenshots in relevanten Themes sowie Kontrast-
  und Zustandsprüfung.

### Paket 6 — Barrierefreiheit und Layout

- Fokusreihenfolge, Tastaturbedienung, aktive Zustände, Skalierung,
  Bildschirmgrößen und reduzierte Bewegung prüfen.
- Abnahme: Tastaturabläufe für Navigation, Suche, Details und Aktionen;
  Tests bei kleinem Layout und hoher DPI.

### Paket 7 — Dokumentation und Abschluss

- Benutzerhandbuch, Funktionsübersicht, betroffene Fachdokumente,
  Beta-Testanleitung und Changelog synchronisieren.
- Screenshots nur aus dem tatsächlich implementierten Stand erzeugen.
- Abnahmebericht mit geänderten Dateien, Tests und verbleibenden Risiken.

Nach jedem freigegebenen Paket wird angehalten und ein Prüfbericht erstellt.
Das nächste Paket beginnt erst nach separater Freigabe.

## 6. Risiken und Regressionen

| Risiko | Gegenmaßnahme |
|---|---|
| Aktuell geteilte Fenster/Module werden bei neuer Navigation übersehen | Vor jedem Paket vollständiges Routeninventar und Funktion-zu-Ziel-Abgleich |
| MainWindow und Systemkontrolle verhalten sich unterschiedlich | Beide Fenster bei Navigation, Suche, Zurück/Vor und externen Routen testen |
| Cockpit-Reduktion wirkt wie Funktionsverlust | Details bleiben erreichbar; automatisierter Abgleich der Messkanäle und Seiten |
| Darstellung legt versehentlich Berechtigungen nahe | Expertenmodus bleibt unverändert Standard; Sicherheitsprüfungen liegen weiter in der Aktionslogik |
| Warnfarben oder vereinfachte Zustände suggerieren Messwerte | Nur belastbare Befunde zeigen; „nicht verfügbar“ bleibt eigener Zustand |
| Skin-/Theme-Abweichungen | Ressourcen des bestehenden Themesystems verwenden und mehrere Skins visuell prüfen |
| Lokalisierungsarbeit wird doppelt geleistet | UX-Textänderungen vor der noch ausstehenden Lokalisierung bündeln und Ressourcen vollständig behandeln |
| Uncommittete Änderungen werden beeinträchtigt | Vor jedem Paket Git-Status/Branch prüfen, keine fremden Änderungen überschreiben und keine Commits ohne Auftrag |
| Zentralverwaltung wird mit lokaler Verwaltung verwechselt | Eigener Bereich, verständliche Managed-/Standalone-Erklärung, Einwilligung und Status klar getrennt |

### Besonderer Reihenfolgekonflikt

Der Nutzer wünscht UX vor der Lokalisierung. Der aktuelle Branch heißt jedoch
`test/rc-localization`, enthält bereits Lokalisierungsarbeit und der
Arbeitsbaum ist uncommitted. Diese vorhandenen Änderungen werden nicht
zurückgesetzt oder überschrieben. Vor Paket 1 ist deshalb der tatsächliche
Umfang der noch ausstehenden Lokalisierung mit dem Stand des Branches
abzugleichen. Dieser Konflikt ändert nichts an der Freigabegrenze: bis zur
Proof-Freigabe bleibt der Produktcode unverändert.

## 7. Teststrategie

Vor und nach jedem Paket werden die vorhandenen passenden Prüfungen verwendet;
keine Tests werden entfernt oder abgeschwächt.

- Vor Paket 1: Branch und Arbeitsbaum sichern dokumentieren; betroffene
  bestehende Tests finden und auf dem aktuellen Stand ausführen, soweit die
  lokale Windows-Ausführungsrichtlinie dies zulässt.
- Navigation und Ressourcen: gezielte STA-Assertions für Gruppen,
  Routenbeschriftungen, externe Ziele und Suchzugänge.
- Cockpit: synthetische Daten für Normalzustand, konkrete Auffälligkeit,
  fehlende/nicht verfügbare Werte und leere Zustände; bestehende UI-
  Assertions und Screenshots ergänzen.
- Aktionsdarstellung: Anzeige-/Änderungs-/Admin-/kritisch-Marker testen,
  ohne Aktionslogik auszuführen.
- Themes: Retro-Neon Tag/Nacht, vorhandene Skins, Windows Modern hell/dunkel.
- Accessibility: Tastatur/Fokus, reduzierte Bewegung, schmale Fenster und
  hohe DPI.
- Reale Reparaturen, Updates, Netzwerkänderungen, Dienst-/Clientinstallation
  oder Remote-Aufträge sind keine Routinevalidierung und werden nicht
  ausgeführt.

Konkrete Kommandos werden vor Paket 1 anhand von
`docs/DEVELOPMENT.md` und `docs/TESTING.md` festgelegt. Der STA-Konsolenrunner
ist kein `dotnet test`-Projekt.

## 8. Rückfallstrategie

- Pro Paket begrenzte, nachvollziehbare Änderungen; keine Commits ohne
  ausdrücklichen Auftrag.
- Bestehende Nutzerpräferenzen und serialisierte Schlüssel beibehalten;
  keine Einstellungsmigration ohne eigenen Test und nachgewiesenen Bedarf.
- Falls ein Paket einen unerwarteten Funktions-, Skin- oder
  Navigationseinbruch erzeugt, nur die eigenen Änderungen dieses Pakets
  zurücknehmen, fremde bestehende Änderungen unangetastet lassen und zum
  zuletzt bestandenen Teststand zurückkehren.
- Keine Snapshots allein zum Beseitigen eines Fehlers aktualisieren.
- Ein späterer Rückbau der Navigation muss alle alten Routen als
  erreichbare Ziele behalten, bis Ersatz nachgewiesen ist.

## 9. Reihenfolge

1. Gate 0: Mockup und Plan prüfen lassen.
2. Vor Paket 1: Lokalisierungsreihenfolge/aktueller Arbeitsbaum abgleichen;
   bestehende Tests dokumentieren und ausführen.
3. Paket 1: Benennungen und Orientierung.
4. Paket 2: Cockpit/Überblick.
5. Paket 3: Navigation einschließlich sichtbarer Zentralverwaltung.
6. Paket 4: Progressive Offenlegung.
7. Paket 5: visuelle Hierarchie.
8. Paket 6: Accessibility und Layout.
9. Paket 7: Dokumentation und Abschluss.

## 10. Abgrenzung der Änderungstypen

| Typ | Geplanter Umfang |
|---|---|
| Reine UI-Änderung | Gruppierung, Gewichtung, Beschreibungen, Zustandsdarstellung, Layout und Details |
| Navigationsänderung | Sichtbare Aufgabengruppen und Zielzuordnung; bestehende Routen und Module bleiben erreichbar |
| ViewModel-/Datenänderung | Nur wenn die priorisierte Übersicht einen bereits vorhandenen, belegbaren Datenzustand nicht anders darstellen kann; vorab separat begründen |
| Technische Logik | Nicht Teil des UX-Ziels; Sensorabfrage, Diagnose, Aktionen, Rechte, Bestätigungen, Server/Client-Protokoll bleiben unverändert |

## 11. Funktionen, deren Erreichbarkeit erhalten bleiben muss

- Cockpit-Kernwerte, alle Prozessdetails, vollständige Sensor-Matrix,
  Warnschwellen, Laufwerksbelegung, SMART und Mainframe-Gerätedetails
- Messaufzeichnung, Statistik, Momentaufnahmen, Logvergleich,
  PresentMon/Framezeiten und Berichte
- Windows-Diagnose, Schutzstatus, Ereignisse, Geräte/Treiber,
  Systemdatei-/Update-/Netzwerkassistenten, Diagnoseexport und
  Hintergrunddiagnose
- Prozesse, Dienste, Aufgaben, Features, Energie, Netzwerkadapter/DNS/Proxy,
  Firewall, Benutzer/Gruppen, ACL, Autostart und Registry-Einstellungen
- WinGet/Pakete, Wartungspläne, Cleaner-Dateivorschau, Wiederherstellungspunkt,
  feste Windows-Werkzeuge, Befehlskatalog und freie Administratorkonsole
- xBar, alle Gadgettypen, Tray, Temperatur-Overlay, Bildschirmschoner,
  Multi-Monitor-Verhalten, Skins, Tag/Nacht und Windows Modern
- Zentralverwaltung, Enrollment, Einwilligung, Clientdienst,
  Übertragungsfreigaben, Serverberichte und Remote-Aufträge
- Hilfe, Funktionssuche, Suchsynonyme, Zurück/Vor und Tastaturzugänge

## 12. Akzeptanzkriterien

- Keine vorhandene Funktion oder Route geht verloren.
- Das Cockpit selbst wird zu einer priorisierten Übersicht; keine zweite
  Startseite entsteht.
- Power-User behalten den Expertenmodus als Standard und alle bisherigen
  Detailwerkzeuge.
- Eine konkrete Auffälligkeit ist von „keine Auffälligkeit gemeldet“ und
  „nicht verfügbar“ unterscheidbar; keine synthetischen Health-Scores.
- Anzeige, lokale Einstellung, Systemänderung und Admin-/kritische Aktion
  sind visuell unterscheidbar; bestehende Bestätigungen bleiben erhalten.
- Zentralverwaltung hat einen eigenen sichtbaren Einstieg und erklärt
  Standalone/Managed sowie Status und Einwilligung verständlich.
- Retro-Neon/Synthwave, Gadgets, xBar und alle vorhandenen Designs bleiben
  funktional.
- Tastatur, Fokus, DPI, kleine Fenster und reduzierte Bewegung werden nicht
  verschlechtert.
- Build und passende Tests bestehen; Dokumentation beschreibt den echten
  Stand.
- Jedes Paket hält nach dem Prüfbericht an und wartet auf Freigabe.

## Prüfbericht — Paket 1: Benennungen und Orientierung

**Status:** abgeschlossen, 9. November 2026. Nur Paket 1 wurde freigegeben.

- Sichtbare Navigations- und Zielmodulbezeichnungen präzisiert, unter anderem
  Cache-Bereinigung, SMART-Diagnose, Messlogs & Framezeiten,
  Windows-Werkzeuge, Explorer-Verlauf, Autostart-Programme und Auftragsstatus.
- Interne Routen und Tag-IDs unverändert gelassen. UI-Tests prüfen die
  stabile Routenzuordnung über Tags statt über sichtbare Tab-Titel.
- Nutzerhandbuch, Funktionsübersicht, Wartungsdokumentation und README
  nachgeführt. Das Audit bleibt ausdrücklich als Baseline gekennzeichnet.
- Die parallelen vorhandenen englischen Ressourcen wurden nur für die
  geänderten Bezeichnungen synchronisiert; das ist keine Freigabe der
  englischen Produktoberfläche oder Abschluss der Lokalisierung.
- Keine Sensor-, Diagnose-, Verwaltungs-, Bestätigungs-, Server- oder
  Clientlogik geändert. Der Expertenmodus bleibt unverändert Standard.

Geänderte Dateien dieses Pakets:

- `src/SystemCockpit/MainWindow.Navigation.cs`
- `src/SystemCockpit/ControlCenterWindow.xaml`
- `src/SystemCockpit/Resources/DesktopUiText.resx`
- `src/SystemCockpit/Resources/DesktopUiText.en.resx`
- `tests/SystemCockpit.Tests/InterfaceTests.cs`
- `tests/SystemCockpit.Tests/ExtendedTests.cs`
- `README.md`
- `docs/FEATURES.md`
- `docs/USER-GUIDE.md`
- `docs/SYSTEM-CONTROL.md`
- `docs/UX-INFORMATION-ARCHITECTURE-AUDIT.md`

Prüfungen:

| Prüfung | Ergebnis |
|---|---|
| Baseline: `dotnet run --project .\tests\SystemCockpit.Tests -c Release -- --interface-ui --desktop-language-smoke` | Bestanden: 3.227 Assertions |
| Nach Paket 1: gleicher STA-Testlauf | Bestanden: 3.237 Assertions |
| `python -m unittest discover -s .\tests -p test_desktop_localization_audit.py` | Bestanden: 3 Tests |
| `dotnet build .\SystemCockpit.slnx -c Release --no-restore` | Bestanden: 0 Warnungen, 0 Fehler |
| `git diff --check` für Paketdateien | Bestanden |

Verbleibendes Risiko: Die aufgabengruppierte Hauptnavigation und die
Cockpit-Priorisierung selbst sind noch unverändert; die Sichtbarkeit der
Zentralverwaltung als eigener Hauptbereich gehört zu Paket 3. Diese Punkte
werden nicht stillschweigend mit Paket 1 umgesetzt.
