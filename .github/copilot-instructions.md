# AETHER — Projekt- und Dokumentationsregeln

## Projektkontinuität und Arbeitsweise

- Vor Projektarbeit die [aktuelle Übergabe](../PROJEKTSTAND.md) und
  [Produktübersicht](../README.md) lesen. Befehle stehen in
  [Entwicklung](../docs/DEVELOPMENT.md) und [Tests](../docs/TESTING.md).
- Die Übergabe nach einem abgeschlossenen Release oder einer Änderung des
  Projektumfangs aktualisieren.
- Direkt in diesem Workspace arbeiten. Git-Status, Branch und Remote vor
  Veröffentlichungen prüfen; lokale Änderungen nicht durch Remote-Dateien ersetzen.
- Bestehende portable Releases unter `dist` erhalten. Neue Versionen nur
  bei beauftragter Veröffentlichung in separaten Ausgaben erstellen.
- Windows-Pfade in Befehlen verwenden. Das Testprojekt ist ein
  STA-Konsolenrunner mit Assertions, kein `dotnet test`-Projekt.
- Das ursprüngliche Retro-Neon-Design und die Bestätigungen für geführte
  Verwaltungsaktionen erhalten.
- Nicht verfügbare Sensoren bleiben nicht verfügbar; Temperaturgrenzen sind
  keine Messwerte.
- Reparaturen, Updates, Benchmarks oder Remoteänderungen nicht als
  Routinevalidierung ausführen.
- Das externe Wartungscenter-Projekt ist nur Referenzmaterial und darf
  nicht verändert werden.
- Keine Commits, Pushes, Tags oder Releases ohne ausdrücklichen Auftrag.

## Produktbeschreibung und Sprache

- AETHER immer als eigenständiges Produkt beschreiben. Keine Konkurrenzprodukte
  oder vermeintlichen Vorbilder für Vergleiche heranziehen.
- Technische Komponenten nur nennen, wenn sie tatsächlich verwendet oder
  integriert sind oder für Lizenzen und Kompatibilität relevant sind.
- Natürliches, sachliches Deutsch ohne Genderformen verwenden.
- KI-typische Wiederholungen und übermäßig defensive Negativlisten vermeiden.
  Funktionen zuerst positiv und verständlich beschreiben; Grenzen anschließend
  am relevanten Ort nennen. Echte Sicherheitsgrenzen erhalten.
- Keine Funktionen, Messwerte, Versionen, Fristen, Ports oder Limits erfinden.
  Dokumentationsaussagen vor Änderungen mit dem aktuellen Quellcode abgleichen.
  Nicht belegbare Angaben als offen kennzeichnen, statt sie zu ergänzen.

## Dokumentationsstruktur und Pflege

- [README](../README.md) als kompakte Produktübersicht behandeln.
- Versionsgeschichte ausschließlich im [CHANGELOG](../CHANGELOG.md) pflegen.
  Andere Dokumente dürfen ihren aktuellen Versionsstand nennen und auf die
  Geschichte verweisen, aber keine parallele Versionschronik führen.
- Build-Anweisungen in [DEVELOPMENT](../docs/DEVELOPMENT.md), Testdetails in
  [TESTING](../docs/TESTING.md) und Sicherheits- und Datenschutzdetails in
  [SECURITY-AND-PRIVACY](../docs/SECURITY-AND-PRIVACY.md) pflegen.
  Notwendige Warnungen direkt am jeweiligen Bedienablauf erhalten.
- Vorhandene Dokumente aktualisieren, statt denselben Inhalt in neuen Dateien
  zu duplizieren. Auf die zuständige Fachanleitung verweisen.
- GitHub-Flavored Markdown ohne kopierte HTML-Editorattribute verwenden.
  Tatsächlichen HTML-Programmcode und technische Beispiele nicht mit
  Editorresten verwechseln.
- UTF-8 mit deutschen Umlauten und ß verwenden. Dateinamen, Befehle,
  Parameter und technische Identifier unverändert lassen.
- Relative Links nach Umbenennungen repositoryweit aktualisieren und prüfen;
  auch Build-, Publish-, Setup- und Paketverweise berücksichtigen.
  Historische Releasepakete dabei nicht nachträglich verändern.
- Bei jeder neuen Beta README, CHANGELOG und gegebenenfalls betroffene
  Fachdokumente konsistent aktualisieren.
- Dokumentationsänderungen dürfen kein funktionales Programmverhalten ändern.
  Eingebettete Hilfe nur textlich korrigieren; Versions-, Zustimmungs-,
  Sensor-, Sicherheits- und Ausführungslogik unverändert lassen.
- Projektregeln in dieser Datei zusammenführen. Keine konkurrierenden
  Anweisungsdateien mit abweichenden Regeln anlegen.
