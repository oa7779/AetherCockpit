# Lokale Modellinstallation

AETHER richtet das qualifizierte lokale Modell getrennt von den
Cockpitansichten ein. Normale Fragen laden keine Runtime oder Modelldateien
herunter.

## Qualifiziertes Modell

- Modell: Phi 4 Mini, `phi4-mini:3.8b`
- Inferenz: lokaler Ollama-Provider
- Freigabe: ausschließlich nach Systemfähigkeits- und Integritätsprüfung
- Modelldateien: separat gespeicherte lokale Dateien, nicht in Portable,
  Setup oder Serverpaket enthalten

Andere Modelle sind für Beta 55 weder auswählbar noch freigegeben. Nemotron
3 Nano 4B bleibt technisch abgelehnt; Nemotron 3 Nano 30B besitzt keinen
vollständigen Hardware-Qualifikationsnachweis. Beide sind nicht Bestandteil
der Beta-55-Modellauswahl.

## Einrichtung

1. Im installierten Produkt die Startmenü-Verknüpfung **AETHER Lokale KI
   einrichten** beziehungsweise im Portable-Verzeichnis
   `AetherLocalAiSetup\AetherLocalAiSetup.exe` starten. Die Installation
   kann diesen Assistenten nach dem Hauptsetup auf ausdrückliche Auswahl
   öffnen.
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

Runtime und Modelldateien werden lokal verwaltet und gehören nicht zum
Programmpaket. Während des gesonderten Setup-Ablaufs wird
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
