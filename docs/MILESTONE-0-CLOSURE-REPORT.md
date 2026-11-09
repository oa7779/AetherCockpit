# AETHER — Abschlussbericht Meilenstein 0 / Freigabe 0.5

## Status

**MEILENSTEIN 0 ABSCHLUSSBEREIT.**

Die Restlektüre und die getrennten Sicherungs-/Restoreprüfungen sind
abgeschlossen. Dies ist weder ein RC noch die Freigabe für Meilenstein 1.
Es wird vor einem Baseline-Commit gestoppt; seine Freigabe steht aus.

## 1. Branch und HEAD

`rc/grosse-entfesselung` auf
`53823883a9d1187395dc0fa3e3d8deebd3d3d8cb`.
Keine neuen ausschließlich lokalen Commits gegenüber den vorhandenen lokalen
Remotereferenzen; kein Stash, kein Staging, keine Löschung/Umbenennung.
Kein Fetch und keine Remoteänderung.

## 2. Neu inventarisierter Arbeitsbaum

Vor 0.5: 48 geänderte getrackte und 14 untracked Dateien, 316 Git-relevante
Dateien insgesamt. Physische Inventur ohne `.git`: 13.183 Dateien,
darunter 8.403 Releaseartefakte und 4.463 generierte Ausgaben. 8.720 Dateien
sind für den externen Restore ausgewählt. Die Restmenge ist nicht pauschal
„Backup“: bin/obj und andere generierte Ausgaben werden nur inventarisiert.

Die Einzeldateien stehen im [Hashmanifest](RC-BASELINE-HASHES.csv).
Die abschließende Kandidatenliste wurde nach der Dokumentation neu erzeugt:
48 geänderte getrackte und 18 untracked Dateien, zusammen 66 Kandidaten.
Die vier zusätzlichen Dateien sind ausschließlich die neuen 0.5-Dokumente.

## 3. Leseabdeckung

Siehe [Einzeldatei-Leseverzeichnis](RC-SOURCE-READING-MANIFEST.md).
Vorherige Lesebelege gelten nur bei unverändertem Datei-Hash seit dem Audit.
Neu vollständig gelesen: ControlCenter-XAML, ManagedActions, Packages und
OperationsHub-Codebehind sowie alle übrigen 65 UI-/Ressourcen-/Schonertexte.
Das aktuelle Verzeichnis umfasst 320 Dateien: 279 vollständig gelesen,
drei vollständig strukturiert geprüft, 38 Binärdateien; kein offener
Lesebeleg. Die vier selbst verfassten RESX-Dateien wurden einschließlich
aller Schlüssel und Werte semantisch gelesen, nicht als generierter Code
abgetan. Je Sprache bestehen 114 DesktopText- und 1.483 DesktopUiText-Einträge;
Schlüssel und Formatplatzhalter stimmen paarweise überein. Das ist kein
Nachweis einer vollständig lokalisierten Oberfläche.

## 4. Ungeprüfte Bereiche

Die Leseschranke für eigene relevante Textquellen ist geschlossen.
Binärressourcen, Toolprogramme, Archive, historische Releases und
generierte Ausgaben werden nicht als semantisch „vollständig gelesen“
bezeichnet. Produktionsprofile, echte Stores und Windows-Zertifikatsspeicher
liegen außerhalb der Workspaceprüfung.

Die externe `drivedb.h` ist keine generierte AETHER-Quelle. Ihre 792
Datensätze / 6.940 Zeilen wurden vollständig lexikalisch und strukturell
geprüft, Header und Lizenz gelesen. Jede einzelne Modellzuordnung fachlich
zu verifizieren bleibt ausdrücklich offen. Das generierte
Lokalisierungsmanifest und das erzeugte Hashmanifest wurden vollständig
strukturiert geprüft. Diese drei Sonderfälle sind im Verzeichnis markiert.

## 5. Neue Erkenntnisse

- Der OperationsHub hat eine eigene Ausführungspolitik: Wartungsschritte
  ohne erzwungenes Zeitlimit, sonst lokal 90 Minuten und remote drei Minuten.
  Anhalten während Wartung wartet den aktuellen Schritt ab; es ist kein
  Prozesskill und keine Rücknahme.
- Paketaufträge laufen seriell. Einzelfehler werden erfasst; weitere Pakete
  können weiterlaufen. Journalfehler/Abbruch verlassen den Lauf; der
  `finally`-Pfad kennzeichnet nicht ausgeführte/unvollständige Aufträge.
- Paketimport ist eine Wunschliste; Export schließt nicht aktionierbare oder
  quellenlose Einträge sichtbar aus. Externer Paketmanager benötigt Auswahl
  und Bestätigung; seine Aktionen sind keine AETHER-Queue.
- Die feste Updateinstallation verlangt frische Suche, exakte
  GUID/Revision/EULA-Hash und gesetzte Zustimmung; Auswahlwechsel entfernt
  die Zustimmung. Der Plan wird vor Ausführung verbraucht.
- Die zusätzliche Microsoft-Toolauswahl bietet in diesem UI Verfügbarkeits-
  prüfung und offizielle Bezugsseite. Daraus folgt keine eingebettete
  Sysinternals-Auswertung.
- Die Boarddarstellung bezeichnet Datenfluss/Lüfter als schematisch; der
  Schoner nutzt bei fehlendem Feed eine einzelne lokale Fallbacklesung.
  Diese Quellenunterscheidung ist keine neue oder verifizierte Livequelle.
- Bewegungsgates für Branding/Board/Schoner und Bestätigungen für
  Aufzeichnungen/Exporte sind im UI vorhanden. Ihre reale Lifecyclewirkung
  bleibt Gegenstand der späteren Abnahme.

Belege:
[OperationsHub](../src/SystemCockpit/Controls/OperationsHub.xaml.cs):55–64,174–248,273–299,441–480;
[Packages](../src/SystemCockpit/ControlCenterWindow.Packages.cs):14–145;
[ManagedActions](../src/SystemCockpit/ControlCenterWindow.ManagedActions.cs):61–142,187–219;
[Systemkontrolle](../src/SystemCockpit/ControlCenterWindow.xaml):89–477.
Weitere Belege:
[Board](../src/SystemCockpit/Controls/HardwareBoardView.Drawing.cs):214,344–346;
[Schonersitzung](../src/AetherScreensaver/SaverSession.cs):34–49,61–169;
[Aufzeichnung](../src/SystemCockpit/NetworkMonitorWindow.cs):125–147,229–264.

## 6. Korrigierte Audit-Aussagen

Die bisherigen Leselücken werden nicht rückwirkend als geschlossen dargestellt.
Ein datierter Nachtrag im [Audit](RC-READINESS-AUDIT.md) unterscheidet den
ursprünglichen Stand vom Ergebnis der Freigabe 0.5. Die pauschale
30-Minuten-Laufzeit gilt für den Shared-Runner, nicht für jeden Operations-
oder Konsolenpfad. „Fehler stoppt Folgeschritte“ gilt für Wartungspläne,
nicht unterschiedslos für die Paketqueue. M0-READ ist mit Freigabe 0.5
geschlossen; die Sicherungslücke aus RC-01 ist durch die externe,
hashgeprüfte Baseline mit Restoreprobe geschlossen. Eine Commitfreigabe
und ein späterer reproduzierbarer Neubuild werden dadurch nicht behauptet.

## 7. Sicherungsstatus

Strategie und Restore-Matrix: [Baseline-Sicherungsplan](RC-BASELINE-SNAPSHOT-PLAN.md).
Ziel: `D:\AetherBaselineSnapshots\rc05-20261109-212329`.
Die Kopie ist abgeschlossen: 8.720 Dateien / 4.823.007.346 Bytes wurden
anhand Größe und SHA256 geprüft, einschließlich aller 8.403 dist-Dateien.
Alle 317 Nicht-dist-Dateien wurden zusätzlich in ein getrenntes
Quell-Restore-Ziel rückkopiert und erneut geprüft. `git bundle verify`
und die HEAD-Objektprüfung im daraus wiederhergestellten Bare-Repository
waren erfolgreich. Der erste HEAD-Probeaufruf scheiterte an der
PowerShell-Argumentübergabe; nach Korrektur wurden die Kopien vollständig
erneut geprüft und die Restoreprobe erfolgreich beendet.
Die externe Sicherung liegt auf demselben Laufwerk; ein vollständiger
Datenträgerverlust ist dadurch nicht abgesichert.

## 8. Hashmanifeststatus

Das [Hashmanifest](RC-BASELINE-HASHES.csv) hält Ausgangspfad, Größe,
SHA256, Klassifikation, Gitstatus und Restoreauswahl fest. Dateien mit
sensibler Klassifikation würden ohne Inhalts-/Hashoffenlegung ausgeschlossen.
Selbstreferenz und die nachträglich erstellten Abschlussdokumente werden
über einen externen Dokumentationsnachtrag mit eigenem Manifest abgesichert.

## 9. Wiederherstellbarkeit

Git allein reicht für HEAD, nicht für den Dirty Tree oder untracked/dist.
Die externe Kopie hält den ausgewählten nicht sensiblen Entwicklungsbestand
einschließlich Releaseartefakten; das geprüfte Bundle hält den Historienbezug.
Die sechs Abschlussdokumente sind nach ihrer endgültigen Prüfung separat
unter `closure-supplement\payload` gesichert und rückkopiert; ein externes
Manifest hält ihre finalen Größen und SHA256 fest. Die ursprüngliche
Baseline wird dadurch nicht überschrieben. Zusammengenommen ergeben
Baseline und Dokumentationsnachtrag den Entwicklungsstand nach Freigabe 0.5.
`closure-supplement\payload\evidence\closure-current-files.csv` enthält
zusätzlich die abschließenden Hashes aller 320 Git-relevanten Dateien.
`supplement-result.json` hält das tatsächliche Ergebnis der getrennten
Nachtrags-Restoreprobe fest; ältere Auditberichte und die neuen
Lese-/Prüfbelege liegen daneben im Evidence-Verzeichnis.
Generierte Ausgaben müssen bei Bedarf neu gebaut werden; bytegleicher Neubuild,
produktiver Dienst-/DPAPI-/Zertifikatsrestore und Wiederherstellung laufender
Windows-Aktionen werden nicht behauptet.

## 10. Sensible Kategorien

Im Workspace wurden anhand der dokumentierten Dateinamen-/Mustersuche keine
entsprechenden Secret- oder Benutzerkonfigurationsdateien erkannt.
Das ist kein vollständiger Secret-/Historienaudit. Die Projektdateien für
Server-/Clientkonfiguration sind öffentliche Vorlagen und kein Nachweis echter
Zugangsdaten. Testtokens sind synthetisch; echte Tokens, private Schlüssel,
Kennwörter und persönliche Benutzer-/Serverdaten dürfen nicht in den
Baseline-Commit. Windowsprofile wurden nicht durchsucht.

## 11. Offene P0-Punkte

- Keine offene Lese-/Sicherungs-P0-Schranke für den Abschluss von 0.5.
- Die Baseline-Commitentscheidung steht aus; sie wird nicht durch diese
  Sicherung vorweggenommen und erteilt keine Umsetzungserlaubnis.
- Für den späteren RC: reale Lebenszyklus-/Hardware-/Ressourcenabnahme
  weiterhin offen; keine Performance- oder Sicherheitsfreigabe durch Lektüre.

## 12. Offene P1-Punkte

Dokumentationswidersprüche, Datenschutz-/Retention-/Bootstrapfragen,
verwaiste Jobs, Assetrechte und transitive Lizenznachweise bleiben sichtbar.
Der erhöhte lokale Power-User-Betrieb ist für RC1 als Planungsrichtung
akzeptiert; eine Helperarchitektur ist kein vorausgesetztes RC1-Projekt.
Historische defekte dist-Verweise in der Übergabe waren bereits in der
gesicherten Ausgangsfassung vorhanden. Sie werden als Altbefund getrennt
ausgewiesen, nicht durch erfundene Pakete oder eine Historienbereinigung
repariert.

## 13. Endgültige Aussage

**MEILENSTEIN 0 ABSCHLUSSBEREIT.** Die beauftragte statische Bestandsaufnahme,
die ausgewiesene Restlektüre und die externe Sicherungs-/Restoreprüfung
sind nachgewiesen. Die fachliche Einzelprüfung externer Laufwerkszuordnungen
und die reale RC-Abnahme bleiben ausdrücklich getrennte offene Aufgaben.
„ABSCHLUSSBEREIT“ ist weder RC-Freigabe noch Start von Meilenstein 1.

## 14. Begründung

Quelllektüre, Sicherung und Wiederherstellbarkeit sind getrennte Nachweise.
Keiner wird durch einen Build, Screenshot oder ein erfolgreiches Hashen
ersetzt. Offene Punkte bleiben konkret benannt.

| Abschlusskriterium 0.5 | Ergebnis |
|---|---|
| Aktueller Branch, HEAD, Dirty Tree und untracked neu erfasst | Erfüllt |
| Einzeldateiklassifikation, Ausschlüsse und Hashmanifest | Erfüllt; sensible Suchgrenzen dokumentiert |
| Sicherungsplan vor der Kopie und konkrete Restore-Matrix | Erfüllt |
| Tatsächliche externe Kopie und Wiederherstellungsprobe | Erfüllt; gleicher Datenträger bleibt ein Risiko |
| Relevante eigene Textquellen vollständig gelesen | Erfüllt; pro Datei belegt, Sonderfälle separat |
| Audit präzisiert, Benutzerentscheidungen nur vorgemerkt | Erfüllt |
| Exakte Commitkandidaten und Nicht-Commit-Bestand aufgeführt | Erfüllt |
| Produkt, Versionen, historische Ausgaben unverändert | Erfüllt; externe obj-Regeneration getrennt erfasst |
| Kein Staging, Commit, Push, Tag, Release oder Meilenstein-1-Start | Erfüllt |

## 15. Voraussetzungen für Meilenstein 1

Ausdrückliche Freigabe und abgegrenztes Arbeitspaket. Vorgemerkte Richtung:
RC1 Deutsch bei erhaltener Lokalisierungsarchitektur; ein sauberer Plan für
das sprachabhängige RC-Gate statt Abschalten/Umgehen. Englisch und nicht
tenantgeprüfte Graph-/Autopatch-Funktionen sollen RC1 nicht blockieren;
Graph bleibt vorhanden und für die Planung experimentell.

Erhöhtes Cockpit zunächst behalten. Ruhiger AetherNeonFrame erst mit
Meilenstein 2, übrige Animationen erhalten. Hardwarezentrale mit
Direct/Derived/Database/Unconfirmed und den vereinbarten Feldmetadaten.
Keine unbestätigten Herstellerlogos; neutrale eigene Symbole/Herstellertext.
Wiki zuerst eigenes Fenster innerhalb der Anwendung, offline mit Quellen,
Suche und Deeplinks. Historie vor späterer Changelogverdichtung sichern.
Keine dieser Richtungen wird in 0.5 implementiert.

### Abnahmegrenzen dieser Dokumentationsarbeit

Es wurden keine Produktbuilds, Tests, Installer, Installationen,
Verwaltungs-/Reparaturaktionen oder Tenantzugriffe ausgeführt. Die Prüfung
umfasst Dateihashes, Gitmetadaten, Sicherungs-/Restoreproben, strukturierte
Referenzdatenprüfung, Dokumentlinks und Whitespace.

Der Vergleich mit allen 13.183 Ausgangseinträgen zeigt außerhalb generierter
Ausgaben nur zwei geänderte vorhandene Dokumente: die
[Übergabe](../PROJEKTSTAND.md) und den [Auditbericht](RC-READINESS-AUDIT.md).
Hinzu kommen die vier neuen 0.5-Dokumente. Produktquellen
und sämtliche 8.403 dist-Artefakte sind unverändert. Zwei ignorierte
WPF-Zwischenausgaben unter `obj\Debug\net10.0-windows`
(`SystemCockpit_Content.g.cs`, `SystemCockpit_MarkupCompile.cache`) wurden
währenddessen außerhalb dieser Arbeit regeneriert; ihre Hashabweichung
wird getrennt protokolliert, nicht als unveränderter Bestand behauptet.
Sie werden weder committet noch in den Quellrestore aufgenommen.
Der abschließende Prüfbeleg liegt extern als `rc05-validation.json`.

<!-- RC05-CANDIDATES-START -->

## Exakte Commitentscheidungsvorlage

Aktueller Branch/HEAD unverändert. **48 geänderte getrackte und 18 untracked Dateien**. Jede folgende Datei ist ein vorgeschlagener Baseline-Commitkandidat, kein bereits genehmigter Commit. Der Baseline-Commit würde den gesamten aktuellen nicht sensiblen Quellstand einschließlich der 0.5-Dokumentation enthalten, nicht nur den Diff zum alten Funktionsstand. Keine Dateien sind staged.

### Geänderte getrackte Dateien

| Gitstatus | Datei |
|---|---|
| ` M` | [CHANGELOG.md](../CHANGELOG.md) |
| ` M` | [PROJEKTSTAND.md](../PROJEKTSTAND.md) |
| ` M` | [README.md](../README.md) |
| ` M` | [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) |
| ` M` | [build/AetherCockpit.iss](../build/AetherCockpit.iss) |
| ` M` | [docs/CENTRAL-MANAGEMENT.md](../docs/CENTRAL-MANAGEMENT.md) |
| ` M` | [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md) |
| ` M` | [docs/FEATURES.md](../docs/FEATURES.md) |
| ` M` | [docs/NETWORK.md](../docs/NETWORK.md) |
| ` M` | [docs/SECURITY-AND-PRIVACY.md](../docs/SECURITY-AND-PRIVACY.md) |
| ` M` | [docs/SYSTEM-CONTROL.md](../docs/SYSTEM-CONTROL.md) |
| ` M` | [docs/TESTING.md](../docs/TESTING.md) |
| ` M` | [docs/USER-GUIDE.md](../docs/USER-GUIDE.md) |
| ` M` | [src/AetherManager.Server/Accounts.http](../src/AetherManager.Server/Accounts.http) |
| ` M` | [src/AetherManager.Server/AetherManager.Server.csproj](../src/AetherManager.Server/AetherManager.Server.csproj) |
| ` M` | [src/AetherManager.Server/Middleware/GlobalExceptionHandler.cs](../src/AetherManager.Server/Middleware/GlobalExceptionHandler.cs) |
| ` M` | [src/AetherManager.Server/Program.cs](../src/AetherManager.Server/Program.cs) |
| ` M` | [src/AetherManager.Server/README.md](../src/AetherManager.Server/README.md) |
| ` M` | [src/AetherManager.Server/wwwroot/index.html](../src/AetherManager.Server/wwwroot/index.html) |
| ` M` | [src/AetherManager.Server/wwwroot/management.js](../src/AetherManager.Server/wwwroot/management.js) |
| ` M` | [src/AetherScreensaver/SaverWindow.cs](../src/AetherScreensaver/SaverWindow.cs) |
| ` M` | [src/SystemCockpit/CockpitModel.cs](../src/SystemCockpit/CockpitModel.cs) |
| ` M` | [src/SystemCockpit/ControlCenterWindow.xaml](../src/SystemCockpit/ControlCenterWindow.xaml) |
| ` M` | [src/SystemCockpit/ControlCenterWindow.xaml.cs](../src/SystemCockpit/ControlCenterWindow.xaml.cs) |
| ` M` | [src/SystemCockpit/Controls/DiagramTemplates.xaml](../src/SystemCockpit/Controls/DiagramTemplates.xaml) |
| ` M` | [src/SystemCockpit/Controls/FrameGlow.cs](../src/SystemCockpit/Controls/FrameGlow.cs) |
| ` M` | [src/SystemCockpit/Controls/HardwareBoardView.Drawing.cs](../src/SystemCockpit/Controls/HardwareBoardView.Drawing.cs) |
| ` M` | [src/SystemCockpit/Controls/HardwareBoardView.Landscape.cs](../src/SystemCockpit/Controls/HardwareBoardView.Landscape.cs) |
| ` M` | [src/SystemCockpit/Controls/HardwareBoardView.xaml.cs](../src/SystemCockpit/Controls/HardwareBoardView.xaml.cs) |
| ` M` | [src/SystemCockpit/Controls/NeonInputs.xaml](../src/SystemCockpit/Controls/NeonInputs.xaml) |
| ` M` | [src/SystemCockpit/DashboardModel.cs](../src/SystemCockpit/DashboardModel.cs) |
| ` M` | [src/SystemCockpit/MainWindow.Navigation.cs](../src/SystemCockpit/MainWindow.Navigation.cs) |
| ` M` | [src/SystemCockpit/MainWindow.xaml](../src/SystemCockpit/MainWindow.xaml) |
| ` M` | [src/SystemCockpit/Properties/PublishProfiles/PortableBeta.pubxml](../src/SystemCockpit/Properties/PublishProfiles/PortableBeta.pubxml) |
| ` M` | [src/SystemCockpit/Resources/DesktopUiText.en.resx](../src/SystemCockpit/Resources/DesktopUiText.en.resx) |
| ` M` | [src/SystemCockpit/Resources/DesktopUiText.resx](../src/SystemCockpit/Resources/DesktopUiText.resx) |
| ` M` | [src/SystemCockpit/Services/AdminCommandCatalog.cs](../src/SystemCockpit/Services/AdminCommandCatalog.cs) |
| ` M` | [tests/AetherManager.Tests/AccountApiAssertions.cs](../tests/AetherManager.Tests/AccountApiAssertions.cs) |
| ` M` | [tests/AetherManager.Tests/Program.cs](../tests/AetherManager.Tests/Program.cs) |
| ` M` | [tests/AetherManager.Tests/browser-smoke.mjs](../tests/AetherManager.Tests/browser-smoke.mjs) |
| ` M` | [tests/SystemCockpit.Tests/BootTests.cs](../tests/SystemCockpit.Tests/BootTests.cs) |
| ` M` | [tests/SystemCockpit.Tests/DashboardTests.cs](../tests/SystemCockpit.Tests/DashboardTests.cs) |
| ` M` | [tests/SystemCockpit.Tests/ExtendedTests.cs](../tests/SystemCockpit.Tests/ExtendedTests.cs) |
| ` M` | [tests/SystemCockpit.Tests/HardwareBoardTests.cs](../tests/SystemCockpit.Tests/HardwareBoardTests.cs) |
| ` M` | [tests/SystemCockpit.Tests/InterfaceTests.cs](../tests/SystemCockpit.Tests/InterfaceTests.cs) |
| ` M` | [tests/SystemCockpit.Tests/ModernDesktopTests.cs](../tests/SystemCockpit.Tests/ModernDesktopTests.cs) |
| ` M` | [tests/SystemCockpit.Tests/OperationsTests.cs](../tests/SystemCockpit.Tests/OperationsTests.cs) |
| ` M` | [tests/SystemCockpit.Tests/ScreensaverTests.cs](../tests/SystemCockpit.Tests/ScreensaverTests.cs) |

### Relevante untracked Dateien

| Gitstatus | Datei |
|---|---|
| `??` | [build/AetherManager.iss](../build/AetherManager.iss) |
| `??` | [build/Publish-ManagerSetup.ps1](../build/Publish-ManagerSetup.ps1) |
| `??` | [docs/MILESTONE-0-CLOSURE-REPORT.md](../docs/MILESTONE-0-CLOSURE-REPORT.md) |
| `??` | [docs/RC-BASELINE-HASHES.csv](../docs/RC-BASELINE-HASHES.csv) |
| `??` | [docs/RC-BASELINE-SNAPSHOT-PLAN.md](../docs/RC-BASELINE-SNAPSHOT-PLAN.md) |
| `??` | [docs/RC-READINESS-AUDIT.md](../docs/RC-READINESS-AUDIT.md) |
| `??` | [docs/RC-SOURCE-READING-MANIFEST.md](../docs/RC-SOURCE-READING-MANIFEST.md) |
| `??` | [docs/UX-INFORMATION-ARCHITECTURE-AUDIT.md](../docs/UX-INFORMATION-ARCHITECTURE-AUDIT.md) |
| `??` | [docs/UX-REDESIGN-PLAN.md](../docs/UX-REDESIGN-PLAN.md) |
| `??` | [src/AetherManager.Server/Endpoints/GraphEndpoints.cs](../src/AetherManager.Server/Endpoints/GraphEndpoints.cs) |
| `??` | [src/AetherManager.Server/Models/GraphContracts.cs](../src/AetherManager.Server/Models/GraphContracts.cs) |
| `??` | [src/AetherManager.Server/Services/MicrosoftGraphService.cs](../src/AetherManager.Server/Services/MicrosoftGraphService.cs) |
| `??` | [src/AetherManager.Server/Services/MicrosoftGraphTokenProvider.cs](../src/AetherManager.Server/Services/MicrosoftGraphTokenProvider.cs) |
| `??` | [src/SystemCockpit/Controls/AnimatedBrand.xaml](../src/SystemCockpit/Controls/AnimatedBrand.xaml) |
| `??` | [src/SystemCockpit/Controls/AnimatedBrand.xaml.cs](../src/SystemCockpit/Controls/AnimatedBrand.xaml.cs) |
| `??` | [src/SystemCockpit/Controls/HardwareBoardView.Glow.cs](../src/SystemCockpit/Controls/HardwareBoardView.Glow.cs) |
| `??` | [src/SystemCockpit/Services/WakeOnLan.cs](../src/SystemCockpit/Services/WakeOnLan.cs) |
| `??` | [tests/AetherManager.Tests/GraphApiAssertions.cs](../tests/AetherManager.Tests/GraphApiAssertions.cs) |

### Ausdrücklich nicht in den Commit

- Alle 8.403 ignorierten dist-Dateien: extern gesichert, historische Ausgaben erhalten.
- Alle 4.463 generierten bin/obj-/Testausgaben: inventarisiert, nicht als Quelle gesichert.
- `tools\intune\IntuneWinAppUtil.exe`: separat bereitgestelltes Microsoft-Binärwerkzeug, extern gesichert, nicht neu bündeln oder committen.
- Echte Benutzer-/Serverdaten, Tokens, private Zertifikatsschlüssel, Kennwörter, lokale Gitkonfiguration und Sicherungsordner: kein Bestandteil des Baseline-Commits.

**Entscheidung ausstehend:** Baseline-Commit ja/nein und gegebenenfalls Umfang. Bis dahin kein Commit, Push, Tag oder Release.

<!-- RC05-CANDIDATES-END -->
