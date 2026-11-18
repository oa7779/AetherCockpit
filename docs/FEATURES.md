# Funktionsübersicht

AETHER verbindet lokale Windows- und Hardwareinformationen mit Diagnose,
Leistungsansichten und ausgewählten Verwaltungsabläufen. Die verfügbaren
Datenquellen unterscheiden sich je nach System; fehlende Werte bleiben als
nicht verfügbar gekennzeichnet.

## Systemübersicht

- Livewerte für CPU, Arbeitsspeicher, Datenträger und Netzwerk.
- Windows- und Gerätezustände mit sichtbarer Quellenabdeckung.
- Verlaufskurven und verfügbare Sensorwerte.

## Prozesse und Leistung

- Prozessübersicht mit Identitäts- und Leistungsdetails.
- Prozessbaum und begrenzte Historie für ausgewählte Messwerte.
- Belegte Beziehungen zu Windows-Diensten, Aufgaben, Verbindungen und
  Hardwarequellen, soweit die jeweiligen Provider die Zuordnung liefern.

Eine Prozess-I/O-Zahl ist kein Beweis physischer Datenträgeraktivität.
Ein Netzwerk-Endpunkt ist keine Netzwerkdatenrate.

## Hardware

- Hardwareinventar für Windows und verfügbare Gerätequellen.
- Sensoransichten mit Einheiten, Quelle und Verfügbarkeitsstatus.
- Datenträgerinformationen und verfügbare SMART-/NVMe-Befunde.
- Schematische Hardwareansichten, deren Zeichnung keine elektrische
  Topologie darstellt.

Temperaturgrenzen und andere Schwellenwerte sind keine Messwerte.

## Diagnose und Auswertung

- Windows- und Geräteprüfungen, Ereignisübersichten und lokale Berichte.
- Messwertvergleiche und ausgewählte lokale Aufzeichnungen.
- Hinweise auf fehlende oder eingeschränkte Quellen.

Eine Inventar- oder Ereignisanzeige bestätigt nicht die Funktionsfähigkeit
jeder Anwendung und ist für sich allein keine Ursachendiagnose.

## Wartung und Verwaltung

- Geführte Verwaltungsabläufe für ausgewählte Prozesse, Dienste, Aufgaben,
  Netzwerkeinstellungen und Windows-Funktionen.
- Vorschau des Ziels und der erwarteten Wirkung vor unterstützten Änderungen.
- Einzelbestätigung und anschließende Statusprüfung.
- Wartungspläne und Cache-Ansichten mit einer Vorschau der betroffenen
  Kategorien beziehungsweise Dateien.

Windows-Rechte, Richtlinien und externe Zustandsänderungen gelten weiterhin.
Nicht jede Aktion kann zurückgenommen werden. Die freie
Administratorkonsole führt eingegebene Befehle unmittelbar aus.

## AETHER Intelligence

AETHER Intelligence verbindet lokale Messungen mit Evidence und
kanonischen Claims. Der qualifizierte automatische Modellpfad verwendet
Phi 4 Mini (`phi4-mini:3.8b`) über den lokalen Ollama-Provider. Ein
strukturierter Antwortvalidator prüft Claims und ihre Belegreferenzen;
nicht belegbare oder widersprüchliche Aussagen werden abgelehnt.

Die lokale Modellinstallation erfolgt separat, mit ausdrücklicher
Lizenzannahme sowie Hardware- und Integritätsprüfung. Modelldateien sind
nicht Teil des Portable-Pakets. Eine gültige Referenzprüfung ist keine
allgemeine semantische Wahrheitsgarantie.

## Oberfläche und Desktop

- Eigenständige Retro-Neon-Cockpitoberfläche mit aufgabenbezogener Navigation.
- Live-Instrumente und Diagramme für verfügbare Messwerte.
- Desktopanzeigen und optionale Darstellungseinstellungen.
- Animationen lassen sich abschalten.

## AETHER Guardian

Guardian befindet sich in Entwicklung und ist keine freigegebene
Produktionsfunktion. Seine Entwicklungsansichten sind keine Zusage
qualifizierter Wiederherstellungs- oder Deep-Trace-Funktionen.

## Weiterführende Anleitungen

- [Benutzerhandbuch](USER-GUIDE.md)
- [Hardware und Sensoren](HARDWARE-SENSORS.md)
- [Verwaltung und Wartung](USER-GUIDE.md#verwaltung)
- [AETHER Intelligence](INTELLIGENCE-ARCHITECTURE.md)
- [Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md)
