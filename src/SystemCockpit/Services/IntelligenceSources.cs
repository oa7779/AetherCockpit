using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class IntelligenceSources
{
    internal static IntelligenceEntity Entity(string id, IntelligenceKind kind, string name, string domain,
        string source, DateTimeOffset time, IReadOnlyDictionary<string, string>? fields = null,
        DataAvailability state = DataAvailability.Available) =>
        new(id, kind, name, new Dictionary<string, string>(fields ?? new Dictionary<string, string>()) { ["domain"] = domain },
            IntelligenceModel.Metadata(source, time, state));

    internal static IntelligenceSnapshot Live(DashboardSnapshot? dashboard, LiveSample? live, HardwareInventory? hardware, string name)
    {
        var time = dashboard?.Timestamp ?? live?.Timestamp ?? DateTimeOffset.UtcNow;
        var entities = new List<IntelligenceEntity>(); var edges = new List<IntelligenceEdge>(); var coverage = new List<SourceCoverage>();
        if (dashboard is not null)
        {
            foreach (var row in dashboard.Processes)
            {
                var fields = new Dictionary<string, string>
                {
                    ["pid"] = row.Id.ToString(CultureInfo.InvariantCulture), ["start"] = row.StartTicks.ToString(CultureInfo.InvariantCulture),
                    ["status"] = row.Status
                };
                void Number(string key, double? value) { if (value is { } v) fields[key] = v.ToString("R", CultureInfo.InvariantCulture); }
                Number("cpu", row.CpuPercent); Number("ramMiB", row.MemoryMb); Number("privateMiB", row.PrivateMb);
                Number("readMiBs", row.ReadMbps); Number("writeMiBs", row.WriteMbps); Number("handles", row.Handles); Number("threads", row.Threads);
                entities.Add(Entity("process:" + row.Identity, IntelligenceKind.Process, row.Name, "Process", "Gemeinsame Windows-Prozessprobe",
                    row.ObservedAt == default ? dashboard.Timestamp : row.ObservedAt, fields,
                    row.Identity.IsKnown ? DataAvailability.Available : DataAvailability.Unknown));
            }
            var byPid = dashboard.Processes.GroupBy(p => p.Id).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
            foreach (var row in dashboard.Processes.Where(p => p.Identity.IsKnown))
                if (row.ParentPid is { } pid && byPid.TryGetValue(pid, out var parent) && parent.Identity.IsKnown && parent.StartTicks < row.StartTicks)
                    edges.Add(new("process:" + parent.Identity, "process:" + row.Identity, "Eltern-PID mit Erstellungsreihenfolge",
                        IntelligenceModel.Metadata("Toolhelp / Prozessstart", dashboard.Timestamp)));
            coverage.Add(new("Process", dashboard.Processes.All(p => p.Identity.IsKnown), IntelligenceModel.Metadata("Process.GetProcesses", dashboard.Timestamp)));
            foreach (var adapter in dashboard.Adapters)
            {
                var f = new Dictionary<string, string> { ["interfaceId"] = adapter.Id, ["speedBps"] = adapter.Speed.ToString(CultureInfo.InvariantCulture) };
                if (adapter.DownloadMbps is { } down) f["downloadMbps"] = down.ToString("R", CultureInfo.InvariantCulture);
                if (adapter.UploadMbps is { } up) f["uploadMbps"] = up.ToString("R", CultureInfo.InvariantCulture);
                entities.Add(Entity("adapter:" + adapter.Id, IntelligenceKind.Adapter, adapter.Name, "Adapter", "Gemeinsame aktive Adapterprobe", time, f));
            }
            coverage.Add(new("Adapter", false, IntelligenceModel.Metadata("Dashboard erfasst nur aktive Nicht-Loopback-Adapter", time)));
        }
        if (live is not null)
        {
            var f = new Dictionary<string, string> { ["ramPercent"] = live.MemoryPercent.ToString("R", CultureInfo.InvariantCulture) };
            if (live.CpuPercent is { } cpu) f["cpu"] = cpu.ToString("R", CultureInfo.InvariantCulture);
            entities.Add(Entity("resource:system", IntelligenceKind.Resource, "Aktuelle Systemressourcen", "Resource", "Vorhandene NativeMetrics", live.Timestamp, f));
            foreach (var sensor in live.Sensors.Where(s => s.Value is not null))
                entities.Add(Entity("sensor:" + sensor.Id, IntelligenceKind.Sensor, sensor.Name, "Sensor", "Vorhandener Cockpit-Sensorpfad", live.Timestamp,
                    new Dictionary<string, string> { ["value"] = sensor.Value!.Value.ToString("R", CultureInfo.InvariantCulture), ["unit"] = sensor.Unit }));
            coverage.Add(new("Resource", true, IntelligenceModel.Metadata("NativeMetrics", live.Timestamp)));
            coverage.Add(new("Sensor", false, IntelligenceModel.Metadata("Nur bereits aktivierte verfügbare Sensoren", live.Timestamp)));
        }
        if (hardware is not null)
        {
            foreach (var component in hardware.At(time).Components)
            {
                var values = component.Facts.Where(f => f.Observation.Metadata.Classification != DataClassification.Secret)
                    .GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First().Display + " [" + g.First().State + "]");
                entities.Add(Entity("hardware:" + component.Id, IntelligenceKind.Hardware, component.Name, "Hardware",
                    "Vorhandener M3-Inventarzustand; Einzelquellen in Hardwareinspektor", hardware.GeneratedAt, values,
                    component.Facts.Any(f => f.Observation.Metadata.Availability != DataAvailability.Available) ? DataAvailability.Unknown : DataAvailability.Available));
            }
            coverage.Add(new("Hardware", false, IntelligenceModel.Metadata("Klassenweise erfasst, kein vollständiger Bestand", hardware.GeneratedAt)));
        }
        return new(1, Guid.NewGuid(), name, time, entities.GroupBy(e => e.Id).Select(g => g.First()).ToArray(), edges, coverage);
    }

    internal static async Task<IntelligenceSnapshot> CaptureAsync(IntelligenceSnapshot live, CancellationToken token)
    {
        var start = DateTimeOffset.UtcNow;
        var entities = live.Entities.ToList(); var edges = live.Edges.ToList(); var coverage = live.Coverage.ToList();
        void Cover(string domain, bool complete, string source, DataAvailability state = DataAvailability.Available) =>
            coverage.Add(new(domain, complete, IntelligenceModel.Metadata(source, DateTimeOffset.UtcNow, state)));
        try
        {
            var software = await Task.Run(() => SystemInventory.ReadRegistrations(token), token).ConfigureAwait(false);
            foreach (var row in software)
                entities.Add(Entity("software:" + row.Id, IntelligenceKind.Software, row.Name, "Software", "Uninstall-Registry-Identität", start,
                    new Dictionary<string, string> { ["version"] = row.Version, ["publisher"] = row.Publisher, ["location"] = row.Location,
                        ["installDateReported"] = row.InstallDate, ["architecture"] = row.Architecture, ["fileOwnership"] = "Nicht bewiesen" }));
            Cover("Software", true, "HKLM/HKCU Uninstall; vorhandene Inventarfilter");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { Trace.TraceError("Software Intelligence: {0}", ex); Cover("Software", false, ex.GetType().Name, ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError); }
        var relations = await ProcessCorrelation.ReadSystemAsync(token).ConfigureAwait(false);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string FileEntity(string path, DateTimeOffset time, string source)
        {
            if (files.TryGetValue(path, out var existing)) return existing;
            var id = "file:" + path.ToUpperInvariant();
            files.Add(path, id);
            entities.Add(Entity(id, IntelligenceKind.File, path, "File", source, time,
                new Dictionary<string, string> { ["path"] = path, ["signature"] = "Nicht abgefragt; Dateidetails im Prozessinspektor" }));
            return id;
        }
        var verified = new Dictionary<int, IntelligenceEntity>();
        foreach (var process in live.Entities.Where(e => e.Kind == IntelligenceKind.Process && e.Metadata.Availability == DataAvailability.Available))
        {
            token.ThrowIfCancellationRequested();
            if (!int.TryParse(process.Fields.GetValueOrDefault("pid"), out var pid) ||
                !long.TryParse(process.Fields.GetValueOrDefault("start"), out var ticks) || ticks > start.UtcTicks) continue;
            try
            {
                using var handle = ProcessNative.Open(new(pid, ticks));
                var path = ProcessNative.Path(handle);
                if (ProcessNative.Exited(handle)) continue;
                verified[pid] = process;
                edges.Add(new(process.Id, FileEntity(path, DateTimeOffset.UtcNow, "QueryFullProcessImageName"),
                    "Exakter aktueller Imagepfad; keine Aussage über ersetzte Dateiinhalte",
                    IntelligenceModel.Metadata("Identitätsgebundenes Prozesshandle", DateTimeOffset.UtcNow)));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            { Trace.TraceWarning("Prozessdatei nicht mehr belegbar: {0}", ex.Message); }
        }
        Cover("File", false, "Nur belegte aktuelle Images und konfigurierte Executables, kein Dateisysteminventar");
        foreach (var group in relations.Where(r => r.Kind != "Quellenabdeckung").GroupBy(r => r.Kind))
        {
            var kind = group.Key switch { "Dienst" => IntelligenceKind.Service, "Autostart" => IntelligenceKind.Startup,
                "Geplante Aufgabe" => IntelligenceKind.Task, "Softwarepaket" => IntelligenceKind.Software,
                "Netzwerkadapter" => IntelligenceKind.Adapter, _ => IntelligenceKind.Endpoint };
            var domain = group.Key;
            foreach (var rows in group.Where(r => r.Availability == DataAvailability.Available).GroupBy(r =>
                r.Key + (r.Kind is "TCP" or "UDP" ? ":" + r.OwnerPid : "")))
            {
                var row = rows.First();
                var process = row.OwnerPid is { } owner ? verified.GetValueOrDefault(owner) : null;
                var id = "relation:" + domain + ":" + rows.Key + (kind == IntelligenceKind.Endpoint ? ":" + (process?.Id ?? "unverified") : "");
                var f = new Dictionary<string, string> { ["value"] = string.Join(" | ", rows.Select(r => r.Value).Distinct()), ["key"] = row.Key };
                if (rows.Any(r => r.Executable is not null)) f["executable"] = string.Join(" | ", rows.Select(r => r.Executable).Where(p => p is not null).Distinct());
                if (row.OwnerPid is { } pid) f["ownerPid"] = pid.ToString(CultureInfo.InvariantCulture);
                if (row.Kind is "TCP" or "UDP")
                {
                    f["protocol"] = row.Kind; f["localAddress"] = row.LocalAddress ?? "Nicht verfügbar";
                    f["localPort"] = row.LocalPort?.ToString(CultureInfo.InvariantCulture) ?? "Nicht verfügbar";
                    f["remoteAddress"] = row.RemoteAddress ?? "Kein Remoteziel (UDP) oder nicht verfügbar";
                    f["remotePort"] = row.RemotePort?.ToString(CultureInfo.InvariantCulture) ?? "Nicht verfügbar";
                    f["state"] = row.EndpointState ?? "Nicht verfügbar";
                    f["processIdentity"] = process?.Id ?? "Nicht belegt; Owner-PID allein reicht nicht.";
                }
                entities.Add(Entity(id, kind, row.Key, domain, row.Source, row.Timestamp, f));
                foreach (var path in rows.Select(r => r.Executable).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                    edges.Add(new(id, FileEntity(path, row.Timestamp, row.Source), "Explizit konfigurierte Executable", IntelligenceModel.Metadata(row.Source, row.Timestamp)));
                if (process is not null && long.TryParse(process.Fields.GetValueOrDefault("start"), out var ticks) && ticks > 0 && ticks <= start.UtcTicks)
                {
                    try
                    {
                        using var handle = ProcessNative.Open(new(row.OwnerPid!.Value, ticks));
                        if (!ProcessNative.Exited(handle)) edges.Add(new(process.Id, id, "Owner-PID während geprüfter Lebenszeit",
                            IntelligenceModel.Metadata(row.Source, row.Timestamp)));
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    { Trace.TraceWarning("Endpoint-Identität nicht mehr belegbar: {0}", ex); }
                }
            }
            Cover(domain, group.All(r => r.Availability == DataAvailability.Available), "Vorhandene Prozesskorrelationsquelle",
                group.Any(r => r.Availability != DataAvailability.Available) ? DataAvailability.Unknown : DataAvailability.Available);
        }
        // Empty source domains must remain explicit rather than implying a successful full inventory.
        foreach (var domain in new[] { "Dienst", "Autostart", "Geplante Aufgabe", "Softwarepaket", "TCP", "UDP" })
            if (coverage.All(c => c.Domain != domain))
            {
                var complete = relations.Any(r => r.Kind == "Quellenabdeckung" && r.Key == domain && r.Availability == DataAvailability.Available);
                Cover(domain, complete, complete ? "Erfolgreich gelesen; leerer Bestand" : "Keine Quellenzeile; Vollständigkeit nicht belegt",
                    complete ? DataAvailability.Available : DataAvailability.Unknown);
            }
        var response = await AdminShell.RunAsync(InventoryScript, TimeSpan.FromSeconds(40), token).ConfigureAwait(false);
        if (response.ExitCode != 0 || response.OutputTruncated) throw new InvalidOperationException("Intelligence-Inventur fehlgeschlagen oder abgeschnitten.");
        var inventory = JsonSerializer.Deserialize<InventoryRows>(response.Output) ?? throw new JsonException("Inventur fehlt.");
        if (inventory.Rows is null || inventory.Sources is null || inventory.Rows.Length > 12000) throw new JsonException("Inventurgrenze überschritten.");
        foreach (var row in inventory.Rows)
        {
            if (!Enum.TryParse<IntelligenceKind>(row.Kind, out var kind) || !Enum.IsDefined(kind)) throw new JsonException("Unbekannte Inventurklasse.");
            entities.Add(Entity(row.Id, kind, row.Name, row.Domain, row.Source, row.Timestamp, row.Fields));
        }
        foreach (var source in inventory.Sources)
            Cover(source.Domain, source.Complete, source.Source, source.Complete ? DataAvailability.Available : DataAvailability.ProviderError);
        var events = await Task.Run(() => IntelligenceWindowsEvents.Read(token), token).ConfigureAwait(false);
        entities.AddRange(events.Entities); coverage.AddRange(events.Coverage);
        var availableIds = entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var driver in entities.Where(e => e.Kind == IntelligenceKind.Driver))
        {
            if (driver.Fields.TryGetValue("deviceId", out var device))
            {
                var pnp = "device:" + device;
                if (availableIds.Contains(pnp)) edges.Add(new(driver.Id, pnp, "Exakte PnP-Geräteinstanz", driver.Metadata));
            }
            if (driver.Fields.TryGetValue("service", out var service) && service.Length > 0)
            {
                var target = entities.SingleOrDefault(e => e.Kind == IntelligenceKind.Service && e.Fields.GetValueOrDefault("key") == service);
                if (target is not null) edges.Add(new(driver.Id, target.Id, "PnP-Treiberdienst", driver.Metadata));
            }
        }
        var result = live with { Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow,
            Entities = entities.GroupBy(e => e.Id).Select(g => g.First()).ToArray(),
            Edges = edges.DistinctBy(e => (e.From, e.To, e.Relation)).ToArray(), Coverage = coverage.DistinctBy(c => c.Domain).ToArray() };
        IntelligenceModel.Validate(result);
        return result;
    }

    private sealed record InventoryRows(InventoryRow[] Rows, InventorySource[] Sources);
    private sealed record InventoryRow(string Id, string Kind, string Domain, string Name, string Source, DateTimeOffset Timestamp, Dictionary<string, string> Fields);
    private sealed record InventorySource(string Domain, bool Complete, string Source);
    internal const string InventoryScript = """
        $ErrorActionPreference='Stop'
        $rows=[Collections.Generic.List[object]]::new(); $sources=[Collections.Generic.List[object]]::new()
        function Row($id,$kind,$domain,$name,$source,$fields) {
            if($rows.Count -ge 12000){throw 'Inventurgrenze erreicht'}
            $rows.Add(@{Id=[string]$id;Kind=$kind;Domain=$domain;Name=[string]$name;Source=$source;Timestamp=[DateTimeOffset]::UtcNow.ToString('O');Fields=$fields})
        }
        function Source($domain,$complete,$text){$sources.Add(@{Domain=$domain;Complete=$complete;Source=$text})}
        try {
            $devices=@(Get-CimInstance Win32_PnPEntity -OperationTimeoutSec 8)
            $byId=@{}; foreach($d in $devices) {
                $byId[[string]$d.DeviceID]=$d
                Row ('device:'+$d.DeviceID) 'Hardware' 'PnP' $d.Name 'Win32_PnPEntity' @{
                    deviceId=[string]$d.DeviceID;class=[string]$d.PNPClass;service=[string]$d.Service;
                    hardwareIds=([string]::Join(' | ',@($d.HardwareID)));status=[string]$d.Status;problem=[string]$d.ConfigManagerErrorCode
                }
            }
            Source 'PnP' $true 'Win32_PnPEntity'
        } catch {Source 'PnP' $false ('Win32_PnPEntity: '+$_.Exception.HResult)}
        try {
            Get-CimInstance Win32_PnPSignedDriver -OperationTimeoutSec 8 | ForEach-Object {
                $service=''; if($byId.ContainsKey([string]$_.DeviceID)){$service=[string]$byId[[string]$_.DeviceID].Service}
                Row ('driver:'+$_.DeviceID) 'Driver' 'Driver' $_.DeviceName 'Win32_PnPSignedDriver / DeviceID' @{
                    deviceId=[string]$_.DeviceID;class=[string]$_.DeviceClass;provider=[string]$_.DriverProviderName;
                    version=[string]$_.DriverVersion;date=[string]$_.DriverDate;inf=[string]$_.InfName;
                    signed=[string]$_.IsSigned;signer=[string]$_.Signer;service=$service
                }
            }
            Source 'Driver' $true 'Win32_PnPSignedDriver'
        } catch {Source 'Driver' $false ('Win32_PnPSignedDriver: '+$_.Exception.HResult)}
        try {
            Get-CimInstance Win32_SystemDriver -OperationTimeoutSec 8 | ForEach-Object {
                Row ('kernelservice:'+$_.Name) 'Service' 'KernelService' $_.DisplayName 'Win32_SystemDriver.Name' @{
                    key=[string]$_.Name;startMode=[string]$_.StartMode;state=[string]$_.State;path=[string]$_.PathName;serviceType=[string]$_.ServiceType
                }
            }
            Source 'KernelService' $true 'Win32_SystemDriver'
        } catch {Source 'KernelService' $false ('Win32_SystemDriver: '+$_.Exception.HResult)}
        try {
            Get-CimInstance Win32_ReliabilityStabilityMetrics -OperationTimeoutSec 6 | Select-Object -First 16 | ForEach-Object {
                Row ('reliability:'+$_.TimeGenerated.ToUniversalTime().ToString('O')) 'Resource' 'ReliabilityIndex' 'Windows-Zuverlässigkeitsindex' 'Win32_ReliabilityStabilityMetrics' @{
                    index=[string]$_.SystemStabilityIndex;sourceTime=$_.TimeGenerated.ToUniversalTime().ToString('O')
                }
            }
            Source 'ReliabilityIndex' $true 'Win32_ReliabilityStabilityMetrics'
        } catch {Source 'ReliabilityIndex' $false ('Win32_ReliabilityStabilityMetrics: '+$_.Exception.HResult)}
        try {
            Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 8 | ForEach-Object {
                Row 'boot:current' 'Boot' 'Boot' 'Windows-Start' 'Win32_OperatingSystem.LastBootUpTime' @{
                    bootUtc=$_.LastBootUpTime.ToUniversalTime().ToString('O');localDateTime=$_.LocalDateTime.ToUniversalTime().ToString('O')
                }
            }
            Get-CimInstance Win32_LogonSession -Filter 'LogonType=2 OR LogonType=10' -OperationTimeoutSec 8 | ForEach-Object {
                Row ('logon:'+$_.LogonId) 'Boot' 'Boot' ('Interaktive Sitzung '+$_.LogonId) 'Win32_LogonSession' @{
                    startUtc=$_.StartTime.ToUniversalTime().ToString('O');logonType=[string]$_.LogonType
                }
            }
            Source 'Boot' $true 'Win32_OperatingSystem / Win32_LogonSession'
        } catch {Source 'Boot' $false ('Boot-/Sitzungsquelle: '+$_.Exception.HResult)}
        try {
            Get-NetAdapter -IncludeHidden -ErrorAction Stop | ForEach-Object {
                Row ('netadapter:'+$_.InterfaceGuid) 'Adapter' 'NetworkInventory' $_.Name 'Get-NetAdapter / InterfaceGuid' @{
                    interfaceId=[string]$_.InterfaceGuid;status=[string]$_.Status;linkSpeed=[string]$_.LinkSpeed;
                    driverVersion=[string]$_.DriverVersion;description=[string]$_.InterfaceDescription
                }
            }
            Source 'NetworkInventory' $true 'Get-NetAdapter -IncludeHidden'
        } catch {Source 'NetworkInventory' $false ('Get-NetAdapter: '+$_.Exception.HResult)}
        @{Rows=@($rows.ToArray());Sources=@($sources.ToArray())} | ConvertTo-Json -Depth 6 -Compress
        """;
}
