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
| GPU | Win32_VideoController und PnP | Modell, Windows-Herstellerangabe, Geräteidentität, Treiberversion/-datum, INF, Status |
| GPU | DXGI Adapter1/Adapter3 | unabhängige Adapterinstanzen, Vendor/Device/Subsystem/Revision, dedizierter/shared Speicher; lokale/nichtlokale Budgets und Nutzung des aktuellen Prozesses, Node 0 |
| NPU | Win32_PnPEntity | Windows-Geräte der Klassen ComputeAccelerator/NeuralProcessor, Identität, Name, Treiber und Status; keine TOPS |
| Mainboard | Win32_BaseBoard, Win32_SystemSlot | Hersteller, Produkt, Version, geschützte Seriennummer; gemeldete Slots und Bus-/Device-/Function-Codes |
| BIOS/UEFI | Win32_BIOS, Get-ComputerInfo.BiosFirmwareType, Confirm-SecureBootUEFI | Firmwareversion/-datum, SMBIOS-Version, Modus und Secure Boot |
| Chipsatz | PCI-Systemgeräte von Windows | echte Geräteidentitäten, Windows-Namen und Treiber; keine daraus abgeleitete Chipsatzfamilie |
| RAM | Win32_PhysicalMemory | Modulslot, Bank, Hersteller, Part Number, Seriennummer, Größe, SMBIOS-/Formfaktorcodes, Geschwindigkeiten, Breiten, Spannungen, rohe Rank-Attribute |
| Storage | MSFT_Disk/PhysicalDisk/Partition/Volume | Identität, Firmware, Bus, Kapazität, Sektorgrößen, OS-Disknummer, Partitionen, Volume/Dateisystem und Status |
| Storage | Get-StorageReliabilityCounter je PhysicalDisk | soweit gemeldet Temperatur, PowerOnHours, Wear, Read-/Write-Fehler, StartStop-/LoadUnload-Zähler |
| PCI/PCIe | Win32_PnPEntity, Get-PnpDeviceProperty | Geräte-/Subsystem-/Revisionsfelder aus expliziten IDs, Bus, rohe Busadresse, daraus validierte Device/Function, Treiber, aktuelle/maximale Linkbreite und Linkgeschwindigkeitscodes |
| Netzwerk | Win32_NetworkAdapter und .NET NetworkInterface | physisch/virtuell laut Windows, Typ, MAC, IP/Gateway/DNS, DHCP, MTU, Status, Linkrate, kumulierte Bytes und Fehler |
| WLAN | MSNdis_80211_ReceivedSignalStrength | Legacy-NDIS-Signal, wenn dieser Provider verfügbar ist |
| Bluetooth | Win32_PnPEntity | Geräteklasse, Herstellerangabe, Identität, Treiber, Status |
| Display | WmiMonitorID/BasicDisplayParams, EnumDisplayDevices, Screen, EnumDisplaySettingsEx | EDID-Identität und Zeichenfolgen (auch nachvollziehbar decodiert), Produktionsangaben, Größe, aktive Windows-Flächen, aktueller Desktopmodus, Refreshcode, Desktopfarbtiefe, Orientierung |
| Audio | Win32_SoundDevice, vorhandene Core-Audio-Topologie | Geräte und Endpoints, Status, Standardrollen und vorhandener Sitzungsstatus |
| USB/Peripherie | Win32_PnPEntity/USBController, PnP-Eigenschaften | Controller/Hubs/Geräte, IDs, Klasse, Treiber, Windows-Elternbeziehung, Status; Tastatur, Maus, Kamera, Bildgeräte, Drucker |
| Sicherheit | Win32_Tpm und Win32_DeviceGuard | TPM-Spezifikation/Firmware/Status, VBS und gemeldete konfigurierte/laufende Sicherheitsdienste |
| Batterie | Win32_Battery | Chemiecode, gemeldete Kapazitäten, Status, als geschätzt gekennzeichnete Windows-Restladung/-laufzeit |
| Sensoren | bestehender Cockpit-/LibreHardwareMonitor-Pfad | Hardware, Sensorname/-typ, Einheit, aktueller endlicher Wert und Erfassungszeitpunkt; kein zusätzlicher Providerstart |

Ein Anbietername beweist keine Funktion. Ein ComputeAccelerator ist insbesondere
nicht automatisch eine unabhängig bestätigte NPU. Windows-Namen und die
Windows-Klasse bleiben einsehbar; eine vollständige NPU-Zertifizierung fehlt.
Legacy AdapterRAM ist ausdrücklich ein begrenztes 32-Bit-CIM-Feld, nicht
der verlässliche VRAM-Gesamtwert. DXGI liefert den eigenen Speicherwert.

## Noch nicht integrierte Detailpfade

| Bereich | Offene Felder / Ursache |
|---|---|
| CPU | Microcode, CPU-Codename, Referenztakt/Multiplikator, effektiver Takt; keine belegte Zuordnung/Abfrage integriert. P/E-Namen werden nicht aus relativer EfficiencyClass geraten. |
| GPU | geräteweiter VRAM-Verbrauch, WDDM/VBIOS, iGPU/dGPU, Architektur/Codename, Speichertyp/-bus/-bandbreite, DirectX Feature Levels, Vulkan/OpenCL/CUDA, Raytracing/Video-Funktionen; entsprechende API-/Herstellerprovider fehlen. |
| NPU | fachlich bestätigte NPU-Zuordnung, Telemetrie und Spezifikationsdatenbank fehlen. |
| Mainboard/Chipsatz | keine gepflegte Chipsatzdatenbank, keine erfundenen BIOS-Updateangebote. |
| RAM/SPD | kein SMBus-/SPD-Lesepfad; JEDEC/Timings/XMP/EXPO nicht integriert. CIM ist kein SPD-Dump. |
| SMART/NVMe | keine Roh-SMART- oder NVMe-Healthlog-Abfrage, keine allgemeinen Vendorattribut-Deutungen; Windows Reliability ist ein separater begrenzter Pfad. TRIM/Controller-Mapping fehlt. |
| Netzwerk/WLAN | keine eigene Rx-/Tx-Ratenhistorie pro Adapter, Native-Wi-Fi-Band/Kanal/Standard fehlt; bestehende Netzwerkwerkzeuge bleiben separat. Keine Schlüsselabfrage. |
| Display | native Auflösung, HDR, Panel-Farbtiefe, monitorbezogene Skalierung und belegte Verbindungsart fehlen. Aktueller Desktopmodus ist nicht native Panelauflösung. |
| Audio | keine Formatcapability-Abfrage; keine Qualitätsbewertung. |
| USB | keine elektrische Hub-/Portmessung oder physische Portnummer. Windows-Eltern sind keine elektrische Topologie. |
| Sensoren | keine neue Statistikreihe; Minimum/Maximum/Average bleiben ausdrücklich nicht verfügbar. Bestehende Messauswertung bleibt separat. Keine geratene Zuordnung von Sensornamen auf CIM-/DXGI-Geräte. |

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

[Architektur](HARDWARE-INTELLIGENCE-ARCHITECTURE.md) ·
[Tatsächliche Prüfungen](MILESTONE-3-HARDWARE-REPORT.md)
