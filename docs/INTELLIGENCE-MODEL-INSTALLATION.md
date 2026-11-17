# Lokale Modellinstallation

AETHER richtet das qualifizierte lokale Modell getrennt von den
Cockpitansichten ein. Normale Fragen laden keine Runtime oder Modellgewichte
herunter.

## Qualifiziertes Modell

- Modell: Phi 4 Mini, `phi4-mini:3.8b`
- Inferenz: lokaler Ollama-Provider
- Freigabe: ausschließlich nach Systemfähigkeits- und Integritätsprüfung
- Modellgewichte: separat gespeicherte lokale Dateien, nicht im
  Portable-Paket enthalten

Andere Modelle sind für den automatischen Produktpfad nicht als
gleichwertig qualifiziert.

## Einrichtung

1. AETHER öffnen und den ausdrücklich getrennten Modell-Setup-Ablauf wählen.
2. Die angezeigte Systemfähigkeitsprüfung abwarten.
3. Modell und Lizenzbedingungen lesen.
4. Lizenz ausdrücklich annehmen, wenn die Einrichtung fortgesetzt werden
   soll.
5. Den von AETHER geführten Runtime- und Modellbezug abschließen.
6. Die Integritätsprüfung und angezeigte Bereitschaft abwarten.

Ohne Lizenzannahme wird die Einrichtung nicht fortgesetzt. Bei fehlender
Systemfähigkeit oder fehlgeschlagener Integritätsprüfung bleibt der
automatische Modellpfad gesperrt. Die angezeigte Ursache beachten, statt
einen alternativen Modellpfad einzurichten.

## Fragen stellen

Nach bestätigter Einrichtung wählt AETHER das qualifizierte lokale Modell
automatisch aus. Die Anfrage sammelt nur den für die Frage vorgesehenen
Kontext. Die strukturierte Antwort wird gegen Evidence und Claims geprüft.
Ein Fallback oder eine Ablehnung wird in der Oberfläche erkennbar
ausgewiesen.

Der normale Frageablauf erteilt dem Modell keine Befugnis, Verwaltungs-
oder Betriebssystemaktionen auszuführen. Diese Funktionen haben eigene
Bedienabläufe und Bestätigungen.

## Modellstatus und Daten

Runtime und Gewichte werden lokal verwaltet und gehören nicht zum
Portable-Programmverzeichnis. Während des gesonderten Setup-Ablaufs wird
der Modellbezug angezeigt. Der normale Frageablauf verwendet den lokalen
Provider; ein Cloud-Fallback gehört nicht zum qualifizierten Pfad.

Laufdiagnosen zeigen technische Metadaten wie Modell-, Token- und
Validatorstatus, nicht die vollständige Modellantwort. Diagnosen und
Screenshots vor einer Weitergabe auf private Systeminformationen prüfen.

## Grenzen

Ein erfolgreicher Validatorlauf bestätigt die geprüften strukturierten
Claims und Belegbeziehungen, nicht jede Bedeutung oder Vollständigkeit des
Antworttextes. Hardwareabdeckung und Sensorverfügbarkeit hängen vom
jeweiligen System ab. Grenzwerte ersetzen keine Messungen.

Weitere Details:
[Architektur](INTELLIGENCE-ARCHITECTURE.md),
[Modellstatus](INTELLIGENCE-MODEL-MATRIX.md) und
[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md).
