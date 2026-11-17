# Drittanbieter, Lizenzen und Herkunft

Die [MIT-Lizenz von AETHER](LICENSE) gilt für eigenen Quellcode und
Dokumentation. Sie ersetzt keine Bedingungen mitgelieferter oder separat
installierter Komponenten. Deren Copyright-, Lizenz- und Herkunftshinweise
müssen bei Weitergabe erhalten bleiben.

## Mitgelieferte Messwerkzeuge

| Komponente | Verwendung | Lizenz und Unterlagen |
|---|---|---|
| smartmontools 7.5 r5714 x64 | `smartctl.exe` für SMART-Diagnose und Selbsttests; `drivedb.h` als Gerätedatenbank | GPL Version 2; [Original-Lizenz](tools/COPYING.txt), [Quellarchiv](tools/smartmontools-7.5-source.tar.gz) |
| PresentMon 2.6.0 x64 Console | `PresentMon.exe` für Frameintervalle eines ausgewählten Prozesses | MIT; [originaler Copyright- und Lizenztext](tools/PresentMon-LICENSE.txt) |

[Das Herkunftsmanifest](tools/provenance.json) nennt offizielle Downloadquellen,
Versionen und SHA256 für Binärdateien, smartmontools-Installer und Quellarchiv.
Das GPL-Quellarchiv bleibt zusammen mit Lizenz und Werkzeug im Paket.
Bei Änderungen oder Weiterverteilung die jeweiligen Quellcode- und
Lizenzpflichten prüfen; die AETHER-Lizenz hebt sie nicht auf.

## Hardwareprovider und .NET

**LibreHardwareMonitorLib 0.9.6** liefert Sensorzugriff unter MPL 2.0.
[Lizenztext](https://licenses.nuget.org/MPL-2.0)
und [Projektquellen](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
sind der Komponente zugeordnet. Änderungen an MPL-Dateien unterliegen
deren Offenlegungsbedingungen. Nicht verfügbare Sensoren werden durch
eine Lizenz oder Installation nicht automatisch zugänglich.

Transitive NuGet-Abhängigkeiten behalten die Lizenzen ihrer Paketmetadaten.
Der separate Verwaltungsserver referenziert Microsoft.AspNetCore.OpenApi,
Microsoft.Extensions.Hosting.WindowsServices und Microsoft.Identity.Client
(MSAL.NET 4.92.0, [MIT-Lizenz](https://licenses.nuget.org/MIT)); seine
Abhängigkeiten werden nicht als Serverinstallation mit dem Cockpit ausgeliefert.
Der optionale Clientdienst verwendet Microsoft.Extensions.Hosting.WindowsServices
und System.Security.Cryptography.ProtectedData. Seine Laufzeit liegt getrennt
unter `ManagedClient`; der Verwaltungsserver bleibt ein eigenes Paket.

Die selbstenthaltene Ausgabe enthält die .NET-/Windows-Desktop-Laufzeit
mit eigenen Lizenz- und Drittanbieterhinweisen. Diese gehören weiterhin
zum vollständigen Paket. Für konkrete Abhängigkeiten und Versionen gelten
Projekt-, NuGet- und Runtime-Metadaten der jeweiligen Ausgabe.

## Externes Npcap

Npcap ist optional für Paketmitschnitte und **nicht gebündelt**. Das Setup
kann nach Auswahl die [offizielle Downloadseite](https://npcap.com/#download)
öffnen; Installation und Lizenzprüfung erfolgen separat.

Die Npcap-Lizenz deckt nicht automatisch jede organisatorische Nutzung
oder Weiterverteilung ab. Insbesondere bei zentraler Softwareverteilung
und gebündelter Installation die geltenden Bedingungen und gegebenenfalls
OEM-/Redistributionsrechte prüfen. AETHER erteilt dafür keine Rechte
und installiert Npcap nicht unbeaufsichtigt.

## Weitere Voraussetzungen und Integrationen

Windows, Windows-Werkzeuge und WinGet sind Voraussetzungen mit ihren eigenen
Bedingungen. Optional gestartetes UniGetUI/WingetUI und Wireshark sind
separate Installationen, keine nachgebauten oder mitgelieferten Bestandteile.
Sysinternals und Windows ADK werden ebenfalls nicht gebündelt.
Die Werkzeugauswahl öffnet vorhandene Installationen oder die offiziellen
Microsoft-Bezugsseiten; Lizenzannahme und Installation erfolgen separat.
Der aktuelle Bildschirmschoner enthält keine Musikfunktion und verwendet
keine externen Musikdateien.

Originale Drittanbieter-Lizenztexte werden nicht übersetzt oder gekürzt.
Diese Übersicht beschreibt die Quellen und Werkzeuge des aktuellen Arbeitsbaums;
ältere Releasepakete behalten ihren damaligen Stand.
