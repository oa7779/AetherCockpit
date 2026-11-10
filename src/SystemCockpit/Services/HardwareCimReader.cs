using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AetherOperations;

namespace SystemCockpit.Services;

internal sealed record HardwareCimResult(string Key, DateTimeOffset Timestamp, DataAvailability Availability,
    string? Error, JsonElement[] Rows);

public sealed class HardwareIntelligenceReader
{
    public async Task<IReadOnlyList<HardwareComponent>> ReadAsync(HardwareClass category, CancellationToken token)
    {
        if (category == HardwareClass.Sensors)
            return [new("sensors:status", category, "Sensorzugriff", [HardwareFact.Missing("sensor", "Sensorprovider",
                "Cockpit / bestehender Sensorpfad", DateTimeOffset.UtcNow,
                "Noch keine Messung übernommen. Bestehenden Sensorzugriff im Cockpit aktivieren; hier wird kein Treiber geöffnet.")])];
        var queries = HardwareCimCatalog.Queries.Where(q => q.Class == category).ToArray();
        var script = Script(queries, category);
        var result = await AdminShell.RunAsync(script, TimeSpan.FromSeconds(90), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (result.ExitCode != 0 || result.OutputTruncated)
        {
            Trace.TraceError("Hardware-CIM: Exitcode {0}; stderr: {1}", result.ExitCode, result.Error);
            throw new InvalidOperationException(result.OutputTruncated ? "Hardwareinventar überschreitet die Ausgabegrenze." :
                "Lesender Hardwareprovider abgebrochen; Exitcode " + result.ExitCode);
        }
        var batches = JsonSerializer.Deserialize<HardwareCimResult[]>(result.Output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("Hardwareprovider lieferte kein Ergebnis.");
        var components = Parse(category, batches);
        void Supplement(string name, Func<IReadOnlyList<HardwareComponent>> read)
        {
            try { components.AddRange(read()); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or
                System.Runtime.InteropServices.COMException or System.Net.NetworkInformation.NetworkInformationException)
            {
                Trace.TraceError("Hardwareprovider {0}: {1}", name, ex);
                components.Add(new("native-error:" + name, category, name,
                    [HardwareFact.Missing("error", "Providerstatus", name, DateTimeOffset.UtcNow,
                        $"Providerfehler ({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}).", DataAvailability.ProviderError)]));
            }
        }
        if (category == HardwareClass.Cpu) Supplement("CPU API", HardwareCpuReader.Read);
        if (category == HardwareClass.Gpu) Supplement("DXGI", HardwareGpuReader.Read);
        if (category == HardwareClass.Network) Supplement("NetworkInterface", HardwareRuntimeReader.Network);
        if (category == HardwareClass.Audio) Supplement("Core Audio", HardwareRuntimeReader.Audio);
        if (category == HardwareClass.Display) Supplement("Display API", HardwareRuntimeReader.Displays);
        token.ThrowIfCancellationRequested();
        return components;
    }

    internal static List<HardwareComponent> Parse(HardwareClass category, IReadOnlyList<HardwareCimResult> batches)
    {
        var components = new List<HardwareComponent>();
        var raw = new List<(HardwareComponent Component, JsonElement Row, string Query)>();
        foreach (var query in HardwareCimCatalog.Queries.Where(q => q.Class == category))
        {
            var matches = batches.Where(b => b.Key == query.Key).ToArray();
            if (matches.Length != 1) throw new JsonException("Fehlendes oder doppeltes Hardwareprovider-Ergebnis: " + query.Key);
            var batch = matches[0];
            if (!Enum.IsDefined(batch.Availability) || batch.Availability is DataAvailability.Stale or DataAvailability.Unknown)
                throw new JsonException("Ungültiger Providerstatus.");
            var rows = batch.Rows;
            if (rows is null) throw new JsonException("Fehlende Zeilenliste.");
            if (batch.Availability != DataAvailability.Available && rows.Length != 0)
                throw new JsonException("Fehlerergebnis darf keine frischen Werte enthalten.");
            var source = $"CIM {query.Namespace}:{query.CimClass}";
            if (rows.Length == 0)
            {
                var state = batch.Availability == DataAvailability.Available ? DataAvailability.Unavailable : batch.Availability;
                components.Add(new("status:" + query.Key, category, query.CimClass,
                    [HardwareFact.Missing("status", "Providerstatus", source, batch.Timestamp,
                        batch.Error ?? "Provider meldet keine Instanzen; keine Aussage über physische Abwesenheit.", state)]));
                continue;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var identity = Text(row, "_Identity");
                if (identity is null || !seen.Add(identity)) throw new JsonException("Fehlende oder doppelte Komponentenidentität.");
                var id = Identity(query.Key, identity);
                var label = Text(row, query.Label);
                var facts = query.Properties.Select(p => Fact(row, p, source, batch.Timestamp)).ToList();
                if (query.CimClass == "WmiMonitorID")
                {
                    foreach (var property in query.Properties.Where(p => p.Property.EndsWith("Name", StringComparison.Ordinal) ||
                                 p.Property is "ProductCodeID" or "SerialNumberID"))
                    {
                        var decoded = DecodeEdid(row, property.Property);
                        if (decoded is not null)
                        {
                            facts.Add(Derived(property.Property + ":text", property.Caption.Replace(" (Zeichencodes)", "", StringComparison.Ordinal),
                                decoded, source + "." + property.Property, batch.Timestamp,
                                "ASCII-Zeichencodes der von Windows gelieferten EDID-Zeichenfolge; Nullterminierung entfernt.")
                                with { Observation = SourceObservation<string>.Present(decoded, "",
                                    Metadata(source + "." + property.Property, batch.Timestamp, SourceType.Derived, classification: property.Classification)) });
                            if (property.Property == query.Label) label = decoded;
                        }
                    }
                }
                if (category != HardwareClass.System && Text(row, "PNPDeviceID") is not null)
                    AddDeviceFacts(row, facts, batch.Timestamp);
                var component = new HardwareComponent(id, category,
                    query.Key + " / " + (label ?? "Windows-Gerät"), facts);
                raw.Add((component, row, query.Key));
            }
        }
        // Only unique exact provider identities create edges, never model/serial-name heuristics.
        foreach (var (component, row, query) in raw)
        {
            string? parent = null;
            if (category == HardwareClass.Usb && Text(row, "Parent") is { } pnpParent)
                parent = Unique(raw.Where(r => pnpParent.Equals(Text(r.Row, "PNPDeviceID"), StringComparison.OrdinalIgnoreCase)));
            if (category == HardwareClass.Storage)
            {
                if (query == "Partition")
                    parent = Unique(raw.Where(r => r.Query == "Disk" && Same(row, "DiskNumber", r.Row, "Number")));
                if (query == "Volume")
                    parent = Unique(raw.Where(r => r.Query == "Partition" && AccessPathMatches(r.Row, row)));
                if (query == "Disk")
                    parent = Unique(raw.Where(r => r.Query == "Physical" && Same(row, "UniqueId", r.Row, "UniqueId")));
            }
            var facts = component.Facts.ToList();
            if (category == HardwareClass.Storage && query is "Partition" or "Volume" or "Disk")
                facts.Add(parent is null
                    ? HardwareFact.Missing("mapping", "Storage-Zuordnung", "CIM Storage-Identitäten", DateTimeOffset.UtcNow,
                        "Kein eindeutiger exakter DiskNumber-/VolumePath-/UniqueId-Match.")
                    : Derived("mapping", "Storage-Zuordnung", "Eindeutiger exakter Provider-Match", "CIM Storage-Identitäten",
                        DateTimeOffset.UtcNow, "Partition.DiskNumber = Disk.Number; Volume.Path in Partition.AccessPaths; Physical.UniqueId = Disk.UniqueId"));
            components.Add(component with { ParentId = parent, Facts = facts });
        }
        foreach (var extra in batches.Where(b => b.Key.StartsWith("Extra:", StringComparison.Ordinal)))
        {
            foreach (var row in extra.Rows)
            {
                var key = Text(row, "Key") ?? throw new JsonException("Zusatzwert ohne Schlüssel.");
                var name = Text(row, "Name") ?? key;
                var source = Text(row, "Source") ?? "Windows Hardwareprovider";
                var value = Text(row, "Value");
                var state = extra.Availability;
                var classification = Text(row, "Sensitive") == "true" ? DataClassification.Sensitive : DataClassification.Normal;
                var fact = value is not null && state == DataAvailability.Available
                    ? new HardwareFact(key, name, SourceObservation<string>.Present(value, Text(row, "Unit") ?? "",
                        Metadata(source, extra.Timestamp, classification: classification)))
                    : HardwareFact.Missing(key, name, source, extra.Timestamp, extra.Error ?? "Windows meldet keinen Wert.",
                        state == DataAvailability.Available ? DataAvailability.Unavailable : state, classification: classification);
                var target = Text(row, "Target");
                var index = target is null ? -1 : components.FindIndex(c => c.Id == Identity("Physical", target));
                if (index >= 0)
                    components[index] = components[index] with { Facts = components[index].Facts.Append(fact with { Cadence = HardwareCadence.Dynamic }).ToArray() };
                else components.Add(new(Identity(category + ":extra", extra.Key + ":" + key), category, name, [fact]));
            }
        }
        foreach (var gap in HardwareCimCatalog.Gaps(category))
            components.Add(new("gap:" + category + ":" + gap, category, "○ " + gap,
                [HardwareFact.Missing(gap, gap, "AETHER / Quellenabdeckung", DateTimeOffset.UtcNow,
                    "Kein belastbarer integrierter Lesepfad. Kein Ersatz durch Modellannahmen oder Marketingwerte.",
                    DataAvailability.Unsupported)]));
        return components;
    }

    private static string? Unique(IEnumerable<(HardwareComponent Component, JsonElement Row, string Query)> matches)
    {
        var ids = matches.Select(m => m.Component.Id).Take(2).ToArray();
        return ids.Length == 1 ? ids[0] : null;
    }

    private static bool Same(JsonElement a, string ap, JsonElement b, string bp) =>
        Text(a, ap) is { Length: > 0 } value && value.Equals(Text(b, bp), StringComparison.OrdinalIgnoreCase);

    private static bool AccessPathMatches(JsonElement partition, JsonElement volume) =>
        Text(volume, "Path") is { Length: > 0 } path &&
        partition.TryGetProperty("AccessPaths", out var paths) && paths.ValueKind == JsonValueKind.Array &&
        paths.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.String && path.Equals(p.GetString(), StringComparison.OrdinalIgnoreCase));

    internal static string Identity(string domain, string identity) =>
        domain + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToUpperInvariant())));

    internal static string? Text(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
            JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => throw new JsonException("Nicht skalarer Hardwarewert: " + property)
        };
    }

    internal static string? DecodeEdid(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        var characters = new List<char>();
        foreach (var code in value.EnumerateArray())
        {
            if (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var number) || number is < 0 or > 127) return null;
            if (number == 0) break;
            if (number < 32) return null;
            characters.Add((char)number);
        }
        return characters.Count == 0 ? null : new string(characters.ToArray()).Trim();
    }

    internal static HardwareFact Fact(JsonElement row, HardwareProperty property, string source, DateTimeOffset time)
    {
        var value = Text(row, property.Property);
        if (row.TryGetProperty("_PropertyStates", out var states) &&
            states.TryGetProperty(property.Property, out var errorState))
        {
            var state = (DataAvailability)errorState.GetInt32();
            if (state is not (DataAvailability.AccessDenied or DataAvailability.Unsupported or DataAvailability.ProviderError))
                throw new JsonException("Ungültiger Eigenschaftsfehler.");
            return HardwareFact.Missing(property.Property, property.Caption, source + "." + property.Property, time,
                "Windows-Geräteeigenschaft konnte nicht gelesen werden.", state, property.Unit, property.Classification);
        }
        return value is null
            ? HardwareFact.Missing(property.Property, property.Caption, source + "." + property.Property, time,
                "Eigenschaft fehlt oder wurde leer gemeldet.", classification: property.Classification, unit: property.Unit)
            : new(property.Property, property.Caption, SourceObservation<string>.Present(value, property.Unit,
                Metadata(source + "." + property.Property, time, estimated: property.Property.StartsWith("Estimated", StringComparison.Ordinal),
                    classification: property.Classification)), property.Cadence);
    }

    internal static SourceMetadata Metadata(string source, DateTimeOffset time, SourceType type = SourceType.Direct,
        bool estimated = false, DataClassification classification = DataClassification.Normal) =>
        new(source, type, time, type == SourceType.Unconfirmed ? DataConfidence.Unconfirmed : DataConfidence.ProviderReported,
            DataAvailability.Available, isEstimated: estimated, classification: classification,
            documentationLink: new Uri("https://learn.microsoft.com/windows/win32/cimwin32prov/computer-system-hardware-classes"));

    internal static HardwareFact Derived(string key, string name, string value, string source, DateTimeOffset time, string calculation) =>
        new(key, name, SourceObservation<string>.Present(value, "", Metadata(source, time, SourceType.Derived)),
            Calculation: calculation);

    private static void AddDeviceFacts(JsonElement row, List<HardwareFact> facts, DateTimeOffset time)
    {
        var id = Text(row, "PNPDeviceID");
        foreach (var (key, pattern) in new[] { ("Vendor ID", @"(?:VEN_|VID_)([0-9A-F]{4})"),
                     ("Device / Product ID", @"(?:DEV_|PID_)([0-9A-F]{4})"), ("Subsystem ID", @"SUBSYS_([0-9A-F]{8})"),
                     ("Revision", @"REV_([0-9A-F]{2})") })
        {
            var match = id is null ? Match.Empty : Regex.Match(id, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            facts.Add(match.Success ? Derived(key, key, match.Groups[1].Value, "Windows PnP-Hardware-ID", time,
                "Extraktion des expliziten Hex-Feldes " + pattern)
                : HardwareFact.Missing(key, key, "Windows PnP-Hardware-ID", time, "Kein entsprechendes Hex-Feld gemeldet."));
        }
        foreach (var (key, name, unit) in new[] { ("Parent", "Windows-Elterngerät", ""), ("BusNumber", "Busnummer", ""),
                     ("Address", "Busadresse (Windows roh)", ""), ("CurrentLinkSpeed", "PCIe aktuelle Linkgeschwindigkeit (Windows-Code)", ""),
                     ("CurrentLinkWidth", "PCIe aktuelle Linkbreite", "lanes"), ("MaxLinkSpeed", "PCIe maximale Linkgeschwindigkeit (Windows-Code)", ""),
                     ("MaxLinkWidth", "PCIe maximale Linkbreite", "lanes"), ("DriverVersion", "PnP-Treiberversion", ""),
                     ("DriverDate", "PnP-Treiberdatum", "") })
            facts.Add(Fact(row, new(key, name, unit, key == "Parent" ? DataClassification.Sensitive : DataClassification.Normal,
                HardwareCadence.Dynamic), "Get-PnpDeviceProperty", time));
        if (id?.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) == true)
        {
            var validAddress = uint.TryParse(Text(row, "Address"), NumberStyles.None, CultureInfo.InvariantCulture, out var address) &&
                (address >> 16) <= 31 && (address & 0xffff) <= 7;
            foreach (var (name, value, calculation) in new[]
            {
                ("PCI Device", address >> 16, "DEVPKEY_Device_Address >> 16; nur gültige PCI Device-/Function-Bereiche"),
                ("PCI Function", address & 0xffff, "DEVPKEY_Device_Address & 0xffff; nur gültige PCI Device-/Function-Bereiche")
            })
                facts.Add(validAddress ? Derived(name, name, value.ToString(CultureInfo.InvariantCulture), "DEVPKEY_Device_Address",
                    time, calculation) : HardwareFact.Missing(name, name, "DEVPKEY_Device_Address", time,
                    "Keine gültige PCI Device-/Function-Adresse gemeldet."));
        }
    }

    internal static string Script(HardwareQuery[] queries, HardwareClass category)
    {
        var spec = JsonSerializer.Serialize(queries.Select(q => new { q.Key, q.Namespace, q.CimClass, q.Filter, q.Identity,
            IncludePnp = category != HardwareClass.System,
            Properties = q.Properties.Select(p => p.Property).Append(q.Identity).Distinct().ToArray() }));
        return """
            $ErrorActionPreference='Stop'
            $queries=ConvertFrom-Json @'
            """ + "\n" + spec + "\n'@\n" + """
            $results=[Collections.Generic.List[object]]::new()
            function IdentityOf($device,$fallback) {
                $keys=[ordered]@{}
                foreach($p in @($device.CimInstanceProperties | Sort-Object Name)){
                    if(($p.Flags -band [Microsoft.Management.Infrastructure.CimFlags]::Key) -ne 0){
                        $keys[$p.Name]=$p.Value
                    }
                }
                if($keys.Count -gt 0){return (ConvertTo-Json -InputObject $keys -Compress)}
                $p=$device.CimInstanceProperties[$fallback]
                if($null -ne $p -and $null -ne $p.Value){return [string]$p.Value}
                throw [InvalidOperationException]::new('Keine CIM-Schluesselidentitaet')
            }
            function FailState($e) {
                if($e.Exception -is [UnauthorizedAccessException] -or $e.CategoryInfo.Category -eq 'PermissionDenied'){return 2}
                if($e.Exception.HResult -eq -2147217392 -or $e.Exception.HResult -eq -2147217394){return 3}
                return 4
            }
            foreach($q in $queries){
                $time=[DateTimeOffset]::UtcNow.ToString('o')
                try {
                    $args=@{Namespace=$q.Namespace;ClassName=$q.CimClass;OperationTimeoutSec=8;ErrorAction='Stop'}
                    if($q.Filter){$args.Filter=$q.Filter}
                    $rows=@(Get-CimInstance @args | ForEach-Object {
                        $device=$_
                        $item=@{_Identity=(IdentityOf $device $q.Identity);_PropertyStates=@{}}
                        foreach($p in $q.Properties){
                            $property=$device.CimInstanceProperties[$p]
                            $v=if($null -eq $property){$null}else{$property.Value}
                            if($v -is [DateTime]){$v=$v.ToUniversalTime().ToString('o')}
                            $item[$p]=$v
                        }
                        if($q.IncludePnp -and $device.PNPDeviceID){
                            $pairs=@(
                                @('Parent','DEVPKEY_Device_Parent'),@('BusNumber','DEVPKEY_Device_BusNumber'),
                                @('Address','DEVPKEY_Device_Address'),@('DriverVersion','DEVPKEY_Device_DriverVersion'),
                                @('DriverDate','DEVPKEY_Device_DriverDate'),
                                @('CurrentLinkSpeed','DEVPKEY_PciDevice_CurrentLinkSpeed'),
                                @('CurrentLinkWidth','DEVPKEY_PciDevice_CurrentLinkWidth'),
                                @('MaxLinkSpeed','DEVPKEY_PciDevice_MaxLinkSpeed'),@('MaxLinkWidth','DEVPKEY_PciDevice_MaxLinkWidth'))
                            try{
                                $properties=@(Get-PnpDeviceProperty -InstanceId $device.PNPDeviceID -ErrorAction Stop)
                                foreach($pair in $pairs){
                                    $property=@($properties | Where-Object KeyName -eq $pair[1])
                                    if($property.Count -eq 1){
                                        $v=$property[0].Data
                                        if($v -is [DateTime]){$v=$v.ToUniversalTime().ToString('o')}
                                        $item[$pair[0]]=$v
                                    }else{$item._PropertyStates[$pair[0]]=3}
                                }
                            }catch{
                                $code=if($_.CategoryInfo.Category -eq 'ObjectNotFound'){3}else{FailState $_}
                                foreach($pair in $pairs){$item._PropertyStates[$pair[0]]=$code}
                            }
                        }
                        $item
                    })
                    $results.Add(@{Key=$q.Key;Timestamp=$time;Availability=0;Rows=$rows})
                } catch {
                    $code=FailState $_
                    $results.Add(@{Key=$q.Key;Timestamp=$time;Availability=$code;Error=('CIM-Abfrage fehlgeschlagen (HRESULT 0x{0:X8})' -f $_.Exception.HResult);Rows=@()})
                }
            }
            """ + (category == HardwareClass.Firmware || category == HardwareClass.System ? FirmwareScript : "") +
            (category == HardwareClass.Storage ? ReliabilityScript : "") +
            "\nConvertTo-Json -InputObject @($results.ToArray()) -Depth 10 -Compress";
    }

    private const string FirmwareScript = """

        foreach($key in @('FirmwareType','SecureBoot')){
            $time=[DateTimeOffset]::UtcNow.ToString('o')
            try{
                if($key -eq 'FirmwareType'){
                    $v=(Get-ComputerInfo -Property BiosFirmwareType -ErrorAction Stop).BiosFirmwareType
                    if($null -eq $v){throw [InvalidOperationException]::new('Kein Firmwaremodus')}
                    $v=[string]$v
                }else{$v=[string](Confirm-SecureBootUEFI -ErrorAction Stop)}
                $results.Add(@{Key=('Extra:'+$key);Timestamp=$time;Availability=0;Rows=@(@{Key=$key;Name=$key;Source=($(if($key -eq 'FirmwareType'){'Get-ComputerInfo.BiosFirmwareType'}else{'Confirm-SecureBootUEFI'}));Value=$v})})
            }catch{
                $results.Add(@{Key=('Extra:'+$key);Timestamp=$time;Availability=(FailState $_);Error=('Firmwareabfrage fehlgeschlagen (HRESULT 0x{0:X8})' -f $_.Exception.HResult);Rows=@(@{Key=$key;Name=$key;Source='Windows-Firmwareabfrage'})})
            }
        }
        """;

    private const string ReliabilityScript = """

        try {
            foreach($disk in @(Get-PhysicalDisk -ErrorAction Stop)){
                $time=[DateTimeOffset]::UtcNow.ToString('o')
                $target=IdentityOf $disk 'ObjectId'
                try {
                    $counter=Get-StorageReliabilityCounter -PhysicalDisk $disk -ErrorAction Stop
                    $rows=@()
                    foreach($p in @('Temperature','TemperatureMax','PowerOnHours','Wear','ReadErrorsTotal','ReadErrorsUncorrected','WriteErrorsTotal','WriteErrorsUncorrected','StartStopCycleCount','LoadUnloadCycleCount')){
                        $unit=if($p -like 'Temperature*'){'°C'}elseif($p -eq 'PowerOnHours'){'h'}elseif($p -eq 'Wear'){'% (Windows Storage gemeldet)'}else{''}
                        $rows+=@{Key=('Reliability:'+$p);Name=('Storage Reliability / '+$p);Source=('Get-StorageReliabilityCounter.'+$p);Target=$target;Value=$counter.$p;Unit=$unit}
                    }
                    $results.Add(@{Key=('Extra:Reliability:'+$target);Timestamp=$time;Availability=0;Rows=$rows})
                }catch{
                    $results.Add(@{Key=('Extra:Reliability:'+$target);Timestamp=$time;Availability=(FailState $_);Error=('Storage-Reliability nicht lesbar (HRESULT 0x{0:X8})' -f $_.Exception.HResult);Rows=@(@{Key='Reliability';Name='Storage Reliability';Source='Get-StorageReliabilityCounter';Target=$target})})
                }
            }
        }catch{
            $results.Add(@{Key='Extra:ReliabilityProvider';Timestamp=[DateTimeOffset]::UtcNow.ToString('o');Availability=(FailState $_);Error=('PhysicalDisk-Provider fehlgeschlagen (HRESULT 0x{0:X8})' -f $_.Exception.HResult);Rows=@(@{Key='ReliabilityProvider';Name='Storage Reliability';Source='Get-PhysicalDisk'})})
        }
        """;
}
