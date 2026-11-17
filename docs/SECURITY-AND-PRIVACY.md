# Sicherheit und Datenschutz

AETHER ist für die lokale Nutzung ausgelegt. Hardware- und Windows-Abfragen
laufen im Cockpitprozess. Die optionale lokale Intelligence-Inferenz nutzt
den lokalen Ollama-Provider. Ein separater, ausdrücklich aktivierter
Client-/Serverablauf ist davon zu unterscheiden.

## Lokale KI und Verwaltungsgrenzen

- Die qualifizierte AETHER-Intelligence verwendet Phi 4 Mini über Ollama auf
  dem lokalen System. Der Modellpfad ist keine Cloud-Inferenz.
- Systemfragen werden anhand ausgewählter Messwerte und Evidence geprüft.
  Modelltext ist nicht selbst ein Messwert oder Beleg.
- Der normale Frageablauf gewährt dem Modell keine Shell-, Datei- oder
  Verwaltungsbefugnis und führt keine Systemänderung aus.
- Unterstützte Verwaltungsabläufe zeigen Ziel und Wirkung und verlangen
  eine Bestätigung. Die freie Administratorkonsole führt eingegebene
  Befehle unmittelbar aus.
- Der Validator prüft strukturierte Claims und ihre Belegverweise. Er ist
  keine allgemeine semantische Wahrheitsgarantie.

Die separate Modellinstallation zeigt die Lizenz und verlangt eine
ausdrückliche Annahme. Der Modellstore und die Runtime liegen außerhalb des
Portable-Pakets; Details zur Einrichtung enthält die
[Installationsanleitung](INTELLIGENCE-MODEL-INSTALLATION.md).

## Verarbeitete und gespeicherte Informationen

Systemansichten können Hardwarebezeichnungen, Betriebssystemstatus,
Prozessinformationen, Netzwerkinformationen, Ereignisse und Messwerte
anzeigen. Lokale Berichte und Diagnosen können zusätzlich Benutzer-,
Geräte- oder Dateipfade enthalten. Vor einer Weitergabe jeden Export,
Screenshot und Diagnosedatensatz prüfen.

Intelligence-Laufdiagnosen dienen der technischen Nachvollziehbarkeit und
enthalten Lauf- und Validierungsmetadaten. Eine vollständige Modellantwort
wird nicht als Laufdiagnose gespeichert. Lokale Einstellungen, Berichte
und Modellgewichte bleiben von der Programmdatei getrennt.

Der optionale Client-/Serverbetrieb überträgt Daten nur in seinem eigenen
Bedienablauf. Gerätefreigabe und Übertragung benötigen die dort angezeigte
Zustimmung. Er ist von den lokalen Cockpit- und Intelligence-Abläufen
getrennt.

## Zuverlässigkeit und Grenzen

- Messwerte und Sensoren hängen von Gerät, Treiber und Windows ab.
- Nicht verfügbare Daten bleiben als nicht verfügbar gekennzeichnet.
- Ein Grenzwert ist kein gemessener Wert.
- Ereignisse und Inventardaten sind Hinweise, keine alleinige
  Ursachendiagnose.
- Erhöhte Windows-Rechte können für einzelne Funktionen erforderlich sein.
  Sie erweitern nicht die Befugnisse des Intelligence-Modells.
- Beta-Software ersetzt weder Windows-Sicherheitsmaßnahmen noch
  Administrations- und Datensicherungsvorgaben.

## Sichere Nutzung

- AETHER und Portable-Pakete ausschließlich aus einer vertrauenswürdigen
  Bezugsquelle laden und Prüfsummen vergleichen.
- Keine Warnungen zu unsignierter oder nicht vertrauenswürdiger Software
  umgehen.
- Vor bestätigten Änderungen Ziel und Wirkung prüfen.
- Berichte, Screenshots und Systemdetails vor öffentlicher Weitergabe
  bereinigen.
- Drittanbieterkomponenten und deren Lizenzbedingungen in
  [Drittanbieterhinweisen](../THIRD-PARTY-NOTICES.md) beachten.
