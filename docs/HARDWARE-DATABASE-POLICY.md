# Hardware Intelligence — Datenbankrichtlinie

Es ist keine Hardwaredatenbank gebündelt oder aktiviert. Die aktuelle
Hardwarezentrale liest Windows-/Providerdaten. Produktnamen werden nicht
für Codename-, Chipsatz-, TDP- oder Funktionsannahmen verwendet.

`HardwareDatabaseEntry` bereitet einen späteren versionierten Katalog vor:
ID, Quelle, Lizenzstatus, fachliches Prüfdatum, Datenbankversion, exaktes
Matchkriterium, Confidence und Dokumentationslink. Die derzeitige Validierung
akzeptiert nur einen vollständigen Datensatz, ein exaktes nicht leeres Match,
ein plausibles Prüfdatum, eine sichere Dokumentations-URL ohne Credentials
und einen explizit erfassten Lizenz-Identifier CC0-1.0, MIT oder BSD-3-Clause.
Diese technische Prüfung ersetzt keine Prüfung des tatsächlichen
Quellbestands und seiner Nutzungsbedingungen.

Andere Lizenzen sind nicht automatisch unzulässig; für sie fehlt hier eine
geprüfte Import-/Attributionsregel. Auch eine passende Lizenz allein erlaubt
keinen ungeprüften Datensatz. Es gibt bewusst noch keinen Importer.

Für einen späteren Datenbestand sind vor der Aktivierung zusätzlich
Quellfassung und Lizenztext, Änderungs-/Weitergabebedingungen, erforderliche
Attribution, Versionierung, Invalidierung und fachliche Matches zu prüfen.
Eine bloß ähnliche Modellbezeichnung reicht nicht. Falsche oder mehrdeutige
Matches bleiben ohne Zuordnung.

Ein aktivierter Datensatz muss als **Database**, mit Version, Quelle und
Match im Inspektor und Bericht erscheinen. Er ist keine Live-Messung.
Spezifikationen dürfen keine gemessenen Temperaturen, Leistungsaufnahme,
PCIe-Linkzustände oder aktuelle Funktionen ersetzen.

Es wurden keine proprietären Tabellen, Datenbanken oder Binärressourcen
fremder Analyseprogramme extrahiert oder kopiert. Die Testdaten sind eigene
synthetische Fixtures und kein Produktdatenbestand.

[Datenquellen](HARDWARE-DATA-SOURCES.md) ·
[Brandingrichtlinie](HARDWARE-BRANDING-AND-LOGO-POLICY.md)
