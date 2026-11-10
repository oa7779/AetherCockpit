# Hardware Intelligence — Quellen und Abdeckung

Die folgende Liste beschreibt implementierte Lesepfade, nicht die Garantie,
dass jeder Rechner diese Eigenschaften liefert. ProviderReported ist kein
unabhängiger Mess- oder Herstellerabgleich. Null ist ein Wert; leere oder
fehlende Eigenschaften sind nicht verfügbar. Numerische Codes bleiben als
solche beschriftet, solange keine belegte Übersetzung integriert ist.

## Integrierte Klassen

| Bereich | Quelle | Ausgelesene Informationen |
|---|---|---|
| System | Win32_ComputerSystem, Win32_OperatingSystem | Hersteller, Modell, Familie, Typ, Architektur, RAM, Sockel/logische Prozessoren, Hypervisor, Edition, Version, Build, verfügbarer/virtueller Speicher |
| Systemübersicht | eigene gezielte CIM-Übersichtsabfragen | CPU-, Grafik-, Storage-, Netzwerk- und Monitorinstanzen, BIOS, TPM und DeviceGuard |
| CPU | Win32_Processor | Identität, Architektur-/Familiencodes, Stepping/Revision, ProcessorId, Sockel, Kerne/Threads, gemeldete Takte, Last, L2/L3, Firmwarevirtualisierung/SLAT |
| CPU | CPUID, .NET Intrinsics | Vendor, Rohsignatur, berechnete Display Family/Model/Stepping, SSE/AVX/AES/SHA/Virtualisierungs-/Securitybits; separat OS-nutzbare AVX-Funktionen |
| CPU | GetLogicalProcessorInformationEx, Processor-Group-APIs | Sockel, Kerne, SMT-/EfficiencyClass, Gruppen-/Affinitymasken, NUMA-Knoten, Cachelevel/-typ/-größe/-line |
| CPU | Windows-Registry CentralProcessor/Update Revision | rohe Microcode-Bytes pro Windows-Prozessoreintrag; keine undokumentierte Herstellerinterpretation oder Sockelzuordnung |
| GPU | Win32_VideoController und PnP | Modell, Windows-Herstellerangabe, Geräteidentität, Treiberversion/-datum, INF, Status |
| GPU | DXGI Adapter1/Adapter3 | unabhängige Adapterinstanzen, Vendor/Device/Subsystem/Revision, dedizierter/shared Speicher; lokale/nichtlokale Budgets und Nutzung des aktuellen Prozesses, Node 0 |
| GPU | Direct3D 12 je DXGI-Adapter | höchstes getestetes Featurelevel, Raytracing-Tier, UMA/CacheCoherentUMA für Node 0; keine Render-/Computeaufträge |
| NPU | Win32_PnPEntity | Windows-Geräte der Klassen ComputeAccelerator/NeuralProcessor, Identität, Name, Treiber und Status; keine TOPS |
| Mainboard | Win32_BaseBoard, Win32_SystemSlot | Hersteller, Produkt, Version, geschützte Seriennummer; gemeldete Slots und Bus-/Device-/Function-Codes |
| BIOS/UEFI | Win32_BIOS, Get-ComputerInfo.BiosFirmwareType, Confirm-SecureBootUEFI | Firmwareversion/-datum, SMBIOS-Version, Modus und Secure Boot |
| Chipsatz | PCI-Systemgeräte von Windows | echte Geräteidentitäten, Windows-Namen und Treiber; keine daraus abgeleitete Chipsatzfamilie |
| RAM | Win32_PhysicalMemory | Modulslot, Bank, Hersteller, Part Number, Seriennummer, Größe, SMBIOS-/Formfaktorcodes, Geschwindigkeiten, Breiten, Spannungen, rohe Rank-Attribute |
| Storage | MSFT_Disk/PhysicalDisk/Partition/Volume | Identität, Firmware, Bus, Kapazität, Sektorgrößen, OS-Disknummer, Partitionen, Volume/Dateisystem und Status |
| Storage | Get-StorageReliabilityCounter je PhysicalDisk | soweit gemeldet Temperatur, PowerOnHours, Wear, Read-/Write-Fehler, StartStop-/LoadUnload-Zähler |
| Storage | vorhandenes smartctl, lesend -x -j -d auto | SMART-Gesamtstatus, Temperatur, Laufzeit, NVMe-Healthzähler und ATA-Attribute; dokumentierte Disknummer plus Number/UniqueId/Path-Nachprüfung, kein Modellnamensjoin |
| PCI/PCIe | Win32_PnPEntity, Get-PnpDeviceProperty | Geräte-/Subsystem-/Revisionsfelder aus expliziten IDs, Bus, rohe Busadresse, daraus validierte Device/Function, Treiber, aktuelle/maximale Linkbreite und Linkgeschwindigkeitscodes |
| Netzwerk | Win32_NetworkAdapter und .NET NetworkInterface | physisch/virtuell laut Windows, Typ, MAC, IP/Gateway/DNS, DHCP, MTU, Status, Linkrate, kumulierte Bytes und Fehler |
| Netzwerk | AETHER-Zählerdifferenz je Adapter | Rx/Tx als bit/s über das tatsächliche Intervall zwischen angeforderten Abfragen; erste/fehlende/rückläufige Probe benötigt Baseline |
| WLAN | MSNdis_80211_ReceivedSignalStrength | Legacy-NDIS-Signal, wenn dieser Provider verfügbar ist |
| WLAN | Native Wi-Fi | Schnittstellenstatus, PHY/Standard, Signalqualität in Prozent, Linkraten, Kanal; Frequenz/Band nur bei eindeutigem Match der zwischengespeicherten BSS-Daten, kein aktiver Scan |
| Bluetooth | Win32_PnPEntity | Geräteklasse, Herstellerangabe, Identität, Treiber, Status |
| Display | WmiMonitorID/BasicDisplayParams, EnumDisplayDevices, Screen, EnumDisplaySettingsEx | EDID-Identität und Zeichenfolgen (auch nachvollziehbar decodiert), Produktionsangaben, Größe, aktive Windows-Flächen, aktueller Desktopmodus, Refreshcode, Desktopfarbtiefe, Orientierung |
| Display | DisplayConfig und GetScaleFactorForMonitor | aktive Zielverbindung, bevorzugter Windows-Zielmodus, Refresh, Advanced Color, Bits pro Kanal, Skalierung der zugeordneten Desktopfläche |
| Audio | Win32_SoundDevice, vorhandene Core-Audio-Topologie | Geräte und Endpoints, Status, Standardrollen und vorhandener Sitzungsstatus |
| Audio | IAudioClient.GetMixFormat | Shared-Mode-Audioengineformat aktiver Endpoints: Kanäle, Abtastrate, Container-/gültige Bits, Blockausrichtung, Byte/s, Kanalmaske, Subtyp; kein Streamstart |
| USB/Peripherie | Win32_PnPEntity/USBController, PnP-Eigenschaften | Controller/Hubs/Geräte, IDs, Klasse, Treiber, Windows-Elternbeziehung, Status; Tastatur, Maus, Kamera, Bildgeräte, Drucker |
| Sicherheit | Win32_Tpm und Win32_DeviceGuard | TPM-Spezifikation/Firmware/Status, VBS und gemeldete konfigurierte/laufende Sicherheitsdienste |
| Batterie | Win32_Battery | Chemiecode, gemeldete Kapazitäten, Status, als geschätzt gekennzeichnete Windows-Restladung/-laufzeit |
| Sensoren | bestehender Cockpit-/LibreHardwareMonitor-Pfad | Hardware, Sensorname/-typ, Einheit, aktueller endlicher Wert und Erfassungszeitpunkt; kein zusätzlicher Providerstart |
| Sensoren | vorhandener SessionStatistics-Aggregator | abgeleitetes Minimum/Maximum/Stichprobenmittel, endliche Anzahl und Messintervall pro exaktem Kanal; keine Grenzwertstatistik oder Lückeninterpolation |

Ein Anbietername beweist keine Funktion. Ein ComputeAccelerator ist insbesondere
nicht automatisch eine unabhängig bestätigte NPU. Windows-Namen und die
Windows-Klasse bleiben einsehbar; eine vollständige NPU-Zertifizierung fehlt.
Legacy AdapterRAM ist ausdrücklich ein begrenztes 32-Bit-CIM-Feld, nicht
der verlässliche VRAM-Gesamtwert. DXGI liefert den eigenen Speicherwert.

## Noch nicht integrierte Detailpfade

| Bereich | Offene Felder / Ursache |
|---|---|
| CPU | CPU-Codename, Referenztakt/Multiplikator, effektiver Takt; keine belegte Zuordnung/Abfrage integriert. Microcode bleibt rohe Windows-Angabe. P/E-Namen werden nicht aus relativer EfficiencyClass geraten. |
| GPU | geräteweiter VRAM-Verbrauch, WDDM/VBIOS, iGPU/dGPU, Architektur/Codename, Speichertyp/-bus/-bandbreite, Vulkan/OpenCL/CUDA und Video-Funktionen; entsprechende API-/Herstellerprovider fehlen. |
| NPU | fachlich bestätigte NPU-Zuordnung, Telemetrie und Spezifikationsdatenbank fehlen. |
| Mainboard/Chipsatz | keine gepflegte Chipsatzdatenbank, keine erfundenen BIOS-Updateangebote. |
| RAM/SPD | kein SMBus-/SPD-Lesepfad; JEDEC/Timings/XMP/EXPO nicht integriert. CIM ist kein SPD-Dump. |
| SMART/NVMe | TRIM/Controller-Mapping fehlt; keine allgemeinen Vendorattribut-Deutungen. Unbekannte USB-Brücken werden nicht durch erzwungene Transporte abgefragt. Windows Reliability bleibt ein separater begrenzter Pfad. |
| Netzwerk/WLAN | keine dauerhafte Ratenhistorie; Intervallraten nur zwischen angeforderten Abfragen. Keine Frequenz-/Bandableitung bei uneindeutigen BSS-Daten, keine Schlüsselabfrage. |
| Display | physisch native Panelauflösung, separater HDR-Zustand und Panel-Farbtiefe fehlen. Bevorzugter Windows-Modus und Advanced Color beweisen diese nicht. |
| Audio | vollständige und Exclusive-Mode-Formatcapabilities fehlen; Mixformat ist kein Nachweis aller Geräteformate und keine Qualitätsbewertung. |
| USB | keine elektrische Hub-/Portmessung oder physische Portnummer. Windows-Eltern sind keine elektrische Topologie. |
| Sensoren | gerätegenaue Zuordnung bestehender Sensorkanäle zu CIM-/DXGI-Geräten fehlt; keine Namensheuristik. Statistik nur seit Messbeginn aus tatsächlich übergebenen Messwerten, nicht aus alten ungemessenen Zeiträumen. |

Nicht integrierte Detailfelder werden in der Ansicht als nicht verfügbar mit
Unsupported und Erklärung gezeigt. Das bezeichnet die aktuelle AETHER-
Quellenabdeckung, nicht die Behauptung, Windows oder die Hardware könne diese
Information grundsätzlich nicht liefern.

## Quellenreferenzen

- [Windows Hardware-CIM-Klassen](https://learn.microsoft.com/windows/win32/cimwin32prov/computer-system-hardware-classes)
- [Windows Storage Management](https://learn.microsoft.com/windows-hardware/drivers/storage/storage-management-api-classes)
- [Logische Prozessortopologie](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex)
- [DXGI Adapterbeschreibung](https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc)
- [DXGI Prozess-Speicherinformationen](https://learn.microsoft.com/windows/win32/api/dxgi1_4/nf-dxgi1_4-idxgiadapter3-queryvideomemoryinfo)
- [PnP-Geräteeigenschaften](https://learn.microsoft.com/windows-hardware/drivers/install/device-property-keys)
- [EnumDisplaySettingsEx](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsexw)
- [Native Wi-Fi](https://learn.microsoft.com/windows/win32/nativewifi/portal)
- [Direct3D-Featureabfrage](https://learn.microsoft.com/windows/win32/api/d3d12/nf-d3d12-id3d12device-checkfeaturesupport)
- [Audioengine-Mixformat](https://learn.microsoft.com/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getmixformat)
- [DisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig)

[Architektur](HARDWARE-INTELLIGENCE-ARCHITECTURE.md) ·
[Tatsächliche Prüfungen](MILESTONE-3-HARDWARE-REPORT.md)
