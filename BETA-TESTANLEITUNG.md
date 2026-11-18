# Anleitung für Betatester

AETHER Beta 54 ist eine Vorabversion. Diese Anleitung beschreibt einen
risikoarmen
Bedienungsrundgang, keine Aufforderung zu produktiven Änderungen.

## Vor dem Start

- Nur ein vollständiges Portable-Paket aus einer vertrauenswürdigen Quelle
  verwenden und die veröffentlichte SHA-256-Prüfsumme vergleichen.
- Das vollständige Paket entpacken; keine einzelne EXE aus dem Ordner lösen.
- Keine Warnungen zu unsignierter oder nicht vertrauenswürdiger Software
  umgehen.
- Vor Änderungen wichtige Daten sichern und nur die Funktion testen, deren
  Wirkung verstanden wurde.

Portable, Setup und Server sind getrennte Programmpakete. Modelldateien
sind in keinem davon enthalten. Die optionale lokale Modellinstallation ist
ein separater Zustimmungsablauf.

## Bedienungsrundgang

1. AETHER starten und die Systemübersicht öffnen.
2. Hardware- und Sensoransichten prüfen. Fehlende Werte müssen als nicht
   verfügbar erkennbar bleiben.
3. Zwischen Übersicht, Hardware, Diagnose und Hilfe navigieren.
4. AETHER Intelligence nur mit einer lesenden Systemfrage verwenden und
   Evidence sowie Validatorstatus prüfen.
5. Eine geführte Verwaltungsfunktion nur öffnen, um Vorschau und
   Bestätigung zu beurteilen. Keine Änderung bestätigen, wenn sie nicht
   ausdrücklich beabsichtigt ist.
6. Guardian als Entwicklungsbereich erkennen; daraus keine
   Produktionsfreigabe ableiten.

## Fehler melden

Für einen reproduzierbaren Bericht genügen:

```text
AETHER-Version:
Windows-Version:
Schritte zum Nachstellen:
Erwartetes Ergebnis:
Tatsächliches Ergebnis:
Fehlermeldung:
```

Screenshots und Diagnosen vor Weitergabe auf Kontonamen, Gerätekennungen,
private Pfade, Prozesse und Netzwerkinformationen prüfen. Keine vollständigen
Systemberichte oder Rohdaten ungeprüft öffentlich teilen.

Weitere Hinweise:
[Installation](docs/INSTALLATION.md),
[Benutzerhandbuch](docs/USER-GUIDE.md) und
[Sicherheit und Datenschutz](docs/SECURITY-AND-PRIVACY.md).
