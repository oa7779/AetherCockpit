# AETHER Intelligence bedienen

## Eine Systemfrage stellen

1. In der Navigation **AETHER Intelligence** öffnen.
2. Eine Frage zu den verfügbaren System- und Hardwareinformationen
   eingeben.
3. AETHER ermittelt den vorgesehenen Anfragekontext und startet den
   qualifizierten lokalen Modellpfad, wenn dieser eingerichtet und
   verfügbar ist.
4. Antwort, Evidence-Referenzen, Claimprüfung und Status lesen.

Nicht verfügbare Messungen bleiben als nicht verfügbar gekennzeichnet.
AETHER stellt daraus weder einen Messwert noch einen positiven Zustand her.

## Antwortstatus verstehen

- **Validiert:** Die angezeigten strukturierten Claims haben die
  implementierten Evidence- und Konsistenzprüfungen bestanden.
- **Abgelehnt:** Mindestens ein Claim oder die sichtbare Formulierung
  widerspricht dem geprüften Inhalt. Eine abgelehnte Antwort ist keine
  verlässliche Systemauskunft.
- **Fallback:** Der qualifizierte Modellpfad war nicht verfügbar oder nicht
  freigegeben; AETHER hat einen deterministischen Antwortpfad verwendet.
- **Nicht verfügbar:** Die Quelle lieferte keinen verwendbaren Wert.

Validierung ist keine allgemeine semantische Wahrheitsgarantie und bestätigt
nicht die Vollständigkeit sämtlicher Windows- oder Hardwarequellen.

## Laufdiagnose

Die technische Laufdiagnose kann Status, ausgewähltes Modell, Provider,
Promptversion, Tokenzähler, Validatorstatus und begrenzte Fehlerdetails
anzeigen. Sie speichert nicht die vollständige Modellantwort.

Bei einer Ablehnung den konkreten Validierungsstatus und die referenzierten
Claims prüfen. Diagnosedaten, Screenshots und Exportdateien vor dem Teilen
auf Benutzer-, Geräte- und Dateiinformationen kontrollieren.

## Verwaltungsaktionen

Eine Intelligence-Antwort ändert keine Systemeinstellungen. Für eine
Verwaltungsaufgabe den entsprechenden Funktionsbereich öffnen und die dort
angezeigte Ziel- und Wirkungsprüfung befolgen. Bestätigungen nicht
überspringen. Befehle in der freien Administratorkonsole werden unmittelbar
ausgeführt.

## Einrichtung und Grenzen

Das Modell muss zuvor separat eingerichtet und freigegeben sein. Der
normale Frageablauf lädt weder Runtime noch Gewichte herunter. Siehe
[Modellinstallation](INTELLIGENCE-MODEL-INSTALLATION.md),
[Modellstatus](INTELLIGENCE-MODEL-MATRIX.md) und
[Sicherheit und Datenschutz](SECURITY-AND-PRIVACY.md).
