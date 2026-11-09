# AETHER — Meilenstein 1: technische Konsolidierung

## Status

**IN BEARBEITUNG — Fortsetzung ausdrücklich freigegeben.**

Die nachfolgend dokumentierte Ausgangsabweichung bleibt ursächlich
ungeklärt und wird als **BEOBACHTETE INTERMITTIERENDE AUSGANGSABWEICHUNG –
URSACHE NICHT BELEGT** weitergeführt. Die Fortsetzungsfreigabe erlaubt
die Konsolidierung ohne weitere Dauerdiagnose. Nur ein reproduzierbarer
oder wiederholt auftretender kritischer Ausgangsfehler löst erneut
einen Stopp aus; neue ungeklärte Regressionen bleiben Stoppgründe.

### Aktueller Umsetzungsnachweis

Arbeitspaket Quellen-/Ergebnis-/Datenschutzgrundlage:

- Ergänzende Shared-Verträge `SourceMetadata`, `SourceObservation<T>`,
  `OperationOutcome`, Datenschutzklassen und explizite Freigabepolitik.
- `HasValue` trennt fehlende Information von gemessener Null. Zustände
  Available, Unavailable, AccessDenied, Unsupported, ProviderError, Stale
  und Unknown bleiben getrennt. Stale behält den Originalzeitpunkt.
- Herkunft Direct/Derived/Database/Unconfirmed und Vertrauen sind getrennt;
  Unconfirmed kann keine bestätigte Confidence behaupten.
- Kompatible Adapter für Sensor- und Prozessresultate, keine Änderung an
  Sensor-, Pipe-, History-, Export- oder Verwaltungs-JSON-Verträgen.
  Neue Prozess-Trunkierungsinformation ist ausdrücklich `JsonIgnore`.
- Secret wird über den neuen Freigabehelfer auch mit Zustimmung abgewiesen;
  unbekannte Felder gelten zunächst als Sensitive. Noch keine pauschale
  Migration bestehender Exportformate.
- Release-Gesamtbuild: null Warnungen/Fehler; STA-Grundrunner:
  4.183 Assertions (55 neue Vertragsassertions); Verwaltungsrunner:
  384 Assertions bestanden.

Die übrigen Abschnitte halten bis zur abschließenden Zusammenführung
den historischen Start-/Diagnosestand fest.

Arbeitspaket Prozessresultate, Cancellation und Ressourcen:

- Ausgabegrenze exakt geprüft: 2.000.000 Zeichen sind vollständig,
  2.000.001 Zeichen werden explizit als gekürzt/Partial gekennzeichnet.
  Vorher konnte das letzte überschriebene Zeichen ohne Kennzeichnung
  verloren gehen. stdout und stderr werden weiterhin bis zum Ende gelesen.
- Bereits angeforderter Abbruch und ungültiger Timeout werden vor dem
  Prozessstart geprüft. Fehler im Start-Progress liegen jetzt innerhalb
  der Prozessbereinigung. Abbruch/Timeout warten Prozessende und
  Streamtasks ab; kein Rollbackversprechen.
- WindowsIdentity und Process beim Ermitteln des Board-Pipenamens werden
  deterministisch freigegeben, ebenso der aktuelle Processwrapper vor
  der bestätigten Exploreraktion. Pipename und Aktion bleiben gleich.
- Release-Gesamtbuild 0/0; 4.202 STA-Assertions bestanden, einschließlich
  19 neuer isolierter Prozess-/Grenzprüfungen. Kein Windows-Verwaltungsbefehl
  wurde für diese Tests gestartet.

Stand: 9. November 2026. Meilenstein 0 und Freigabe 0.5 wurden vom
Auftraggeber abgenommen. Der Baseline-Commit wurde erstellt. Der erste
Verwaltungstestlauf zeigte einen ungeklärten Dateizugriffsfehler; der
unveränderte Wiederholungslauf bestand. Diese Abweichung wird nicht durch
den erfolgreichen Wiederholungslauf als geklärt behandelt.

Die technische Konsolidierung ist noch nicht abnahmebereit. Die weiteren
Arbeitspakete warten auf die Entscheidung zum unten beschriebenen Befund.
Es gibt keinen Push, Tag, Release, Merge oder Versionssprung.
Auf die anschließende Entscheidung „Zuerst den Dateizugriffsfehler gezielt
weiter untersuchen“ wurden die begrenzten Diagnoseprüfungen in Abschnitt 15
ausgeführt. Sie belegen einen reproduzierbaren Sperrmechanismus, aber noch
nicht die Ursache oder den Sperrhalter des ursprünglichen Fehlers.

## 1. Baseline-Commit

Branch: `rc/grosse-entfesselung`.

Commit: `cc39d0f3a4d03a1d94fb6f84fcc26e58e14bff10`

Betreff: `chore(rc): establish pre-consolidation baseline`

Vor dem Commit wurden sämtliche 320 Git-relevanten Dateien mit den finalen
Größen-/SHA256-Belegen aus Freigabe 0.5 verglichen. Keine zusätzlichen oder
fehlenden Pfade, keine Hashabweichung. Die 66 freigegebenen Kandidaten
(48 geänderte getrackte und 18 untracked Dateien) wurden aufgenommen.
Danach war der Arbeitsbaum sauber. Der Commit enthält den bereits
vorhandenen Entwicklungsstand, keine neue Konsolidierungsimplementierung.

Keine ignorierten dist-, bin-/obj- oder Testausgaben, Sicherungsordner oder
zusätzlichen Intune-Werkzeuge wurden aufgenommen. Der vorherige
Secret-Suchbefund aus 0.5 gilt für die hashidentischen Dateien; er ist kein
vollständiger Secret- oder Historienaudit.

Der externe Rückkehrpunkt aus 0.5 bleibt erhalten:
`D:\AetherBaselineSnapshots\rc05-20261109-212329`.
Siehe [Abschlussbericht 0.5](MILESTONE-0-CLOSURE-REPORT.md).

## 2. Bisherige Änderungen aus Meilenstein 1

Nur dieser Bericht und der aktuelle Auftragsstatus in der
[Projektübergabe](../PROJEKTSTAND.md). Keine Produktionsänderungen.
Keine weiteren Meilenstein-1-Commits erstellt.

## 3. Technische Verträge

Noch nicht geändert. Die erste Sichtung umfasst die bestehenden
[Sensormodelle](../src/SystemCockpit/Models.cs),
[Sensorsemantik](../src/SystemCockpit/Services/SensorSemantics.cs),
[Board-Pipe](../src/SystemCockpit/Services/BoardLiveFeed.cs) und das
[Verwaltungsprotokoll](../src/AetherOperations.Shared/ManagementProtocol.cs).
Ein neues Quellen-/Metadatenmodell ist noch nicht implementiert.

## 4. Fehlerbehandlungsmodell

Noch nicht konsolidiert. Der nachstehende Testfehler muss technisch
eingeordnet werden. Es wurde weder ein Catch ergänzt noch ein Fehler
unterdrückt oder in Erfolg umgedeutet.

## 5. Loggingmodell

Noch nicht geändert. Vorhandenes
[Aktionsjournal](../src/SystemCockpit/Services/ActionJournal.cs),
[lokale Messhistorie](../src/SystemCockpit/Services/LocalHistory.cs)
und die vorhandenen Trace-/ILogger-Pfade bleiben unverändert.
Keine neuen Logframeworks oder zusätzlichen personenbezogenen Logdaten.

## 6. Cancellation und Timeouts

Noch nicht geändert. Die unterschiedlichen Modelle aus
[Shared-Runner](../src/AetherOperations.Shared/ManagedActionRunner.cs),
[CommandRunner](../src/SystemCockpit/Services/CommandRunner.cs) und
OperationsHub bleiben erhalten. Es wird keine neue Abbruch- oder
Rollbackgarantie behauptet.

## 7. Lebenszyklen

Noch keine Korrekturen vorgenommen. Keine Timer, Provider, Streams,
Prozesshandler, Animationen oder laufenden Funktionen abgeschaltet.

## 8. Doppelte Abfragen

Die angeforderte Bewertung nach Kosten, Änderungsrate, Nutzern,
Aktualitätsbedarf und Invalidierung ist noch nicht durchgeführt.
Aus dem Audit bekannte Kandidaten sind weiterhin nur Kandidaten.

## 9. Tatsächlich optimierte Abfragen

Keine. Kein Cache eingeführt, keine Messquelle ersetzt.

## 10. Performance-Ausgangswerte

Keine neuen Produktmesswerte. Der Build dauerte laut Buildausgabe
27,98 Sekunden; dies ist keine Kaltstart-, UI-, CPU-, GPU- oder
Hardwareabfrage-Messung.

Das angeforderte reproduzierbare Messprotokoll
`PERFORMANCE-BASELINE.md` wird nach Fortsetzungsentscheidung erstellt.
Historische UI-Kompositionswerte werden nicht als neue Ausgangswerte
übernommen.

## 11. Datenschutzklassifikation

Noch kein neues feldbezogenes Modell implementiert. Vorhandene
Exportwarnungen, Reporting-Zustimmungen und Schutzabfragen bleiben
unverändert. Kein Zugriff auf produktive Konten oder Serverdaten.

## 12. RC1-Sprachstrategie

Vorgabe bleibt Deutsch zuerst bei erhaltener Mehrsprachigkeitsarchitektur.
Das bestehende Englisch-Gate wurde nicht geändert, deaktiviert oder
umgangen. Der angeforderte ausführliche Entwurf
`RC-LANGUAGE-STRATEGY.md` steht noch aus. Keine RC-Version erzeugt.

## 13. Ausgeführte Prüfungen

Nach dem Baseline-Commit, vor jeder technischen Änderung:

```powershell
dotnet build .\SystemCockpit.slnx -c Release --no-restore
dotnet run --project .\tests\SystemCockpit.Tests -c Release --no-build
dotnet run --project .\tests\AetherManager.Tests -c Release --no-build
```

Der Verwaltungsrunner wurde anschließend unverändert ein zweites Mal
ausgeführt. Im freigegebenen Diagnoseschritt folgten zwei weitere
unveränderte Verwaltungsrunnerläufe. Beide Runner verwenden ihre vorhandenen synthetischen
Fixtures; keine Reparaturen, Updates, Installation, Hardwareinitialisierung
oder Tenantaktionen wurden ausgeführt.

## 14. Testergebnisse

| Prüfung | Tatsächliches Ergebnis |
|---|---|
| Lösungsbuild Release | Bestanden, null Warnungen und null Fehler |
| STA-Grundrunner | Bestanden, 4.128 Assertions |
| Verwaltungsrunner, erster Lauf | Fehlgeschlagen nach 162 HTTPS-/Konten-/Rollen-/CSRF-/Sitzungsassertions |
| Verwaltungsrunner, unveränderter zweiter Lauf | Bestanden: 162 HTTPS-, 150 lokale Konten-, 14 Flottenprüfungen; insgesamt 384 Verwaltungsassertions |
| Verwaltungsrunner, Diagnosewiederholungen 1 und 2 | Beide bestanden, jeweils 384 Verwaltungsassertions |

Die Abschnittszahlen des Verwaltungsrunners werden nicht nochmals zur
ausgegebenen Gesamtsumme addiert. Der STA-Grundlauf ohne optionale
UI-Schalter ist nicht mit früheren kombinierten UI-Läufen gleichzusetzen.

## 15. Testabweichung und Ursachenstand

Erster Lauf:

```text
System.UnauthorizedAccessException: Access to the path is denied.
System.IO.FileSystem.MoveFile(..., Boolean overwrite)
LocalAccountService.SaveAsync(...):244
LocalAccountService.LoginAsync(...):102
VerifyAccountsAsync(...):54
```

Betroffen ist der atomare Austausch der synthetischen Kontendatei beim
Anmelden im vorhandenen Testpfad. Belege:
[Persistenz](../src/AetherManager.Server/Services/LocalAccountService.cs),
[Kontenfixture](../tests/AetherManager.Tests/AccountAssertions.cs).
Die Fixture legt ihren eigenen zufälligen Datenordner unter
`tests\.manager-test-runs` an.

Die bisherige Sichtung und der erfolgreiche unveränderte Wiederholungslauf
belegen keine eindeutige Ursache. Eine externe Dateisperre oder
Schutzsoftware ist nicht nachgewiesen und wird nicht als Erklärung
behauptet. Es wurde nichts an Dateirechten oder Schutzrichtlinien geändert.

Da noch keine technische Konsolidierungsänderung vorgenommen wurde, ist
dies keine nachgewiesene durch Meilenstein 1 verursachte Regression.
Es ist jedoch ein ungeklärter Fehler des Ausgangslaufs. Vorsorglicher
Stopp entsprechend der angeforderten Test- und Stoppregeln, statt
einen Wiederholungserfolg als Ursachenklärung zu behandeln.

### Freigegebene gezielte Diagnose

1. **Kontrollfluss:** `SaveAsync` erstellt eine neue zufällige Stagingdatei,
   schreibt und flusht sie, wartet `DisposeAsync` des Schreibstreams ab und
   führt erst dann `File.Move(..., overwrite: true)` aus. Der interne
   Kontenzustand wird erst nach erfolgreichem Dateiaustausch übernommen.
   Die Methoden sind durch den vorhandenen Semaphore geschützt.
   Die sichtbaren Testlesevorgänge werden vor der folgenden Anmeldung
   abgewartet. Daraus ergibt sich kein belegter eigener parallel
   offengehaltener Lesestream im ursprünglichen Fehlerpfad.
2. **Reproduktion ohne Sperre:** Zwei isolierte Serien mit jeweils
   500 Austauschvorgängen auf neu angelegten synthetischen JSON-Dateien
   bestanden. Schreiboptionen, Flush und asynchrones Schließen entsprechen
   dem Dateipersistenzablauf. Diese Probe läuft im PowerShell-.NET-Host
   und ist kein zusätzlicher Produkt- oder Kontentestinhalte-Nachweis.
3. **Kontrollierte Sperre:** Bei bewusst geöffnetem Lesestream scheitert
   derselbe Dateiaustausch reproduzierbar mit
   `UnauthorizedAccessException`, HRESULT `0x80070005`.
   Dies wurde für `FileShare.Read`, `Read | Delete`, `ReadWrite` sowie
   `ReadWrite | Delete` beobachtet. In allen vier Fällen blieb die
   Zieldatei unverändert. Die Dateien hatten das Attribut `Archive`,
   nicht `ReadOnly`. Nach Schließen und Entfernen der eigenen
   Fixturedateien wurden die beiden Diagnoseordner entfernt.
4. **Unveränderter Produktpfad:** Zwei zusätzliche Verwaltungsrunnerläufe
   bestanden jeweils 384 Assertions. Zusammen mit dem unmittelbaren
   Wiederholungslauf liegen damit drei erfolgreiche Läufe nach dem
   ursprünglichen Fehler vor. Kein produktiver Quellcode und kein Test
   wurde dafür geändert.
5. **Windows-Ereignisse:** Im untersuchten Zeitfenster ab 21:50 wurden
   keine zum konkreten Fixturepfad passenden Defender-Ereignisse 1123/1124
   gefunden. Das ist weder ein allgemeiner AV-Ausschluss noch ein Beweis,
   dass keine andere Dateisperre bestand.

**Einordnung:** Ein dauerhaft fehlendes Schreibrecht ist für die neu
erzeugten erfolgreichen Fixtures nicht nachgewiesen. Ein offener Handle
kann die beobachtete Exceptionklasse auslösen, ohne dass ein
Berechtigungswechsel nötig ist. Für den ursprünglichen Lauf wurden
jedoch weder ein HRESULT noch der Handlehalter aufgezeichnet. Daher ist
„externe Sperre“, insbesondere „Virenscanner“, weiterhin nur eine
Hypothese, keine nachgewiesene Ursache. Keine Produktionsreparatur,
keine Retrylogik und keine Änderung von ACLs oder Schutzsoftware.

## 16. Offene Punkte und Entscheidung

Der freigegebene begrenzte Diagnoseschritt ist ausgeführt. Eine endgültige
Ursachenzuordnung bleibt offen; dazu müsste der ursprüngliche Fehler mit
gleichzeitiger auf die Fixture begrenzter Handle-/Dateioperationsdiagnose
erneut auftreten. Keine systemweite Aufzeichnung oder dauerhafte
Überwachung wurde eingerichtet.

Die technische Konsolidierung bleibt vorerst angehalten. Eine Fortsetzung
trotz der dokumentierten intermittierenden Ausgangsabweichung erfordert
eine ausdrückliche Entscheidung; eine bloße Wiederholung bis Grün
ersetzt sie nicht.

Bis dahin bleiben alle neun Konsolidierungsarbeitspakete offen:
Quellenmodell, Fehler, Logging, Cancellation, Lebenszyklen, Messbarkeit,
Abfragebewertung, Datenschutz und RC1-Sprachstrategie.
Keine Assertions gelöscht, abgeschwächt oder Test-Snapshots aktualisiert.

## 17. Risiken

Der erfolgreiche zweite Lauf ist kein Stabilitätsnachweis für den
Dateiaustausch. Eine pauschale Wiederholungslogik, das Ignorieren der
Exception oder das Aufweichen der Dateirechte würde die Ursache
verdecken und wurde nicht implementiert.

Die übrigen RC-Risiken aus dem
[Readiness-Audit](RC-READINESS-AUDIT.md) bleiben bestehen.

## 18. Auswirkungen auf Meilenstein 2 und 3

Keine Umsetzung und keine Vorwegnahme. Retro-Neon, Cockpitanzeigen,
umlaufende Linien, Animationen und Funktionsumfang bleiben erhalten.
Meilenstein 1 ist noch nicht abnahmebereit; Meilenstein 2 und 3 starten
nicht aufgrund dieses Zwischenberichts.
