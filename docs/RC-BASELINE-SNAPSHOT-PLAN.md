# AETHER — Baseline-Sicherungsplan, Freigabe 0.5

## Grenze und Ausgangspunkt

Nur lesen, inventarisieren, sichern und dokumentieren. Kein Commit, Staging,
Push, Tag, Reset, Checkout, Clean, Build oder Installerlauf. Dieser Plan wird
vor der Dateikopie erstellt. Er ist keine Freigabe für Meilenstein 1.

- Branch: `rc/grosse-entfesselung`.
- HEAD: `53823883a9d1187395dc0fa3e3d8deebd3d3d8cb`.
- Frische Gitinventur: 48 geänderte getrackte und 14 untracked Dateien,
  kein Staging, keine gelöschten oder umbenannten Dateien.
- Keine ausschließlich lokalen Commits gegenüber den vorhandenen lokalen
  Remotereferenzen; keine Stashes. Kein Fetch oder Remoteabgleich durchgeführt.
- Gegenüber dem ersten Audit zusätzlich der Auditbericht; die Übergabe wurde
  dort bereits geändert. Alte Zahlen dienen nicht als neue Inventur.

Die genauen Datei-, Größen-, Hash- und Klassifikationslisten werden im
[Hashmanifest](RC-BASELINE-HASHES.csv) abgelegt. Das Manifest enthält keinen
Dateiinhalt und ist allein **kein Backup**.

## Bestände und Ausschlussregeln

Die Inventur umfasst den gesamten Workspace einschließlich versteckter Dateien,
ignorierter Ausgaben und vorhandener `dist`-Historie. `.git` wird separat über
Gitmetadaten und ein externes Bundle behandelt. Verzeichnislinks werden nicht
verfolgt. Keine fremden Projekte oder Windows-Benutzerprofile werden durchsucht.

| Klasse | Inhalt | Baseline-Commitkandidat | Externe Sicherung |
|---|---|---|---|
| A | Produktquellen, XAML, Ressourcen und Skins | Ja, aktueller nicht sensibler Bestand | Bytegenaue Kopie |
| B | Dokumentation einschließlich bisheriger Dirty-Dokumente | Ja | Bytegenaue Kopie |
| C | Tests, Fixtures, Testkonfiguration | Ja, keine echten Zugangsdaten | Bytegenaue Kopie |
| D | Projekt-, Build-, Workflow-, Provenienz- und Gitignoredateien | Ja | Bytegenaue Kopie |
| E | Inno-Installerquellen | Ja, keine Änderung ihres Verhaltens | Bytegenaue Kopie |
| F | bin/obj, Cache, Testausgaben | Nein | Inventar/Hash; neu erzeugbar, nicht Teil der Quellwiederherstellung |
| G | Assets, gebündelte Tools, historische Releasepakete | Assets/erlaubte Tools wie bisher; dist und externes Intune-Tool nein | Eigene Dateikopie einschließlich dist, nichts überschreiben |
| H/I/J | Temporäres, Benutzerdaten, Zugangsdaten/Schlüssel | Nein | Nicht automatisch kopieren; gesondert sicher behandeln |
| K | Unklarer Bestand oder Links | Nein bis geklärt | Nicht automatisch verfolgen/kopieren |

Prüfung sensibler Kategorien: Dateinamen für `.env`, private Schlüssel,
Zertifikatscontainer, Token-/Konten-/Client-/Benutzerkonfiguration; zusätzlich
begrenzte Inhaltssuche nach konkreten Private-Key-/Tokenmustern in kleinen,
nicht generierten Textdateien. Nur Trefferart/Existenz dokumentieren, keine
Geheimnisse. Dies ist keine Garantie einer vollständigen Secret-Erkennung.
Produktive Daten außerhalb des Workspace, Windows-Zertifikatsspeicher, DPAPI-
Schlüssel und persönliche Einstellungen werden nicht ausgelesen oder kopiert.
Generierte Ausgaben und alte dist-Inhalte werden nicht als neue Quelllektüre
oder vollständiger Secret-Scan ausgegeben.

## Sicherungsablauf

1. Frischen Branch/HEAD/Status und SHA256-Inventar vor Änderungen sichern.
2. Nicht sensible, nicht generierte Restore-Dateien in einen **neuen externen**
   Ordner `D:\AetherBaselineSnapshots\rc05-20261109-212329` kopieren. Ist das
   Ziel vorhanden, abbrechen statt überschreiben. Relative Pfade beibehalten.
3. Auch die 8.403 vorhandenen dist-Dateien und das separat bereitgestellte
   Intune-Tool sichern; bestehende Pakete nicht ausführen oder verändern.
4. Gitreferenzen/Commitobjekte in `repository.bundle` mit `git bundle create
   --all` exportieren. Das ist keine neue Referenz im Repository und kein
   Commit. Gitstatus/Referenzlisten separat exportieren; lokale Gitkonfiguration
   nicht ungeprüft kopieren.
5. Jede Kopie mit Größe und SHA256 gegen das Ausgangsinventar prüfen; bei
   zwischenzeitlicher Änderung abbrechen. Keine stillen Auslassungen.
6. Quelle aus der Kopie in einen getrennten externen Probeordner zurückkopieren,
   alle Größen/Hashes vergleichen; dort nichts bauen oder ausführen. Für dist
   genügt vollständiger Hashvergleich der separaten Sicherungskopie.
7. Nach Abschluss der Dokumente einen getrennten, ebenfalls hashgeprüften
   Dokumentationsnachtrag sichern. Kein Manifest kann sich selbst hashen:
   Manifesthash und Nachtragmanifest liegen extern.

Die Kopie auf demselben Laufwerk schützt vor Bearbeitungsfehlern, **nicht**
vor Datenträgerverlust, Ransomware oder Verlust des gesamten Rechners.
Eine zusätzliche getrennte Offlinekopie bleibt eine Betreiberaufgabe.

## Restore-Matrix

| Bereich | Git ausreichend | Zusätzliche Sicherung nötig | Grund | Restore-Prüfung |
|---|---|---|---|---|
| Quellcode | Für HEAD ja, Dirty Tree nein | Ja | Lokale Änderungen liegen nicht im Commit | Alle Quellkopien Größe/SHA256 |
| Dokumentation | Nur HEAD | Ja | Geänderte und neue Berichte | Baseline plus separater Abschlussnachtrag |
| Tests | Nur HEAD | Ja | Änderungen und neue Graph-Fixture | Quellrestore, kein Teststart |
| Projektdateien | Nur HEAD | Ja | Geändertes Serverprojekt/Publishprofil | Bytevergleich |
| Buildskripte | Nur getrackter HEAD | Ja | Neues Manager-Publishskript | Bytevergleich |
| Installerquellen | Nur HEAD | Ja | Beta-44-Änderung und neue Manager-ISS | Bytevergleich, kein Compilerlauf |
| Assets/lokale Ressourcen | Nur getrackter HEAD | Ja | Ressourcentexte geändert; Binärassets nicht semantisch gelesen | Datei-/Hashvergleich |
| Untracked Entwicklung | Nein | Ja | Noch kein Gitobjekt/Commit | Vollständige Pfadliste plus Kopie |
| Generierte Dateien | Nein | Nicht für Quellrestore | bin/obj reproduzierbar neu aufbauen; Bytegleichheit eines Neubuilds nicht zugesagt | Inventar vorhanden; keine Wiederherstellung behauptet |
| Lokale Konfiguration | Teilweise | Bei echter lokaler Konfiguration gesondert | Geheimnisse/absolute Pfade möglich | Nur nicht sensible Projektkonfiguration in Kopie |
| Zertifikate/Signierung | Nein | Separates gesichertes Betreiberverfahren | Privater Schlüssel und Speicher/ACL liegen außerhalb Git | Kein Export/Restore in 0.5 |
| Server-/Clientkonfiguration | Nur öffentliche Vorlagen | Echte Instanzen separat | DPAPI/Konto/Rechte und Opt-ins sind maschinengebunden | Kein produktiver Store-/DPAPI-Restore behauptet |
| Entwicklungs-JSON-Stores | Nicht automatisch | Nur explizit freigegebene synthetische Fixtures | Echte Konten/Tokens/Reports ausschließen | Workspaceinventar; keine Profilsuche |
| Benutzer-/Testdaten | Synthetische getrackte Fixtures ja | Persönliche Daten nicht übernehmen | Datenschutz und fremde Profile | Kein produktiver Datenbackup |
| dist/Portable/Setups | Nein, ignoriert | Ja | Historische Binärartefakte nicht neu erzeugen | Vollständiger SHA256-Vergleich |
| Intune-Prep-Tool | Nein, separat bereitgestellt | Ja | Externe Binärabhängigkeit | Bytevergleich, keine Ausführung |
| Gitobjekte/Referenzen | Arbeitsdateien allein nein | Bundle und Metadaten | Basis-/Historienbezug | Bundleprüfung und externer Proberepository-Check |

## Rückkehrverfahren

Nicht über den bestehenden Workspace schreiben. Zuerst Sicherungsmanifest
und Bundle prüfen. Ein neues leeres Restore-Ziel verwenden, Commitobjekte
aus dem Bundle bereitstellen und danach die gesicherten Arbeitsdateien
einschließlich untracked Quellen einspielen. Bei im Ausgangsinventar
verzeichneten Löschungen nicht einfach HEAD-Dateien stehen lassen; aktuell
wurden keine Löschungen gefunden. Bin/obj getrennt neu erzeugen, falls später
ein Build beauftragt wird. Öffentliche Projektkonfiguration wiederherstellen;
produktive Secrets/DPAPI/Zertifikate ausschließlich separat provisionieren.
Kein automatisches Zurücksetzen laufender Windows-Aktionen durch Quellrestore.

Vor einem Baseline-Commit entscheiden lassen, welche bereits vorhandenen
Änderungen inhaltlich dazugehören. Die [Abschlussvorlage](MILESTONE-0-CLOSURE-REPORT.md)
nennt die tatsächlichen Sicherungsergebnisse, Kandidatenlisten und offenen
Punkte. Dieser Plan behauptet noch keine erfolgreich erstellte Kopie.
