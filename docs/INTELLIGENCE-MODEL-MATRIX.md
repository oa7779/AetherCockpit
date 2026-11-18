# Modellstatus

Diese Übersicht beschreibt den für AETHER freigegebenen automatischen
Modellpfad. Sie ist keine Benchmark-Tabelle.

| Modellpfad | Status | Provider | Freigabebedingung |
|---|---|---|---|
| Phi 4 Mini (`phi4-mini:3.8b`) | Für den qualifizierten lokalen Pfad freigegeben | Lokales Ollama | Getrennte Einrichtung, Systemfähigkeitsprüfung, ausdrückliche Lizenzannahme und erfolgreiche Integritätsprüfung |
| Nemotron 3 Nano 4B | Abgelehnt; in Beta 53 nicht auswählbar | — | Keine Produktfreigabe |
| Nemotron 3 Nano 30B | Hardwarequalifikation ausstehend; in Beta 53 nicht auswählbar | — | Kein vollständiger Digest-, Leistungs- oder Hardwarevertrag |
| Andere Modelle | Nicht für den automatischen Produktpfad qualifiziert | — | Keine automatische Auswahl |
| Deterministischer Antwortpfad | Verfügbarer Nicht-Modellpfad | AETHER | Wird verwendet, wenn der qualifizierte Modellpfad nicht verfügbar oder nicht freigegeben ist; als Fallback ausgewiesen |

Der Portable-Download enthält keine Modellgewichte. Eine erfolgreiche
Claim-/Evidence-Prüfung bestätigt die implementierten Struktur- und
Konsistenzregeln, nicht die allgemeine semantische Wahrheit einer Antwort.

Siehe [Modellinstallation](INTELLIGENCE-MODEL-INSTALLATION.md) und
[Architektur](INTELLIGENCE-ARCHITECTURE.md).
