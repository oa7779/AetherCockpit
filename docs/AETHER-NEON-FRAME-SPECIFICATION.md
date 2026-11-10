# AETHER Neon Frame — Spezifikation

## Designziel und Umfang

Eine vollständige, geometrisch stabile Neonkontur ersetzt die umlaufenden
Rahmen-Laufspuren. Retro-Neon bleibt kräftig und maximalistisch.
Inhalt, Navigation, Panelanordnung, Diagramme und Datenquellen bleiben
unverändert. Kein Ladeindikator, keine wandernden Striche, keine Rotation
und keine Bewegung der Kernkontur.

Die zentrale Implementierung ist
[AetherNeonFrame](../src/SystemCockpit/Controls/AetherNeonFrame.cs).
[Messungen](PERFORMANCE-BASELINE.md#meilenstein-2-neonrahmenvergleich)
und [Abschlussbericht](MILESTONE-2-NEON-FRAME-REPORT.md) dokumentieren
Nachweise und Grenzen. Es gibt keinen neuen Benutzerschalter für alte Rahmen.

## Technische Architektur

Attached Behavior mit genau einem nicht interaktiven Adorner je aktiviertem
Element. Vorhandene Border, Styles und Templates bleiben Besitzer ihrer
Inhalte und Geometrie; kein neuer Inhaltswrapper.

| Variante | Entscheidung |
|---|---|
| Eigenes Control / Decorator | Würde vorhandene Border-/Templateverträge und Inhaltsbäume stärker verändern; nicht benötigt |
| Reiner Style / Effect am Panel | Könnte Inhalt mit weichzeichnen; kein eigener sauberer Lifecycle |
| Attached Behavior + Adorner | Bestehende Einbindungsstellen direkt migrierbar, keine Layoutverschiebung, unabhängig testbar |
| Individuelle Viewlösungen | Nicht verwendet; Palette und Bewegungsregeln liegen zentral |

Ein aktives Element benötigt einen vorhandenen AdornerLayer. Ohne Layer
kann WPF keinen Adorner anzeigen; die migrierten Hosts besitzen ihn
explizit oder über ihr bestehendes Windowtemplate. Die Komponente erzeugt
keinen versteckten Ersatzhost und startet keine Timer.

## Visuelle Ebenen

1. **Kernkontur:** vollständiger Border, scharf, ohne Effect, unveränderte
   Geometrie und konstante Deckkraft während der Atmung.
2. **Innerer Glow:** separate leere Borderkontur mit kleinem Blur und
   niedrigerer Deckkraft. Der gemeinsame hohle Clip hält den Schein vom
   Innenraum fern; nur der bis zu drei DIP breite Randbereich bleibt offen.
3. **Äußerer Glow:** eigene leere Kontur mit weichem Blur. Die beiden
   Glowkonturen besitzen BitmapCaches; nicht das Panel oder dessen Inhalt.

Das Opacity-Signal betrifft ausschließlich die gemeinsame Glowebene.
Keine Bitmap wird pro Animationstick im Anwendungscode erzeugt.
Palette, Geometrie und Cache können sich bei tatsächlichem Größen-,
DPI-, Theme- oder Parameterwechsel neu aufbauen.

Der effektive Blur wird zentral auf den vorhandenen Abstand zum Rand des
AdornerLayers begrenzt. Ein knapp eingefasstes xBar-/Fenster bekommt einen
engeren Schein statt abgeschnittenem Glow oder verschobenem Inhalt.
Ecken folgen dem Border oder einer expliziten CornerRadius-Angabe.
Neue Verbraucher müssen Text und Eingaben außerhalb des inneren
Randbereichs platzieren; das Behavior verändert dafür kein Padding.

Ein zusätzlicher wandernder Energieimpuls ist bewusst nicht implementiert.
Die begrenzte Intensitätsänderung reicht für die vorgesehenen Zustände.

## Statusmodell

`NeonFrameStatus` wird ausdrücklich gesetzt, nie aus einer beliebigen Farbe
oder einem fehlenden Sensorwert erschlossen. Normal ist der dekorative
Grundzustand und kein Beweis eines gesunden Rechners.

| Status | Kontur und Glow | Bewegung bei erteilter Freigabe |
|---|---|---|
| Normal | Vorhandene BorderBrush bzw. Cyan-Themeakzent | Ruhiges Atmen |
| Active | Heller technischer Akzent, leicht erhöhter Glow | Ruhiges Atmen |
| Analysis | Heller Analyseakzent, leicht erhöhter Glow | Zwei langsame Zyklen, dann statisch |
| Notice | Gelb/Orange | Ein langsamer Zyklus, dann statisch |
| Error | Rot | Zwei langsame Zyklen, dann statisch |
| Admin | Magenta/Violett | Ruhiges Atmen; kein Ersatz für Bestätigungen oder UAC |
| Offline | Entsättigtes Graublau, reduzierter Glow | Statisch |
| Disabled | Deutlich reduzierter, sichtbarer Rahmen | Statisch |
| Unknown | Neutraler Rahmen | Statisch, keine erfundene Bewertung |

Notice, Error, Offline und andere fachliche Bewertungen benötigen zusätzlich
sichtbaren Text, Symbol oder Statusbezeichnung im betreffenden Inhalt.
Der Adorner verändert keine Texte und ersetzt keine Accessibilitysemantik.
Die Zustandsgalerie zeigt alle neun Zustände mit expliziter Bezeichnung.
Die Migration deutet bestehende farbige Messkarten nicht in neue
Fehler-/Gesundheitszustände um.

## Themeintegration

Die zentralen Brushes `NeonFrameNormal`, `NeonFrameActive`,
`NeonFrameAnalysis`, `NeonFrameNotice`, `NeonFrameError`, `NeonFrameAdmin`,
`NeonFrameOffline`, `NeonFrameDisabled`, `NeonFrameUnknown` stehen im
[Basisdictionary](../src/SystemCockpit/Controls/NeonGlass.xaml).

- Normal übernimmt die vorhandene BorderBrush, auch bei den bestehenden
  Skins und roten Datenträgerkarten. Keine neue View-Farbsammlung.
- Retro-Neon Tag/Nacht behalten ihre bisherigen Hintergründe.
- Vista Aero und Candy Gloss behalten ihre bestehenden Ressourcen.
- Skinwechsel löst eine zentrale Aktualisierung aktiver Adorner aus.
- Windows Modern bleibt neutral: ModernWindowAppearance deaktiviert die
  Rahmeneinbindungen. Die isolierte Komponente unterdrückt im Modernmodus
  zusätzlich Glow und Bewegung und verwendet ModernBorder.
- Hoher Kontrast verwendet SystemColors.WindowTextBrush, ohne Glow und
  Bewegung. Die echte Windows-Hochkontrasteinstellung wurde nicht verändert.

## Öffentliche Parameter

Alle Maße sind DIP. Numerische Werte müssen endlich sein. Nicht definierte
Status-/Themewerte und ungültige Radien/Dauern werden vom DP-Vertrag
abgewiesen. Farbübersteuerungen bleiben zentral verwendbar; die Migration
benötigt keine individuellen neuen Farben.

| Attached Property | Standard / erlaubter Bereich | Bedeutung |
|---|---|---|
| Enabled | false | Behavior installieren / vollständig entfernen |
| Status | Normal | Expliziter semantischer Zustand |
| ThemeMode | Auto; außerdem Neon, Modern | Auto folgt SkinCatalog; globales Modern wird nicht übersteuert |
| AccentColor | null | Optionaler gemeinsamer Farbakzent |
| CoreColor | null | Optionale Kernfarbe |
| CoreThickness | 1,25; 0,5–3 | Scharfe Konturbreite |
| OuterGlowColor | null | Optionale äußere Farbe |
| OuterGlowRadius | 9; 0–16 | Obergrenze des äußeren Blur, zusätzlich randbegrenzt |
| OuterGlowOpacity | 0,55; 0–1 | Äußere Basisdeckkraft vor Statusfaktor |
| InnerGlowColor | null | Optionale innere Farbe |
| InnerGlowRadius | 3; 0–6 | Obergrenze des inneren Blur |
| InnerGlowOpacity | 0,28; 0–1 | Innere Basisdeckkraft |
| CornerRadius | null | Bordergeometrie übernehmen; sonst 12 DIP |
| BreathingEnabled | true, vererbbar | Bewegungsfreigabe, nicht Sichtbarkeit des statischen Rahmens |
| BreathingMinimum | 0,86; 0,65–1 | Untere Glowintensität |
| BreathingMaximum | 1; 0,65–1 | Obere Glowintensität |
| BreathingDuration | 14 Sekunden; 8–120 Sekunden | Vollständiger Hin-/Rückzyklus |
| ReducedMotion | false, vererbbar | Atmung vollständig unterdrücken |
| IsActive | true, vererbbar | Bewegung nur für aktiven Kontext |
| IsVisible | true, vererbbar | Dekoration sichtbar; verändert nicht den Inhalt |
| MotionActive | schreibgeschützt | Ob aktuell ein Atmungsclock angewendet ist |

Bei vertauschten Intensitätsgrenzen werden Minimum/Maximum geordnet.
Die Kernkontur bleibt davon unabhängig sichtbar. `EnergyPulseEnabled`
ist nicht vorhanden, weil kein separater Energieimpuls implementiert wird.

**Wichtige Migration:** Beim alten FrameGlow bedeutete Enabled Bewegung und
Visible den statischen Rahmen. Beim neuen Behavior aktiviert Enabled die
Komponente; Bewegung wird ausdrücklich über BreathingEnabled gesteuert.
Beide Namen dürfen nicht blind eins zu eins ersetzt werden.

```xml
<Border controls:AetherNeonFrame.Enabled="True"
        controls:AetherNeonFrame.BreathingEnabled="{Binding MotionEnabled}"
        BorderBrush="{DynamicResource GlassRim}"
        CornerRadius="12">
    <!-- Vorhandener Panelinhalt -->
</Border>
```

Kleine Diagramm-, Button-, Tab- und Eingaberahmen setzen BreathingEnabled
ausdrücklich false. Shell, große Panels, xBar und Schoner nutzen die
bestehende AETHER-Bewegungsfreigabe.

## Atmung und Ressourcen

Der im Proof getestete Standard ist ein 14-Sekunden-Zyklus von 0,86 bis 1
und zurück, weich über SineEase. Kein vollständiges Erlöschen, keine
Layoutanimation. WPF erhält eine gewünschte Animationsrate von zehn
Aktualisierungen pro Sekunde für diese Opacityanimation; das ist keine
globale UI-/GPU-FPS-Garantie. Andere Animationen bleiben unabhängig.

Notice endet nach einem Zyklus, Error/Analysis nach zwei. Completed entfernt
den Clock und stellt statisches Glow wieder her. Theme-/Statuswechsel
ersetzen den bisherigen Clock mit SnapshotAndReplace, statt ihn zu stapeln.

## Lifecycle

| Ereignis | Verhalten |
|---|---|
| Loaded | Höchstens einen Adorner im vorhandenen Layer erzeugen |
| Unloaded / Enabled false | Clock entfernen, Layer bereinigen, externe Handler lösen |
| Besitzer verborgen / Collapsed / IsVisible false | Bewegung stoppen, Inhalt nicht verändern |
| Fenster minimiert / verborgen | Bewegung stoppen; bei Rückkehr neu prüfen |
| IsActive false / IsEnabled false | Bewegung stoppen |
| AETHER-Animation aus / ReducedMotion | Statischer Rahmen ohne Clock |
| Windows ClientAreaAnimation aus / hoher Kontrast | Keine Atmung |
| Themewechsel | Palette und Bewegung neu bewerten, keine parallelen Adorner |
| Größe / BorderBrush / CornerRadius | Nur tatsächliche Änderungen nachziehen |
| DPI-Wechsel | Bounds und Glowgrenzen neu berechnen |
| Wechsel des Hosts | Unload/Reload löst alte Windowhandler und bindet den neuen Besitzer |

Externe Abonnements: Windows-Animations-/Kontrastparameter, zentraler
Skinwechsel sowie zuständiges Fenster. Diese werden beim Entfernen gelöst;
keine dauerhafte statische Viewliste. Vorübergehend getrennte Visualbäume
im Unloadpfad benötigen keine Transformationsannahme.

Keine eigenen DispatcherTimer, Threads, Sockets, Sensoren oder Hintergrundtasks.
xBar, Gadgets, Alarme, Tray, BoardFeed und Aufzeichnung werden nicht wegen
eines unsichtbaren Rahmens abgeschaltet. Reales Standby/Resume und physischer
Monitorwechsel sind noch separat zu prüfen.

## Tests und Migration

[NeonFrameTests](../tests/SystemCockpit.Tests/NeonFrameTests.cs) prüft
Statuswerte, Defaults, Paletten, statische Darstellung, ReducedMotion,
Fenster-/Element-Sichtbarkeit, Minimierung, Themewechsel, Randbegrenzung,
20 Unload/Reload-Zyklen, entfernte Adorner und deren GC-Erreichbarkeit.
Die GC-Prüfung ist eine Referenzprüfung, kein geschönter Speicherbenchmark.

Die Adorner-Sichtbarkeit folgt der effektiven `IsVisible`-Eigenschaft ihres
Besitzers und dem eigenen Sichtbarkeitsschalter. Da die gemeinsame Adornerebene
außerhalb eingeklappter Inhalte liegt, reicht das Stoppen der Animation nicht:
Auch der statische Rahmen muss unsichtbar werden. Ein Pixeltest prüft
wiederholtes Auf-/Zuklappen und versteckte Vorfahren; die Interfaceprüfung
kontrolliert alle sechs tatsächlichen Navigationsgruppen.

Synthetische Root-DPI 100/150/200 Prozent werden in zwei Größen tatsächlich
gesetzt. Tests prüfen unveränderte Inhaltsgröße und transparente äußerste
Bildpixel, also keine abgeschnittenen Konturen. Die zusätzliche Galerie
rendert neun Zustände in sieben Modi mit drei Rasterauflösungen.
Ein separater Echtzeittest prüft einen vollständigen Standardzyklus und
das tatsächliche Ende der begrenzten Zustandsclocks.
[Befehle und Nebenwirkungen](TESTING.md#neonrahmen).

Migration getrennt: Hauptfenster, Sidebar/Diagramme/Eingaben, Schoner.
Nach jedem Paket Build, synthetische Interface-/Branding-/Schonertests,
Modern-Smoke und drei gepaarte Messrunden.

FrameGlow bleibt unverändert ausschließlich für Altvergleich und
technischen Rückfall im Quellcode. Kein produktiver Aufruf bleibt;
die Quellreferenzprüfung muss das vor einer späteren Entfernung bestätigen.
Lüfter, Board-Datenflüsse, Landschaft, Instrumentwechsel, Brandingpuls,
Lichtreflex, Diagramme und CRT-Effekte sind nicht Teil dieser Migration.

## Bekannte Grenzen

Synthetische Teilmessungen sind keine Produkt-/GPU-/Langzeitfreigabe.
BitmapCache und Blur hängen auch von WPF-Tier, Treiber und DPI ab.
Echte Multi-Monitor-/Resume-/Hochkontrastbedienung und Langzeitstabilität
sind noch nicht abgenommen. Die Komponente erfordert einen AdornerLayer
und ausreichenden inneren Inhaltsabstand; sie ändert kein bestehendes Layout.
Fachliche Statuszuordnung bleibt Aufgabe des jeweiligen Verbrauchers.
