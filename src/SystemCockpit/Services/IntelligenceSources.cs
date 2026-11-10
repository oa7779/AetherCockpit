using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Security.Principal;
using AetherOperations;
using Microsoft.Win32;

namespace SystemCockpit.Services;

internal static class IntelligenceSources
{
    private static readonly Lazy<string?> HostIdentity = new(() =>
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            using var identity = WindowsIdentity.GetCurrent();
            if (key?.GetValue("MachineGuid") is string text && Guid.TryParse(text, out var guid) && identity.User?.Value is { Length: > 0 } sid)
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(guid.ToString("D") + "/" + sid)));
            Trace.TraceError("Intelligence-Rechnerbezug: Windows MachineGuid oder Benutzerkontext fehlt oder ist ungültig.");
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { Trace.TraceError("Intelligence-Rechnerbezug: {0}", ex); return null; }
    });
    internal static IntelligenceEntity Entity(string id, IntelligenceKind kind, string name, string domain,
        string source, DateTimeOffset time, IReadOnlyDictionary<string, string>? fields = null,
        DataAvailability state = DataAvailability.Available) =>
        new(id, kind, name, new Dictionary<string, string>(fields ?? new Dictionary<string, string>()) { ["domain"] = domain },
            IntelligenceModel.Metadata(source, time, state));

    internal static IntelligenceSnapshot Live(DashboardSnapshot? dashboard, LiveSample? live, HardwareInventory? hardware, string name, bool synthetic = false)
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
            foreach (var sensor in live.Sensors.Where(s => s.Value is not null && !SensorSemantics.IsTemperatureLimit(s)))
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
                var pnp = component.Facts.FirstOrDefault(f => f.Key == "PNPDeviceID" && f.Observation.HasValue &&
                    f.Observation.Metadata.Availability == DataAvailability.Available)?.Observation.Value;
                if (pnp is not null) values["pnpDeviceId"] = pnp;
                var luid = component.Facts.FirstOrDefault(f => f.Key == "luid" && f.Observation.HasValue &&
                    f.Observation.Metadata.Availability == DataAvailability.Available)?.Observation.Value;
                if (luid is not null) values["adapterLuid"] = luid;
                entities.Add(Entity("hardware:" + component.Id, IntelligenceKind.Hardware, component.Name, "Hardware",
                    "Vorhandener M3-Inventarzustand; Einzelquellen in Hardwareinspektor", hardware.GeneratedAt, values,
                    component.Facts.Any(f => f.Observation.Metadata.Availability != DataAvailability.Available) ? DataAvailability.Unknown : DataAvailability.Available));
            }
            var hardwareIds = hardware.Components.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var component in hardware.Components.Where(c => c.ParentId is { } parent && hardwareIds.Contains(parent)))
                edges.Add(new("hardware:" + component.ParentId, "hardware:" + component.Id, "Vorhandene M3-Provider-/Gerätebeziehung",
                    IntelligenceModel.Metadata("M3 ParentId; genaue Belege im Hardwareinspektor", hardware.GeneratedAt)));
            foreach (var adapter in dashboard?.Adapters ?? [])
            {
                var id = HardwareIntelligenceReader.Identity("net", adapter.Id);
                if (hardwareIds.Contains(id))
                    edges.Add(new("adapter:" + adapter.Id, "hardware:" + id, "Identische Windows-Interface-ID",
                        IntelligenceModel.Metadata("NetworkInterface.Id", time)));
            }
            foreach (var sensor in live?.Sensors ?? [])
            {
                var id = HardwareIntelligenceReader.Identity("sensor", sensor.Id);
                if (sensor.Value is not null && !SensorSemantics.IsTemperatureLimit(sensor) && hardwareIds.Contains(id))
                    edges.Add(new("sensor:" + sensor.Id, "hardware:" + id, "Identische vorhandene Sensor-ID",
                        IntelligenceModel.Metadata("Gemeinsamer Sensorpfad / M3-Sensoridentität", time)));
            }
            coverage.Add(new("Hardware", false, IntelligenceModel.Metadata("Klassenweise erfasst, kein vollständiger Bestand", hardware.GeneratedAt)));
        }
        var observed = DateTimeOffset.UtcNow;
        var current = entities.GroupBy(e => e.Id).Select(g => g.First()).Select(e =>
            e.Kind is IntelligenceKind.Process or IntelligenceKind.Resource or IntelligenceKind.Sensor or IntelligenceKind.Adapter &&
            e.Metadata.Availability == DataAvailability.Available && observed - e.Metadata.Timestamp > TimeSpan.FromSeconds(10)
                ? e with { Metadata = e.Metadata.AsStale("Gemeinsame Probe älter als 10 Sekunden.") } : e).ToArray();
        var host = synthetic ? "synthetic" : HostIdentity.Value;
        coverage.Add(new("HostScope", host is not null, IntelligenceModel.Metadata("SHA-256 aus Windows-MachineGuid und Benutzer-SID; sensibler lokaler Kontextbezug",
            observed, host is null ? DataAvailability.Unknown : DataAvailability.Available)));
        return new(1, Guid.NewGuid(), name, observed, current, edges, coverage.Select(c => current.Any(e =>
            IntelligenceModel.Domain(e) == c.Domain && e.Metadata.Availability == DataAvailability.Stale) ? c with { Complete = false } : c).ToArray(), host);
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
                entities.Add(Entity("software:" + row.Id.ToUpperInvariant(), IntelligenceKind.Software, row.Name, "Software", "Uninstall-Registry-Identität", start,
                    new Dictionary<string, string> { ["version"] = row.Version, ["publisher"] = row.Publisher, ["location"] = row.Location,
                        ["installDateReported"] = row.InstallDate, ["architecture"] = row.Architecture, ["fileOwnership"] = "Nicht bewiesen" }));
            Cover("Software", true, "HKLM/HKCU Uninstall; vorhandene Inventarfilter");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { Trace.TraceError("Software Intelligence: {0}", ex); Cover("Software", false, ex.GetType().Name, ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError); }
        var relations = (await ProcessCorrelation.ReadSystemAsync(token).ConfigureAwait(false))
            .Concat(await ProcessGpu.ReadAsync(token).ConfigureAwait(false)).ToArray();
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
                "Netzwerkadapter" => IntelligenceKind.Adapter, "GPU" => IntelligenceKind.Resource, _ => IntelligenceKind.Endpoint };
            var domain = group.Key;
            foreach (var rows in group.Where(r => r.Availability == DataAvailability.Available).GroupBy(r =>
                r.Key + (r.Kind is "TCP" or "UDP" or "Netzwerkadapter" or "GPU" ? ":" + r.OwnerPid : "") + (r.GpuEngine ?? "")))
            {
                var row = rows.First();
                var process = row.OwnerPid is { } owner ? verified.GetValueOrDefault(owner) : null;
                var stableKey = kind is IntelligenceKind.Service or IntelligenceKind.Startup or IntelligenceKind.Task ? rows.Key.ToUpperInvariant() : rows.Key;
                var id = "relation:" + domain + ":" + stableKey + (kind == IntelligenceKind.Endpoint ? ":" + (process?.Id ?? "unverified") : "");
                var f = new Dictionary<string, string> { ["value"] = string.Join(" | ", rows.Select(r => r.Value).Distinct()), ["key"] = row.Key };
                if (rows.Any(r => r.Executable is not null)) f["executable"] = string.Join(" | ", rows.Select(r => r.Executable).Where(p => p is not null).Distinct());
                if (row.OwnerPid is { } pid) f["ownerPid"] = pid.ToString(CultureInfo.InvariantCulture);
                if (row.GpuEngine is { } engine)
                {
                    f["gpuEngine"] = engine; f["adapterLuid"] = row.Key;
                    if (row.GpuPercent is { } percent) f["gpuPercent"] = percent.ToString("R", CultureInfo.InvariantCulture);
                    if (row.GpuSampleStart is { } sampleStart) f["sampleStart"] = sampleStart.ToString("O");
                }
                if (row.LocalAddress is { } localAddress) f["localAddress"] = localAddress;
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
        var inventory = ParseInventory(response.Output);
        entities.AddRange(inventory.Entities);
        coverage.AddRange(inventory.Coverage);
        ClassifyDrivers(entities, inventory.Entities);
        var events = await Task.Run(() => IntelligenceWindowsEvents.Read(token), token).ConfigureAwait(false);
        entities.AddRange(events.Entities); coverage.AddRange(events.Coverage);
        var availableIds = entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in events.Entities)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Fields.GetValueOrDefault("provider") == "Microsoft-Windows-Kernel-PnP" &&
                entry.Fields.GetValueOrDefault("DeviceInstanceId") is { Length: > 0 } deviceId)
            {
                var target = "device:" + deviceId.ToUpperInvariant();
                if (availableIds.Contains(target)) edges.Add(new(entry.Id, target,
                    "Kernel-PnP-Ereignis berichtet dieselbe Geräteinstanz; kein Ursachenbeweis", entry.Metadata));
            }
            if (!IntelligenceWindowsEvents.ProcessTarget(entry, out var pid) || !verified.TryGetValue(pid, out var process) ||
                !long.TryParse(process.Fields.GetValueOrDefault("start"), out var ticks) || entry.Metadata.Timestamp.UtcTicks < ticks ||
                entry.Metadata.Timestamp > DateTimeOffset.UtcNow) continue;
            try
            {
                using var handle = ProcessNative.Open(new(pid, ticks));
                if (!ProcessNative.Exited(handle)) edges.Add(new(entry.Id, process.Id,
                    "Application-Ereignis berichtet PID innerhalb erneut geprüfter Prozesslebenszeit", entry.Metadata));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            { Trace.TraceWarning("Ereignis-Prozessidentität nicht mehr belegbar: {0}", ex.Message); }
        }
        foreach (var driver in entities.Where(e => e.Kind == IntelligenceKind.Driver))
        {
            if (driver.Fields.TryGetValue("deviceId", out var device))
            {
                var pnp = "device:" + device.ToUpperInvariant();
                if (availableIds.Contains(pnp)) edges.Add(new(driver.Id, pnp, "Exakte PnP-Geräteinstanz", driver.Metadata));
            }
            if (driver.Fields.TryGetValue("service", out var service) && service.Length > 0)
            {
                var target = entities.SingleOrDefault(e => e.Kind == IntelligenceKind.Service &&
                    string.Equals(e.Fields.GetValueOrDefault("key"), service, StringComparison.OrdinalIgnoreCase));
                if (target is not null) edges.Add(new(driver.Id, target.Id, "PnP-Treiberdienst", driver.Metadata));
            }
        }
        foreach (var node in entities.Where(e => e.Kind == IntelligenceKind.Hardware && e.Fields.ContainsKey("pnpDeviceId")))
        {
            var target = entities.SingleOrDefault(e => e.Id.StartsWith("device:", StringComparison.Ordinal) &&
                string.Equals(e.Fields.GetValueOrDefault("deviceId"), node.Fields["pnpDeviceId"], StringComparison.OrdinalIgnoreCase));
            if (target is not null) edges.Add(new(node.Id, target.Id, "Exakte vorhandene PnP-Geräteinstanz", node.Metadata));
        }
        foreach (var service in entities.Where(e => IntelligenceModel.Domain(e) == "KernelService").ToArray())
            if (service.Fields.TryGetValue("path", out var path) && DriverPath(path) is { } local)
                edges.Add(new(service.Id, FileEntity(local, service.Metadata.Timestamp, service.Metadata.Source),
                    "Exakt aufgelöster Windows-Treiberpfad; Signatur separat auf Anforderung", service.Metadata));
        foreach (var gpu in entities.Where(e => IntelligenceModel.Domain(e) == "GPU"))
            foreach (var adapter in entities.Where(e => e.Kind == IntelligenceKind.Hardware &&
                gpu.Fields.GetValueOrDefault("adapterLuid") is { } luid && e.Fields.GetValueOrDefault("adapterLuid") == luid))
                edges.Add(new(gpu.Id, adapter.Id, "Identische Adapter-LUID aus GPU Engine und vorhandener DXGI-Probe",
                    IntelligenceModel.Metadata("PID / LUID / Engine; keine Sensorursache", gpu.Metadata.Timestamp)));
        foreach (var endpoint in entities.Where(e => e.Kind == IntelligenceKind.Endpoint))
        {
            if (!endpoint.Fields.TryGetValue("localAddress", out var address)) continue;
            var candidates = relations.Where(r => r.Kind == "Netzwerkadapter" && r.LocalAddress == address)
                .Select(r => r.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length != 1) continue;
            var adapter = entities.FirstOrDefault(e => e.Kind == IntelligenceKind.Adapter &&
                Guid.TryParse(e.Fields.GetValueOrDefault("interfaceId"), out var a) && Guid.TryParse(candidates[0], out var b) && a == b);
            if (adapter is not null) edges.Add(new(endpoint.Id, adapter.Id, "Exakte eindeutige lokale Unicastadresse; kein Raten-/Routingbezug",
                IntelligenceModel.Metadata("NetworkInterface.GetIPProperties / lokaler Endpoint", endpoint.Metadata.Timestamp)));
        }
        var result = live with { Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow,
            Entities = entities.GroupBy(e => e.Id).Select(g => g.First()).ToArray(),
            Edges = edges.DistinctBy(e => (e.From, e.To, e.Relation)).ToArray(), Coverage = coverage.DistinctBy(c => c.Domain).ToArray() };
        IntelligenceModel.Validate(result);
        return result;
    }
    internal static void ClassifyDrivers(List<IntelligenceEntity> entities, IReadOnlyList<IntelligenceEntity> inventory)
    {
        var wireless = inventory.Where(e => e.Kind == IntelligenceKind.Adapter &&
                !string.IsNullOrWhiteSpace(e.Fields.GetValueOrDefault("pnpDeviceId")))
            .GroupBy(e => e.Fields["pnpDeviceId"], StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1 && g.Single().Fields.GetValueOrDefault("physicalMediumCode") is "1" or "9")
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entities.Count; index++)
        {
            var driver = entities[index];
            if (driver.Kind != IntelligenceKind.Driver || driver.Fields.GetValueOrDefault("deviceId") is not { } deviceId ||
                !wireless.TryGetValue(deviceId, out var adapter)) continue;
            entities[index] = driver with { Fields = new Dictionary<string, string>(driver.Fields)
            {
                ["functionalGroup"] = "WLAN",
                ["functionalGroupSource"] = "Get-NetAdapter.NdisPhysicalMedium=" + adapter.Fields["physicalMediumCode"] + " / exakte PnPDeviceID"
            } };
        }
    }

    internal static string? DriverPath(string path)
    {
        var value = path.Trim();
        if (value.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), value[12..]);
        if (value.StartsWith(@"\??\", StringComparison.Ordinal)) value = value[4..];
        if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal) || value.Contains('"')) return null;
        try { return Path.GetFullPath(value); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        { Trace.TraceWarning("Treiberpfad nicht auflösbar: {0}", ex.Message); return null; }
    }

    private sealed record InventoryRows(InventoryRow[] Rows, InventorySource[] Sources);
    private sealed record InventoryRow(string Id, string Kind, string Domain, string Name, string Source, DateTimeOffset Timestamp, Dictionary<string, string> Fields);
    private sealed record InventorySource(string Domain, bool Complete, string Source);
    internal static (IReadOnlyList<IntelligenceEntity> Entities, IReadOnlyList<SourceCoverage> Coverage) ParseInventory(string json)
    {
        var inventory = JsonSerializer.Deserialize<InventoryRows>(json) ?? throw new JsonException("Inventur fehlt.");
        var domains = new Dictionary<string, IntelligenceKind>(StringComparer.Ordinal)
        {
            ["PnP"] = IntelligenceKind.Hardware, ["Driver"] = IntelligenceKind.Driver, ["KernelService"] = IntelligenceKind.Service,
            ["ReliabilityIndex"] = IntelligenceKind.Resource, ["Boot"] = IntelligenceKind.Boot, ["NetworkInventory"] = IntelligenceKind.Adapter
        };
        if (inventory.Rows is null || inventory.Sources is null || inventory.Rows.Length > 12000 ||
            inventory.Sources.Length != domains.Count || inventory.Sources.Any(s => s is null || s.Domain is null || !domains.ContainsKey(s.Domain) ||
                string.IsNullOrWhiteSpace(s.Source)) || inventory.Sources.Select(s => s.Domain).Distinct().Count() != domains.Count)
            throw new JsonException("Inventurzeilen oder erwartete Quellenabdeckung ungültig.");
        var entities = new List<IntelligenceEntity>(inventory.Rows.Length);
        foreach (var row in inventory.Rows)
        {
            if (row is null || row.Domain is null || !domains.TryGetValue(row.Domain, out var expected) ||
                !Enum.TryParse<IntelligenceKind>(row.Kind, out var kind) || kind != expected || row.Fields is null ||
                string.IsNullOrWhiteSpace(row.Source)) throw new JsonException("Ungültige Inventurklasse oder Einzelquelle.");
            entities.Add(Entity(row.Id, kind, row.Name, row.Domain, row.Source, row.Timestamp, row.Fields));
        }
        var coverage = inventory.Sources.Select(s => new SourceCoverage(s.Domain, s.Complete,
            IntelligenceModel.Metadata(s.Source, DateTimeOffset.UtcNow, s.Complete ? DataAvailability.Available : DataAvailability.ProviderError))).ToArray();
        IntelligenceModel.Validate(new(1, Guid.NewGuid(), "Quellenvalidierung", DateTimeOffset.UtcNow, entities, [], coverage));
        return (entities, coverage);
    }
    internal const string InventoryScript = """
        $ErrorActionPreference='Stop'
        $rows=[Collections.Generic.List[object]]::new(); $sources=[Collections.Generic.List[object]]::new()
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        function Row($id,$kind,$domain,$name,$source,$fields) {
            if($rows.Count -ge 12000){throw 'Inventurgrenze erreicht'}
            if([string]::IsNullOrWhiteSpace($id) -or -not $seen.Add([string]$id)){throw 'Inventuridentitaet fehlt oder ist doppelt'}
            $rows.Add(@{Id=[string]$id;Kind=$kind;Domain=$domain;Name=[string]$name;Source=$source;Timestamp=[DateTimeOffset]::UtcNow.ToString('O');Fields=$fields})
        }
        function Source($domain,$complete,$text){$sources.Add(@{Domain=$domain;Complete=$complete;Source=$text})}
        try {
            $devices=@(Get-CimInstance Win32_PnPEntity -OperationTimeoutSec 8)
            $byId=@{}; foreach($d in $devices) {
                if([string]::IsNullOrWhiteSpace($d.DeviceID)){throw 'PnP DeviceID fehlt'}
                $byId[[string]$d.DeviceID]=$d
                Row ('device:'+([string]$d.DeviceID).ToUpperInvariant()) 'Hardware' 'PnP' $d.Name 'Win32_PnPEntity' @{
                    deviceId=[string]$d.DeviceID;class=[string]$d.PNPClass;service=[string]$d.Service;
                    hardwareIds=([string]::Join(' | ',@($d.HardwareID)));status=[string]$d.Status;problem=[string]$d.ConfigManagerErrorCode
                }
            }
            Source 'PnP' $true 'Win32_PnPEntity'
        } catch {$byId.Clear(); Source 'PnP' $false ('Win32_PnPEntity: '+$_.Exception.HResult)}
        try {
            Get-CimInstance Win32_PnPSignedDriver -OperationTimeoutSec 8 | ForEach-Object {
                if([string]::IsNullOrWhiteSpace($_.DeviceID)){throw 'Driver DeviceID fehlt'}
                $service=''; if($byId.ContainsKey([string]$_.DeviceID)){$service=[string]$byId[[string]$_.DeviceID].Service}
                Row ('driver:'+([string]$_.DeviceID).ToUpperInvariant()) 'Driver' 'Driver' $_.DeviceName 'Win32_PnPSignedDriver / DeviceID' @{
                    deviceId=[string]$_.DeviceID;class=[string]$_.DeviceClass;provider=[string]$_.DriverProviderName;
                    version=[string]$_.DriverVersion;date=[string]$_.DriverDate;inf=[string]$_.InfName;
                    signed=[string]$_.IsSigned;signer=[string]$_.Signer;service=$service
                }
            }
            Source 'Driver' $true 'Win32_PnPSignedDriver'
        } catch {Source 'Driver' $false ('Win32_PnPSignedDriver: '+$_.Exception.HResult)}
        try {
            Get-CimInstance Win32_SystemDriver -OperationTimeoutSec 8 | ForEach-Object {
                if([string]::IsNullOrWhiteSpace($_.Name)){throw 'Kernel-Serviceidentitaet fehlt'}
                Row ('kernelservice:'+([string]$_.Name).ToUpperInvariant()) 'Service' 'KernelService' $_.DisplayName 'Win32_SystemDriver.Name' @{
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
            $os=@(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 8)
            if($os.Count -ne 1){throw 'Windows-Startquelle ist nicht eindeutig'}
            foreach($state in $os) {
                Row 'boot:current' 'Boot' 'Boot' 'Windows-Start' 'Win32_OperatingSystem.LastBootUpTime' @{
                    bootUtc=$state.LastBootUpTime.ToUniversalTime().ToString('O');localDateTime=$state.LocalDateTime.ToUniversalTime().ToString('O')
                }
            }
            Get-CimInstance Win32_LogonSession -Filter 'LogonType=2 OR LogonType=10' -OperationTimeoutSec 8 | ForEach-Object {
                Row ('logon:'+$_.LogonId+':'+$_.StartTime.ToUniversalTime().Ticks) 'Boot' 'Boot' ('Interaktive Sitzung '+$_.LogonId) 'Win32_LogonSession' @{
                    startUtc=$_.StartTime.ToUniversalTime().ToString('O');logonType=[string]$_.LogonType
                }
            }
            Source 'Boot' $true 'Win32_OperatingSystem / Win32_LogonSession'
        } catch {Source 'Boot' $false ('Boot-/Sitzungsquelle: '+$_.Exception.HResult)}
        try {
            Get-NetAdapter -IncludeHidden -ErrorAction Stop | ForEach-Object {
                Row ('netadapter:'+$_.InterfaceGuid) 'Adapter' 'NetworkInventory' $_.Name 'Get-NetAdapter / InterfaceGuid' @{
                    interfaceId=[string]$_.InterfaceGuid;status=[string]$_.Status;linkSpeed=[string]$_.LinkSpeed;
                    driverVersion=[string]$_.DriverVersion;description=[string]$_.InterfaceDescription;pnpDeviceId=[string]$_.PnPDeviceID;
                    physicalMediumCode=$(if($null -ne $_.NdisPhysicalMedium){[string][int]$_.NdisPhysicalMedium}else{''})
                }
            }
            Source 'NetworkInventory' $true 'Get-NetAdapter -IncludeHidden'
        } catch {Source 'NetworkInventory' $false ('Get-NetAdapter: '+$_.Exception.HResult)}
        @{Rows=@($rows.ToArray());Sources=@($sources.ToArray())} | ConvertTo-Json -Depth 6 -Compress
        """;
}
