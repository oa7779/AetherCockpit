# Benutzerhandbuch

## Erststart

`SystemCockpit.exe` starten und die angezeigte Windows-Bestätigung beachten.
AETHER zeigt beim ersten Start die Produktbedingungen und den
Inventarfortschritt. Das lokale Inventar kann Gerätenamen, Rechnernamen und
weitere Systemdetails enthalten. Exportierte Dateien vor einer Weitergabe
prüfen.

## Navigation

Die Navigation führt durch Übersicht, Hardware, Diagnose, Wartung,
Verwaltung und Hilfe. **Funktion finden** öffnet passende Bereiche; eine
Suche allein startet keinen Auftrag.

## Systemübersicht und Hardware

Die Cockpitansichten zeigen verfügbare CPU-, Speicher-, Datenträger-,
Netzwerk- und Windows-Informationen. Werte stammen von den angezeigten
lokalen Quellen. Abdeckung hängt von Gerät, Treiber und Windows ab.

Nicht verfügbare Sensoren bleiben als solche gekennzeichnet.
Temperaturgrenzen und andere Schwellenwerte sind keine Messungen.
Schematische Hardwareansichten zeigen keine elektrische Topologie.

## AETHER Intelligence

1. **AETHER Intelligence** öffnen und eine lesende Frage zu den verfügbaren
   Systeminformationen stellen.
2. Antwort, Evidence-Referenzen und Validierungsstatus prüfen.
3. Nicht verfügbare Daten und abgelehnte Claims nicht als bestätigte
   Systemwerte verwenden.

Der qualifizierte lokale Modellpfad verwendet Phi 4 Mini über Ollama. Er
steht erst nach separater Systemprüfung, Lizenzanzeige und ausdrücklicher
Annahme bereit. Der normale Frageablauf lädt kein Modell herunter und führt
keine Verwaltungsaktion aus. Eine formale Claim-/Evidence-Prüfung ist keine
allgemeine semantische Wahrheitsgarantie.

Siehe [lokale Modellinstallation](INTELLIGENCE-MODEL-INSTALLATION.md) und
[Antwortstatus und Diagnostik](INTELLIGENCE-OPERATIONS.md).

## Diagnose und Berichte

Diagnose- und Analyseansichten ordnen lokale Informationen nach Quelle,
Zeit und verfügbarem Status. Inventar und Ereignisse helfen beim Einordnen,
beweisen aber allein keine Ursache.

Berichte und Screenshots können Benutzer-, Geräte-, Prozess-, Netzwerk-
oder Dateipfade enthalten. Vor einer Weitergabe Inhalte prüfen und
gegebenenfalls selbst redigieren.

## Verwaltung

Geführte Verwaltungsabläufe zeigen Ziel und erwartete Wirkung. Vor einer
Änderung den vollständigen Vorschlag prüfen und die angezeigte Bestätigung
bewusst erteilen. Windows-Rechte und Richtlinien können eine Funktion
begrenzen.

Die freie Administratorkonsole führt eingegebene Befehle unmittelbar aus.
Sie ist kein Teil des lesenden Intelligence-Frageablaufs.

## AETHER Guardian

Guardian befindet sich in Entwicklung und ist keine freigegebene
Produktionsfunktion.

## Sicherheit und Datenschutz

Die optionale zentrale Verwaltung ist ein separater Ablauf und nicht für
die Nutzung des lokalen Cockpits erforderlich. Weitere Hinweise zu lokalen
Daten, optionaler Übertragung und Verwaltungsgrenzen stehen unter
[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md).
