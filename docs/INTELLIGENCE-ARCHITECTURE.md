# AETHER Intelligence: Architektur und Grenzen

AETHER Intelligence verbindet lokale Systemmessungen mit beleggebundenen
Claims. Der Ablauf trennt Quelle, strukturierte Werte, Modelltext und
validiertes Ergebnis.

## Antwortablauf

1. AETHER liest für die konkrete Frage verfügbare lokale Windows- und
   Hardwareinformationen.
2. Der Anfragekontext ordnet Messwerte, Einheiten, Quellen und
   Verfügbarkeitsstatus als Evidence ein.
3. Strukturierte Claims führen Claim-Typ, Subjekt, Eigenschaft, Operator,
   kanonischen Wert, erlaubte Darstellung, Verfügbarkeit, Einheit und
   Evidence-IDs.
4. Der qualifizierte Phi-Pfad erstellt eine strukturierte Antwort.
5. AETHER prüft zuerst Evidence-Referenzen, danach die strukturierten Claims
   und zuletzt den sichtbaren Antworttext gegen die validierten Claims.
6. Die Oberfläche zeigt das Ergebnis mit den zugehörigen Belegen und dem
   Validatorstatus.

Der `CanonicalValue` ist die maschinenprüfbare Wahrheit des Claims.
`DisplayValue` ist die deterministisch erlaubte Darstellung für den
Antworttext. Das Modell darf weder Identitäten selbst normalisieren noch
die zulässigen Darstellungen erweitern. Ein Widerspruch zwischen Claim und
Text führt zur Ablehnung.

Positive Aussagen und Aussagen über Nichtverfügbarkeit werden getrennt
geprüft. Ein fehlender Sensor kann deshalb nicht in eine Messung oder einen
positiven Zustand umgedeutet werden.

## Lokaler Modellpfad

Der qualifizierte Modellpfad verwendet Phi 4 Mini (`phi4-mini:3.8b`) über den
lokalen Ollama-Provider. Die Modellinstallation ist ein separater Ablauf
mit Hardwareprüfung, Lizenzanzeige und ausdrücklicher Annahme. Der
Portable-Download enthält keine Modelldateien.

Wird der Modellpfad nicht freigegeben oder ist er nicht verfügbar, verwendet
AETHER den dafür vorgesehenen deterministischen Antwortpfad. Ein Fallback
wird als solcher ausgewiesen; er ist keine Modellantwort.

## Sicherheitsgrenzen

- Eine Intelligence-Anfrage ist lesend und erteilt dem Modell keine
  Befugnis, Prozesse, Dienste, Dateien oder Systemeinstellungen zu ändern.
- Geführte Verwaltungsaktionen sind ein eigener Bedienablauf mit Ziel,
  Vorschau und Bestätigung.
- Die freie Administratorkonsole führt eingegebene Befehle unmittelbar aus.
- Der Claim-/Evidence-Validator prüft Struktur, Referenzen und
  Konsistenzregeln. Er beweist nicht die allgemeine semantische Wahrheit
  einer Aussage oder die Korrektheit jedes Windows-Providers.

## Datenabdeckung

Die verfügbaren Messwerte hängen von Windows, Hardware und Treibern ab.
Nicht verfügbare Sensoren und Eigenschaften bleiben ausdrücklich
unverfügbar. Schwellenwerte oder Herstellergrenzen sind keine gemessenen
Werte.

## Weiterführend

- [Lokale Modellinstallation](INTELLIGENCE-MODEL-INSTALLATION.md)
- [Modellstatus](INTELLIGENCE-MODEL-MATRIX.md)
- [Bedienung und Diagnostik](INTELLIGENCE-OPERATIONS.md)
- [Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md)
