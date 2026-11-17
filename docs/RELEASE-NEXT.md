# AETHER 0.7.0 Beta 53 – Local Intelligence

Beta 53 ist als öffentliches Prerelease verfügbar.

## Highlights

- Lokales Windows-Systemcockpit für Systemstatus, Hardware, Leistung,
  Diagnose und ausgewählte Verwaltungsabläufe.
- AETHER Intelligence verbindet lokale Systeminformationen mit
  strukturierten Claims und zugehörigen Evidence.
- Qualifizierter lokaler Phi-4-Mini-Pfad über Ollama nach separater
  Einrichtung, System- und Integritätsprüfung sowie ausdrücklicher
  Lizenzannahme.
- Evidence-/Claim-Validierung prüft strukturierte Claims gegen kanonische
  Werte und Belegreferenzen, bevor Aussagen angezeigt werden. Unbelegte oder
  widersprüchliche Claims werden abgelehnt.
- Zero-Click Local AI verwendet den qualifizierten lokalen Modellpfad nach
  der Einrichtung automatisch für Fragen, ohne jedes Mal ein Modell
  auszuwählen.
- Korrigierter Startup- und Branding-Ressourcenvertrag; der manuelle
  Beta-53-Smoke-Test bestätigte den Start, das Bootlogo und das Fenstersymbol.

## AETHER Intelligence

AETHER Intelligence ist kein gewöhnlicher Chatbot. Antworten beziehen sich
auf den für die Anfrage verfügbaren Evidence-Kontext. Strukturierte Claims
werden vor der Anzeige validiert; unbelegte oder widersprüchliche Aussagen
werden verworfen. Die formale Prüfung verbessert Nachvollziehbarkeit, ist
aber keine allgemeine semantische Wahrheitsgarantie.

## Download und Installation

- [Portable win-x64 herunterladen](https://github.com/oa7779/AetherCockpit/releases/download/v0.7.0-beta.53/AETHER-0.7.0-beta.53-Portable-win-x64.zip)
- [SHA256SUMS.txt herunterladen](https://github.com/oa7779/AetherCockpit/releases/download/v0.7.0-beta.53/SHA256SUMS.txt)

Das ZIP vollständig entpacken; DLLs, Laufzeit und Begleitdateien müssen
zusammenbleiben. `SystemCockpit.exe` für die unterstützten
Verwaltungsabläufe erhöht starten. Vor der Verwendung die ZIP-Prüfsumme mit
SHA256SUMS.txt vergleichen:

`b2a12f2ab1e0e7b9bbe0b10d7d8d54d021e66cb5ed4d29095e60678f556eec00`

## Local AI

Modellgewichte sind nicht im GitHub-Paket enthalten. AETHER richtet den
lokalen Modellpfad über den vorgesehenen separaten Setup-Ablauf ein. Dieser
prüft Systemfähigkeit und Integrität und verlangt die ausdrückliche Annahme
der Modelllizenz. Der normale Frageablauf lädt weder Runtime noch
Modellgewichte herunter. Der qualifizierte lokale Phi-Pfad nutzt Ollama und
benötigt keine Cloud-Inferenz.

## Hinweise

- Beta-Prerelease; Hardware- und Sensorverfügbarkeit hängen von Gerät,
  Treibern und Windows-Konfiguration ab.
- Der Build ist unsigniert.
- Ein Installer ist nicht verfügbar.
- Aktuelle Beta-53-Screenshots folgen später.

## Weiterführend

- [Produktübersicht](../README.md)
- [Versionsgeschichte](../CHANGELOG.md)
- [Portable-Installation](INSTALLATION.md)
- [Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md)
- [AETHER Intelligence](INTELLIGENCE-ARCHITECTURE.md)
