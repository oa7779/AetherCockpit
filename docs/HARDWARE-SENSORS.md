# Hardware, Sensoren und Messwerte

AETHER verbindet Windows-Zähler und Messwerte des Hardwareproviders im
Cockpit. Verfügbare Sensoren und Eigenschaften hängen von Gerät, Firmware,
Treiber und Windows-Konfiguration ab.

## Werte und Quellen

Die Hardwareansicht gruppiert Messwerte nach Gerät und Sensor. Einheit,
Quelle und Verfügbarkeit sind für die Einordnung wichtig. Fehlende,
ungültige oder widersprüchliche Werte werden nicht als null angezeigt.

- **CPU:** Package- und Kernwerte hängen von den gemeldeten Kanälen des
  Hardwareproviders ab.
- **GPU:** Messwerte bleiben dem gemeldeten Gerät zugeordnet; Werte
  verschiedener Adapter werden nicht zu einer Identität zusammengefasst.
- **Arbeitsspeicher:** Working Sets von Prozessen und der Systemspeicher
  sind unterschiedliche Messgrößen und dürfen nicht addiert werden.
- **Datenträger:** Zähler physischer Geräte und Prozess-I/O beschreiben
  unterschiedliche Aktivitäten.
- **Netzwerk:** Adapterdurchsatz beschreibt beobachteten Verkehr, nicht
  die zugesicherte Kapazität des Internetanschlusses.

Diagramme zeigen die ausgewählten Messwerte und Einheiten. Eine dynamische
Skala dient der Darstellung und ist keine Gesundheitsbewertung.

## Messwerte und Grenzwerte

Vom Gerät gemeldete Warn- oder kritische Schwellenwerte sind Grenzen, keine
aktuellen Temperaturmessungen. Benutzerdefinierte Warnschwellen sind
ebenfalls Einstellungen und keine Sensorwerte. Eine hohe Temperatur allein
belegt kein thermisches Drosseln.

Administratorrechte garantieren keine Sensorverfügbarkeit. Windows-
Sicherheitsfunktionen oder die Geräteunterstützung können den Zugriff
begrenzen. Windows-Schutz nicht abschalten, um zusätzliche Messwerte
abzurufen.

## Aufzeichnungen und Berichte

Sensoraufzeichnung und Export sind getrennte optionale Aktionen.
Aufzeichnungen können Zeitstempel, Hardwarebezeichnungen und Messwerte
enthalten. Dateien vor dem Teilen prüfen; Export oder Schwärzung entfernt
nicht zwangsläufig alle identifizierenden Systemdetails.

## Weiterführend

- [Produktfunktionen](FEATURES.md)
- [Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md)
- [Drittanbieterhinweise](../THIRD-PARTY-NOTICES.md)
