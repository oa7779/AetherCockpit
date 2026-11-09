# AETHER — Releaseabhängige Sprachfreigabe

## Ziel und aktueller Stand

RC1 soll Deutsch als vollständig geprüfte Releasesprache ausweisen.
Die Mehrsprachigkeitsarchitektur und beide englischen RESX-Ressourcen
bleiben bestehen. Englisch ist **NICHT VOLLSTÄNDIG FREIGEGEBEN** und
bleibt nach der bestehenden Produktlogik nicht auswählbar.

Dies ist die technische Strategie, keine deutsche oder englische
Laufzeitfreigabe und keine RC-Version. In Meilenstein 1 wurden weder
CI-Gate noch Sprachwahl oder Ressourcenbestand geändert.

## Bestehender Mechanismus

Der [Windows-Workflow](../.github/workflows/windows-build.yml) führt bei
`-rc.` im Versionsprofil weiterhin
`desktop_localization_audit.py --require-complete` aus. Dieses Gate prüft
die heutige Englisch-Freigabe. Ein bloßes Entfernen der Bedingung, Ersetzen
durch einen immer erfolgreichen Befehl oder Umbenennen einer RC-Version
würde die Freigabe umgehen und ist nicht Teil dieser Strategie.

Die vorhandenen Ressourcen-/Kompatibilitäts-Assertions und der synthetische
deutsche Modern-/Neon-Smoke bleiben unabhängig davon erforderlich.
Siehe [Tests](TESTING.md#freigabeschranke-englisch).

## Vorgesehener expliziter Releasevertrag

Vor der ersten RC-Erzeugung wird ein maschinenlesbarer Releasevertrag
eingeführt und zusammen mit dem Quellstand versioniert. Er deklariert:

- Schemaversion und Bezug zum tatsächlichen Versionsprofil;
- `releaseLanguages`: Liste der tatsächlich freigegebenen Sprachen,
  für RC1 geplant `de`, später ohne Architekturbruch erweiterbar;
- je unterstützter Sprache Status `released` oder `experimental`;
- stabile Test-/Reviewnachweise je deklarierter Sprache;
- Konfliktprüfung zwischen Releasevertrag, auswählbaren Produktsprachen
  und den Paket-/Releaseangaben.

Deutsch ist dabei ein Listeneintrag, keine dauerhafte Sonderbehandlung
`if German then skip`. Leere Listen, unbekannte Sprachcodes,
widersprüchliche Status, fehlende Nachweise und nicht deklarierte
auswählbare Releasesprachen lassen das Gate fehlschlagen.

## CI-Entwurf

1. Versionsprofil und Releasevertrag lesen und gegeneinander validieren.
2. Ressourcen laden, doppelte/leere Schlüssel, Schlüsselparität,
   Formatargumente und Rückfallverhalten für **alle** vorhandenen Sprachen
   prüfen. Experimentell bedeutet nicht syntaktisch ungeprüft.
3. Für **jede** deklarierte Releasesprache Vollständigkeit des Text- und
   Datenflussaudits, technische Quellen-/Unit-Texte und dokumentierten
   Reviewstatus verlangen. Der bisherige globale Auditstatus muss dafür
   zunächst explizit sprachbezogen modelliert werden; nicht einfach
   fehlende Englischbefunde löschen.
4. Deutsche bzw. spätere englische UI-Smokes aus demselben Quellstand
   ausführen; Retro-Neon und Windows Modern, Dialoge, Bestätigungen,
   dynamische Fehlermeldungen, Kataloge und Nicht-verfügbar-Zustände prüfen.
5. Für deklarierte englische Freigabe bleibt die vollständige bestehende
   Englischschranke verpflichtend. Ihre Kriterien werden in den
   sprachbewussten Validator übernommen und mit Negativtests gesichert.
6. Release-/Paketangaben ausschließlich aus dem validierten Sprachumfang
   ableiten. Kein RC darf eine ungeprüfte Sprache als freigegeben bewerben.

Der Wechsel des produktiven Workflow-Gates erfolgt erst atomar mit dem
getesteten Nachfolger und dessen vollständigen Kriterien. Bis dahin bleibt
das heutige Gate aktiv, auch wenn es einen geplanten RC1-DE noch blockiert.
In Meilenstein 1 wird kein RC erzeugt, daher ist kein Gate-Bypass nötig.

## Obligatorische Validatorfälle vor Umstellung

| Deklaration / Befund | Erwartung |
|---|---|
| RC-DE, Deutsch vollständig, Englisch experimental mit offenen englischen Reviewstellen | Sprachfreigabe nur für Deutsch möglich; globale Syntax-/Paritätsprüfungen bleiben verpflichtend |
| RC-DE mit offener deutscher UI-/Datenflussstelle | Fehler |
| RC-DE mit leeren/deformierten englischen Ressourcen | Fehler |
| RC-DE/EN mit englischen offenen Stellen oder fehlender englischer Laufzeitprüfung | Fehler |
| RC-DE/EN mit allen Nachweisen für beide Sprachen | Sprachgate kann bestehen; ersetzt keine sonstige RC-Abnahme |
| Unbekannte Sprache / leerer Umfang / Versionskonflikt | Fehler |
| Englisch produktiv auswählbar, aber nur experimentell deklariert | Fehler |
| Nur Schema-/Ressourcenprüfung ohne Reviewnachweis | Keine vollständige Freigabe |

Diese Tabelle ist ein Implementierungsplan, keine Behauptung bereits
ausgeführter neuer CI-Gates. Vor Umstellung werden die Fälle als
automatisierte Negativ-/Positivtests ergänzt.

## Offene Freigabearbeit

Deutsche Realbedienung und dynamische Datenpfade vollständig abnehmen;
sprachbezogenen Auditvertrag und Validator implementieren; Workflow atomar
umstellen und auf einer freigegebenen CI ausführen. Für spätere englische
Freigabe zusätzlich offene Übersetzungs-/Katalog-/Datenflussstellen
schließen und beide Designs real testen. Keine englischen Ressourcen
entfernen und keine Hardcodierung in den deutschen UI-Pfad einführen.
