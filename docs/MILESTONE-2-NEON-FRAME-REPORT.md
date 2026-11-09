# AETHER — Meilenstein 2: Neonrahmen

Stand: 9. November 2026.
**MEILENSTEIN 2 ABNAHMEBEREIT** für den beauftragten Rahmenumfang.
Keine RC-/Produktfreigabe. Stopp vor Meilenstein 3.

## 1. Ausgangsstand

Meilenstein 1 wurde vom Auftraggeber ausdrücklich abgenommen.
Sauberer Ausgangscommit:
`4ba04970ac2a9bc786a0fcd4b655a7e293e5436d`,
Branch `rc/grosse-entfesselung`, Remote `oa7779/AetherCockpit`.
Git-Status und Remote vor der Arbeit geprüft. Keine fremden lokalen
Änderungen ersetzt. Der Meilenstein bleibt vollständig lokal.

Quellen-/Ergebnisverträge, Datenquellen, administrative Bestätigungen,
Funktionsumfang und die bestehende Anordnung bleiben unverändert.
Keine Versionsanhebung, neue Pakete, Installation, Pushes, Tags, Releases
oder Merges. Keine Arbeit an Meilenstein 3.

## 2. Alle lokalen M2-Commits

| Commit | Inhalt |
|---|---|
| `d558463` | Isolierter Neonrahmen-Proof, zentrale Status-/Themeparameter, Tests und Alt-/Neu-Messmodus; noch keine View migriert |
| `45e2296` | Sammelbare Adorner, Timer-/Konturprüfung und wiederholte Lifecyclemessung |
| `0caeedc` | Exklusive Messphasen nach Stress, bevor eine Migration begann |
| `d1eaadf` | Hauptfenster-/Cockpitrahmen separat migriert |
| `ea5ed52` | Sidebar, Diagramme und Eingabestyles migriert |
| `cfa096c` | Schonerrahmen separat migriert, letzte produktive Altverweise entfernt |
| `2066ba9` | Äußeren Glow an tatsächlichen Fensterabstand angepasst, Unloadzwischenzustand berücksichtigt |
| `476e93a` | Echte synthetische DPI-Randtests und vollständige/beschränkte Echtzeitatmung |
| Abschlusscommit dieses Berichts | `docs(frame): close neon frame milestone` |

Der Abschlusscommit ist über `git log` auflösbar; sein eigener Hash wird
nicht durch Amend in sich selbst geschrieben. Jeder Migrationsschritt ist
getrennt rücknehmbar.

## 3. Komponentenentscheidung

[AetherNeonFrame](../src/SystemCockpit/Controls/AetherNeonFrame.cs) ist
ein zentrales Attached Behavior mit einem nicht interaktiven Adorner je
aktiviertem Element. Das erhält Border, Templates, Inhaltsbäume, Padding
und Layout. Ein neues Inhaltscontrol hätte mehr Migration und stärkere
Layoutkopplung erfordert. Blur am gesamten Panel wurde ausgeschlossen.

Der Proof wurde ohne produktive Einbindung gebaut, mit dem alten Rahmen
aus derselben Assembly in drei Runden verglichen und auf Unload geprüft.
Erst nach diesen Nachweisen begann die Migration.
Die [Spezifikation](AETHER-NEON-FRAME-SPECIFICATION.md) beschreibt Architektur,
API, Standards und Integrationsregeln.

## 4. Ersetzte FrameGlow-Pfade

| Pfad | Migration |
|---|---|
| MainWindow / Panelstyle | Statischer Kern und optionales Atmen über bestehendes MotionEnabled |
| MainWindow / Dashboard-, CPU-Kern-, Datenträgerkarten | Vollständige statische Kontur; Daten, rote Datenträgerfarbe und Kurven unverändert |
| SidebarWindow / BarNeonFrame | Eigenes AdornerLayer bleibt, Atmung folgt AETHER-/Windows-Freigabe |
| DiagramTemplates / Metric, History, Volume | Statische vollständige Konturen; Diagrammverträge und Messreihen unverändert |
| NeonInputs / Button, TextBox, TabItem | Statisch; vorhandene Hover-, Fokus- und Disabledzustände erhalten |
| SaverWindow / äußerer Rahmen | Gleiche Position und Größe, neue Kontur; vorhandene Motionfreigabe |
| ModernWindowAppearance | Neue Komponente deaktiviert statt alter Rahmenaktivierung |

Die unterschiedliche Enabled-/Visible-Semantik des alten Behaviors wurde
ausdrücklich in Enabled/BreathingEnabled übersetzt, nicht blind ersetzt.
Die Scannerbewegung bleibt ein unabhängiger Inhaltsindikator; nur ihre
bisherige Fenster-/Animationsfreigabe bezieht sich jetzt auf den neuen Rahmen.

## 5. Verbleibende alte Pfade

**Keine produktive FrameGlow-Einbindung mehr.** `git grep FrameGlow -- src`
findet ausschließlich die unveränderte Klassendatei selbst.
Sie bleibt als begrenzter technischer Altvergleich und Rückfallcode
erhalten. Keine UI-Einstellung oder automatisch erreichbare Altvariante.
Grundtests und Konsolidierungs-/M2-Messhost verwenden den alten Code
weiterhin ausdrücklich als Vergleich.

StrokeDashOffset und StrokeDashArray in der unabhängigen Board-Datenfluss-
und System-Schema-Illustration bleiben erhalten. Sie sind keine migrierten
Panelrahmen. Kein pauschales Entfernen aller Dash-/Effecttypen.

## 6. Visuelle Ebenen

- Scharfer kompletter Kern ohne Blur, Geometrie- oder Opacityanimation.
- Getrennte innere und äußere leere Glowkonturen mit BitmapCache.
- Hohler Clip schützt den Innenraum; keine Unschärfe auf Text oder Controls.
- Äußerer Blur passt sich dem vorhandenen Randabstand an.
- Nur die Glowintensität atmet; kein Dashoffset, keine gestrichelte
  Umlaufkontur, Rotation oder wandernder Lichtpunkt.

Der Standardzyklus beträgt nach Proof und Vergleich 14 Sekunden,
Intensität 0,86–1, weiches SineEase. Gewünschte WPF-Animationsrate zehn
Aktualisierungen pro Sekunde, kein globales UI-FPS-Limit.
Keine Größen-/Layoutanimation und kein vollständiges Ausblenden.

## 7. Statusmodell

Normal, Active, Analysis, Notice, Error, Admin, Offline, Disabled und
Unknown sind zentrale, validierte Zustände. Notice verwendet einen,
Error/Analysis zwei begrenzte langsame Zyklen. Offline, Disabled und
Unknown bleiben statisch; Kernkontur sichtbar.
Normal/Active/Admin dürfen bei erteilter Freigabe ruhig atmen.

Semantik wird nicht aus beliebigen Farben erschlossen. Bestehende rote
Datenträgerkarten werden dadurch nicht zu Fehleranzeigen. Neue fachliche
Statuszuordnung ist nicht Teil dieses Meilensteins. Die Galerie zeigt
alle Zustände mit expliziter Bezeichnung; Verbrauchercode muss Warnungen,
Fehler und fehlende Daten zusätzlich durch Text oder Symbol erklären.
Admin verändert keine UAC-/Bestätigungs- oder Rechteprüfung.

## 8. Themeverhalten

Retro-Neon Tag/Nacht und vorhandene Skins behalten ihre Hintergrund- und
Akzentressourcen. Normal übernimmt vorhandene BorderBrush; andere
Statusfarben kommen aus den neun zentralen NeonFrame-Ressourcen.
Themewechsel aktualisiert aktive Besitzer.

Windows Modern hell/dunkel bleibt neutral und funktional gleich.
Produktive Modernfenster deaktivieren die neuen Adorner. Der isolierte
Rahmen unterdrückt im Modernmodus ebenfalls Glow und Atmung.
Hoher Kontrast verwendet Systemfarben und statische Kontur; echte
Windows-Hochkontrastbedienung ist noch nicht laufzeitgeprüft.

## 9. ReducedMotion

BreathingEnabled, ReducedMotion, IsActive und IsVisible steuern ausschließlich
die Dekoration. AETHER-Animation aus, Windows ClientAreaAnimation aus,
hoher Kontrast oder ReducedMotion lassen keine Atmung laufen.
Die statische Kontur bleibt erhalten, soweit die Dekoration sichtbar ist.
Keine System- oder Benutzereinstellung für Tests verändert.

Kleine Eingabe-/Diagrammrahmen bleiben ausdrücklich statisch, statt
Hunderte separate Daueratmungen einzuführen. Das ist keine Abschaltung
von Messung, Anzeige oder unabhängigen Inhaltsanimationen.

## 10. Lifecycle

Loaded erzeugt höchstens einen Adorner; Unloaded und Enabled false entfernen
ihn, seine Animation und externe Eventhandler. Die Komponente beobachtet
Sichtbarkeit, Enabled, Fensterzustand, Systembewegung/Kontrast, Skin,
DPI und tatsächliche Geometrie-/Brushänderungen.

Minimiert, verborgen, Collapsed oder inaktiv: keine Atmung. Nach Rückkehr
werden die Gates neu bewertet. Abonnements bleiben nicht in statischen
Viewlisten hängen; entfernte Adorner sind per WeakReference sammelbar.
Keine Timer, Threads oder Hintergrundtasks in der Rahmenkomponente.
Kein Umsteuern von xBar, Gadgets, Alarmen, Tray, BoardFeed oder Aufzeichnung.

Nach jedem Migrationspaket folgte ein begrenzter Alt-/Neu-Lauf mit
300 tatsächlichen Reloadzyklen insgesamt. Der finale Lauf zeigte keine
Adornerakkumulation, wachsende Thread-/Handlefolge oder ungebremsten
Speicheranstieg. Das ist kein Langzeitnachweis.

## 11. Performance alt/neu

[Methodik, alle Serien, Rohwerte und Hashes](PERFORMANCE-BASELINE.md#meilenstein-2-neonrahmenvergleich).
Sechs gültige Serien mit je drei gepaarten Runden; Alt und Neu jeweils
derselbe Quell-/Hoststand. Identische 1280 × 720 DIP, DPI 150 Prozent,
Einlauf und angefordertes Messfenster. Tatsächliche Timerlatenz und
CPU-Normalisierung sind ausgewiesen.

Finaler Rendercode `2066ba9`, CPU eines Kerns:

| Phase | Median % | Min–Max % |
|---|---:|---:|
| Alter Laufrahmen | 15,1050 | 13,5389–34,3738 |
| Neuer statischer Rahmen | 2,8870 | 0,4558–3,4817 |
| Neuer atmender Rahmen | 2,0797 | 1,0069–4,1484 |

In allen gültigen Serien liegt der neue Median unter dem alten.
Auf 24 Prozessoren normalisierter finaler Median: alt 0,6294 Prozent, atmend
0,0867 Prozent. Kein allgemeiner Beschleunigungsfaktor für das Produkt.
Der historische M1-Altwert 22,42 Prozent wird nicht als identischer
Vorherlauf missbraucht. Working Set betrifft den gesamten Testhost.

Je finalem Messfenster: alter Rahmen 720 LayoutUpdated-Ereignisse,
neuer statisch/atmend null. Das ist kein exakter Measurezähler.
WPF-Kompositionsintervalle sind keine GPU-FPS.
Keine Energie-/Timer-/Treiber- oder Schutzsystemoptimierung am Host.

## 12. Ausgeführte Tests

Release-Lösungsbuild und vorhandener STA-Konsolenrunner, kein `dotnet test`.
Grundrunner ergänzt um 135 Rahmenassertions. Je Migrationspaket kombiniert:
synthetische Interface-, Schoner- und Brandingprüfungen, Modern-Smoke
und gepaarter Profilmodus.

Zusätzlich final: Ressourcen-/Sprachkompatibilität, Verwaltungsrunner,
Python-Lokalisierungstests, Zustands-/Themegalerie und Echtzeitatmung.
Synthetische Inhalte werden aus MainWindow herausgelöst; dessen produktive
Sensor-/Abfrageloops werden nicht gestartet. Kein `--dashboard-ui`-
Livebetrieb, keine Reparatur-/Update-/Capture-/Tenantaktion.
[Reproduktionsbefehle](TESTING.md#neonrahmen).

## 13. Testergebnisse

| Prüfung | Ergebnis |
|---|---|
| Letzter Release-Gesamtbuild | 0 Warnungen / 0 Fehler |
| Finaler STA-Grundlauf | 4.385 bestanden; gegenüber M1 135 neue Rahmenassertions |
| Final kombiniert mit Interface-/Schoner-/Branding-UI | 4.737 bestanden; Grundassertions enthalten, nicht nochmals addieren |
| Finaler Modern-/Neon-Smoke | 3.611 bestanden, 22 Testbilder |
| Ressourcen-/Sprachprüfung | 3.277 bestanden |
| Verwaltungsrunner | 384 bestanden, keine Windows-Aktion ausgeführt |
| Python-Lokalisierung | Drei Tests bestanden |
| Isolierter Rahmenlauf / Galerie | 135 Assertions bestanden, 21 Zustands-/Theme-/Skalierungsbilder |
| Vollständige Echtzeitbewegung | 22 Assertions bestanden: Standardzyklus und tatsächliches Ende von Notice/Error/Analysis |
| Quellreferenzen | Kein produktiver FrameGlow-Aufruf; neuer Rahmen ohne Dashshapes oder Timer |
| DPI-/Inhaltsgrenzen | 100/150/200 Prozent Root-DPI, zwei Größen, transparente äußere Pixelkanten und unveränderte Inhaltsgröße |
| Dokumentation / Belege | 73 relative Dateiziele geprüft, keine neuen Fehlziele; zehn bekannte historische Paketlinks unverändert; sechs Rohdatenhashes identisch |

Die 135 Rahmenassertions sind Teil des Grundlaufs. Keine Assertions
gelöscht oder abgeschwächt. Alte Rahmenassertions bleiben als technischer
Vergleich bestehen; xBar-/Schonerprüfungen wurden semantisch auf die
neue API umgestellt und Modernprüfungen um neue Adorner erweitert.

## 14. Visuelle Prüfbelege

Persistenter Sessionordner:
`%USERPROFILE%\.copilot\session-state\64a11d20-8d22-4d0e-9eca-a7e030f0d975\files`.

| Beleg | Inhalt |
|---|---|
| `m2-final-gallery` | Neun benannte Zustände je Bild; Nacht, Tag, Animation aus, Modern hell/dunkel, Vista Aero, Candy Gloss; Rasterziele 100/150/200 Prozent, 21 Bilder |
| `m2-final-modern` | 22 synthetische Modern-Hell-/Dunkelansichten einschließlich Cockpit, xBar, Einstellungen und Bestätigungsdialog |
| `m2-final-interface-*` | Bestehende Datenträgerkarten und Mainframe Tag/Nacht mit Testdaten |
| `m2-final-brand-*` | Branding, Cockpit und xBar einschließlich Schalterprüfung |
| `m2-final-saver.png` / `.4k.png` | Tatsächlicher synthetischer Vorschauhost und zusätzliche 4K-Layoutdarstellung |

Die Galerie-Rasterauflösung ist nicht mit einem physisch umgeschalteten
Monitor gleichzusetzen. Die separaten Root-DPI-Tests setzen die DPI
tatsächlich im isolierten Visualbaum. Kein Host-DPI-/Monitorwechsel.

Visuell kontrollierte Stichproben: alle Zustände gemeinsam bei Nacht,
Tag/hoher Rasterauflösung, Animation aus, Modern hell/dunkel, xBar sowie
Schoner und Cockpit. Text und Eingabefelder bleiben scharf, benachbarte
Panels getrennt, Kernkonturen vollständig, keine laufenden Dashreste in
den migrierten Rahmen. Bilder sind Prüfbelege, keine Benutzerabnahme
oder vollständiger Echtzeit-Flacker-/Langzeittest.

## 15. Regressionen und korrigierte Implementierungsbefunde

- Die erste xBar-Bildkontrolle zeigte abgeschnittenen äußeren Schein am
  knappen Fensterrand. Vor Abschluss zentral durch Randbegrenzung
  korrigiert, ohne Layoutänderung; neue Bilder und DPI-Pixeltests bestanden.
- Die neue Randprüfung musste den kurzzeitig getrennten Visualbaum vor
  Unload berücksichtigen. Ein gezielter Testlauf machte die ungültige
  Transformationsannahme sichtbar; explizite Verbindungsprüfung korrigiert
  sie. Keine Exceptionunterdrückung oder Catch-Erfolgsersatz.
- Der erste erweiterte Stressmessmodus überlagerte Folgephasen; vor der
  ersten Migration entdeckt, separat korrigiert und Messung neu ausgeführt.
  Diese Zwischenmessung bleibt als ungültig gekennzeichnet.

**Keine ungeklärte neue Regression in den final ausgeführten Tests.**
Der aus M1 bekannte einmalige Kontendatei-Zugriffsfehler bleibt ursächlich
offen; im finalen Verwaltungsrunner trat er nicht auf. Kein Retry,
keine Rechte-/Schutzänderung. Keine unbeabsichtigte Änderung an Navigation,
Messdaten, Funktionen oder Sicherheitsabfragen beobachtet.

## 16. Offene Grenzen

- Reale GPU-/Produktlast und viele reale Panels sind kein Ergebnis des
  isolierten Einzelrahmenvergleichs. Die M1-Produktmessmatrix bleibt offen.
- Reales Standby/Resume, physischer Monitorwechsel, Windows-Hochkontrast,
  weitere Treiber/Render-Tiers und Langzeitbetrieb sind nicht abgenommen.
- Keine echte Schonerinstallation, native SCR-Prozessabnahme oder neue
  Installerprüfung; synthetische Vorschau und bestehende Layouttests bestanden.
- Zustände sind technisch vorbereitet und in der Galerie getestet;
  keine neue fachliche Diagnose-/Providerintegration oder automatische
  gesund/fehlerhaft-Bewertung eingeführt.
- Ein AdornerLayer und ausreichender innerer Abstand sind Voraussetzung
  für neue Verbraucher. Der Rahmen verändert dafür keine Inhalte.

## 17. Rollbackpfad

Der unveränderte alte FrameGlow und Ausgangscommit bleiben erhalten.
Migrationscommits für Hauptfenster, weitere Styles und Schoner sind einzeln
rücknehmbar; bei tatsächlichem Rückbau abhängige API-/Teständerungen
zusammen berücksichtigen. Kein automatischer Laufzeitfallback und kein
neues dauerhaftes Alt-/Neu-Feature. Kein Reset oder Revert ausgeführt.

## 18. Auswirkungen auf Meilenstein 3

Die zentrale Rahmenkomponente und dokumentierte Parameter stehen zur
Verfügung. Keine Hardware-Informationszentrale, kein Wiki, keine
Navigationserneuerung, Logosammlung, neue Verwaltungsfunktion oder
Server-/Graphänderung begonnen. Datenverträge aus M1 bleiben kompatibel.
Meilenstein 3 benötigt eine neue Freigabe.

## 19. Abnahmestatus

**MEILENSTEIN 2 ABNAHMEBEREIT.**

Zentrale Komponente, ruhige vollständige Konturen, drei getrennte Ebenen,
abschaltbare/langsame Atmung, ReducedMotion, neun Zustände, Tag/Nacht,
neutrales Modern, alle beauftragten Rahmeneinbindungen und Lifecycletests
sind umgesetzt. Unabhängige Animationen und alle Inhalte bleiben erhalten.
Build, relevante Tests, mehrere Vergleichsläufe und diagnostische
Bildbelege bestehen; die Spezifikation und Messgrenzen sind dokumentiert.
In den begrenzten Tests kein ungebremstes Ressourcenwachstum beobachtet.

Die menschliche visuelle Abnahme steht aus. Kein RC, Release oder neue
Version. **Hier Stopp, kein Meilenstein 3 ohne neuen Auftrag.**
