# Anleitung für Betatester

**Beta: 0.7.0-beta.35**

Diese Anleitung hilft beim Nachstellen von Bedien- und Darstellungsfehlern.
Sie verlangt keine Reparatur oder Änderung eines produktiven Rechners.
Ein normaler AETHER-Start initialisiert allerdings die Hardware-Sensoren;
der Provider kann dafür einen lokalen Treiber laden.

## Vor dem Start

Setup oder vollständiges Portable-ZIP aus einer vertrauenswürdigen Quelle
verwenden und SHA256 vergleichen. Die Beta ist nicht signiert.
UAC läuft im verwendeten Administratorkonto; dessen lokale Einstellungen
werden genutzt. Wichtige Daten vor verändernden Tests sichern.

[Installation, Update und Entfernung](docs/INSTALLATION.md)
· [Bedienung](docs/USER-GUIDE.md)

## Kurzer Testrundgang

1. Erststart, EULA und Inventarfortschritt prüfen. Bei Inventarfehler
   erneut aktualisieren; Fehlertext notieren.
2. Cockpit, Prozesse, Sensoren und Laufwerke ansehen. Stimmen Einheiten,
   Gerätenamen und Kennzeichnung fehlender Werte?
3. Mainboard öffnen, Bauteile und Laufwerksnamen anklicken.
   Grafik, Textabstände und Animation bei eigener Auflösung/DPI prüfen.
4. Zwischen Seiten wechseln und **Zurück/Vor** sowie **Strg+K** ausprobieren.
5. xBar-Diagramme schalten, PIN/FLOAT und Monitorwahl prüfen.
6. Gadgets verschieben und Deckkraft ändern. Das Hauptfenster mit X
   ausblenden, über das Tray wieder öffnen und vollständig beenden.
7. Erneut starten und prüfen, ob Positionen und Einstellungen erhalten bleiben.
8. Hilfe und Diagnoseexport öffnen; Vorschau und Schwärzung prüfen.

Gemischte DPI, mehrere Monitore und Explorer-Neustart sind besonders
relevant für Fensterposition und Gadgets. Windows kann Fokuswechsel
und Benachrichtigungen begrenzen.

## Aktueller Testschwerpunkt

Setup, Portable-, Server- und Intune-Paket tragen denselben Beta-35-Stand.
Installationsversion und Prüfsumme vor dem Test vergleichen; das Intune-Paket
auf einem dafür vorgesehenen Gerät separat abnehmen.

- Verstärkten Glow in Hauptfenster, Dashboard und Schoner prüfen:
  Halos und Bewegung bei verschiedenen DPI-Einstellungen ansehen.
  Animation ausschalten: statischer Glow bleibt, Bewegungen enden.
- Den neuen Windows-Werkzeugbereich öffnen und Verfügbarkeit,
  Beschreibungen und Konsolenausgabe prüfen. Systemänderungen nicht
  als Teil dieses Darstellungsrundgangs ausführen.
- Separate HTTPS-Webverwaltung ansehen. Gerätefreigabe, Berichte und
  Auftragsstatus nur mit bewusst eingerichteten Testgeräten prüfen.
  Der optionale Clientdienst benötigt eine eigene Einrichtungsfreigabe.

- Hintergrunddiagnose in den Desktop-Einstellungen einschalten:
  erster lesender Lauf sofort, danach 30 Minuten Pause nach jedem Lauf.
  Letzten Bericht öffnen; ein Tray-Hinweis soll zu den Windows-Assistenten führen.
  Unveränderte Befunde sollen während derselben Sitzung nicht ständig neu melden.
- Zur Prüfung des Anmeldestarts muss die erhöhte Aufgabe separat genehmigt
  sein. Portable Nutzung richtet sie nicht ein.
- Npcap-Downloadauswahl getrennt vom automatischen Mitschnitt prüfen.
  Das Setup installiert Npcap nicht. Ein realer Mitschnitt ist für
  den Testrundgang nicht erforderlich.
- GPU-Kartenkörper, drei Lüfterringe, Anschlussblende und PCIe-Kontakte
  auf klare Darstellung und passende Auswahl prüfen.

Änderungsaufträge bleiben bestätigt. Die einzige eng begrenzte automatische
Reparatur ist der DNS-Cache-Assistent nach doppeltem Opt-in.
Das Diagnoseinventar ist keine
Funktionsprüfung jeder installierten Anwendung.

## Separate Tests

Ein Update zunächst nach vollständigem Beenden ohne vorherigen Uninstall
prüfen; Zielordner und erhaltene Einstellungen kontrollieren.
Autostart, Schoner, Serveranmeldung und reale
Netzwerkprüfungen nur testen, wenn sie gewünscht sind.
Im aktuellen Quellstand zeigt `/c` nur einen Hinweis auf die Windows-Einstellungen.
Die frühere Schoner-Musikfunktion ist entfernt; bestehende Releasepakete
behalten ihren früheren Funktionsstand.

Der Serverclient benötigt Einmalcode, manuelle Gerätefreigabe und ein
separates Übertragungs-Opt-in. Der neue Anmelde-/Reporting-Ablauf ist noch
nicht vollständig manuell auf einem Testgerät geprüft.
[Zentrale Verwaltung](docs/CENTRAL-MANAGEMENT.md).

Reparaturen, Cleaner-Löschungen, Updates, Benchmarks, SMART-Selbsttests,
Remote-Befehle und Paketmitschnitte sind keine Voraussetzung für Feedback.
Deinstallation entfernt auch lokale AETHER-Daten; vorher extern sichern.

## Fehler melden

```text
AETHER-Version:
Installierte oder portable Ausgabe:
Windows-Version und Build:
CPU / GPU / RAM, ohne Seriennummern:
Auflösung / Skalierung / Zahl der Monitore:
Neuinstallation oder Update; vorherige Version:
Schritte zum Nachstellen:
Erwartetes Ergebnis:
Tatsächliches Ergebnis und Fehlertext:
Immer oder gelegentlich:
Bereinigter Screenshot:
```

Bei Setupfehlern ein Protokoll mit `/LOG="Pfad"` erzeugen.
Berichte und Screenshots vor Weitergabe auf Kontonamen, Schlüssel,
Geräte-IDs und private Pfade prüfen. Die Export-Schwärzung ist
Mustererkennung, keine vollständige Anonymisierung.
[Sicherheit und Datenschutz](docs/SECURITY-AND-PRIVACY.md).

Für Entwickler stehen [Testmodi und Befehle](docs/TESTING.md) separat.
