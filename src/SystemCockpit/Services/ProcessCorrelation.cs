using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Xml.Linq;
using AetherOperations;

namespace SystemCockpit.Services;

internal sealed record ProcessRelation(string Kind, string Key, int? OwnerPid, string? Executable, string Value,
    string Source, DateTimeOffset Timestamp, DataAvailability Availability, DataClassification Classification = DataClassification.Sensitive)
{
    public string? LocalAddress { get; init; }
}

internal static class ProcessCorrelation
{
    internal static async Task<IReadOnlyList<ProcessRelation>> ReadAsync(ProcessIdentity identity, CancellationToken token)
    {
        using var handle = ProcessNative.Open(identity);
        var path = ProcessNative.Path(handle);
        var all = await ReadSystemAsync(token).ConfigureAwait(false);
        var result = all.Where(r => Matches(r, identity, path)).ToList();
        var gpu = (await ProcessGpu.ReadAsync(token).ConfigureAwait(false)).Where(r => r.OwnerPid == identity.Pid || r.OwnerPid is null).ToArray();
        result.AddRange(gpu);
        if (gpu.Length == 0) result.Add(new("GPU", "no-instance", identity.Pid, null,
            "Keine gültige GPU-Engineinstanz für diese PID in der Probe; keine Nullauslastung daraus abgeleitet.",
            "Windows GPU Engine / PDH", DateTimeOffset.UtcNow, DataAvailability.Unavailable));
        result.Add(new("Datei", identity.ToString(), identity.Pid, path, path, "QueryFullProcessImageName / identitätsgebundenes Handle",
            DateTimeOffset.UtcNow, DataAvailability.Available));
        result.AddRange(await Task.Run(() => Events(identity, token), token).ConfigureAwait(false));
        if (ProcessNative.Exited(handle)) throw new InvalidOperationException("Prozess während der Korrelation beendet; PID-Beziehungen verworfen.");
        result.Add(new("Hardware / Sensorwirkung", "not-causal", null, null,
            "GPU-Enginebesitz wird separat über PID/LUID gemeldet. Daraus folgt keine Temperatur-/Leistungsursache; globale Hardwarewerte werden keinem Prozess zugeschrieben.",
            "M3-Sensoren enthalten keine kausalen Prozesskanten", DateTimeOffset.UtcNow, DataAvailability.Unsupported));
        result.Add(new("Klassische installierte Software", "software-boundary", null, null,
            "AppX-Manifestzuordnungen stehen separat. Klassische Uninstall-InstallLocation belegt keine Dateieigentümerschaft; ohne exakte Komponentenidentität keine Zuordnung.",
            "Vorhandenes Uninstall-Registry-Inventar", DateTimeOffset.UtcNow, DataAvailability.Unavailable));
        result.Add(new("Absturzberichte", "wer-boundary", null, null,
            "WER-Dateinamen und App-Namen sind keine PID-/Erstellungszeitidentität. Strukturierte Application-Ereignisse stehen separat.",
            "Windows Error Reporting / Application EventData", DateTimeOffset.UtcNow, DataAvailability.Unavailable));
        return result;
    }

    internal static bool Matches(ProcessRelation relation, ProcessIdentity identity, string path) =>
        relation.Availability != DataAvailability.Available ||
        relation.OwnerPid == identity.Pid ||
        relation.Executable is not null && System.IO.Path.IsPathFullyQualified(relation.Executable) &&
            string.Equals(relation.Executable, path, StringComparison.OrdinalIgnoreCase);

    internal static async Task<IReadOnlyList<ProcessRelation>> ReadSystemAsync(CancellationToken token)
    {
        var response = await AdminShell.RunAsync(SystemScript, TimeSpan.FromSeconds(35), token).ConfigureAwait(false);
        if (response.ExitCode != 0 || response.OutputTruncated) throw new InvalidOperationException("Systemkorrelation fehlgeschlagen oder abgeschnitten; keine vollständige Snapshotquelle.");
        var raw = JsonSerializer.Deserialize<RawRelation[]>(response.Output) ?? throw new JsonException("Korrelationsdaten fehlen.");
        if (raw.Length > 20000) throw new JsonException("Zu viele Korrelationszeilen.");
        var time = DateTimeOffset.UtcNow;
        var result = raw.Select(r => new ProcessRelation(r.Kind, r.Key, r.OwnerPid, r.Executable, r.Value, r.Source, r.Timestamp,
            Enum.TryParse<DataAvailability>(r.Availability, out var state) && Enum.IsDefined(state) ? state : throw new JsonException("Ungültiger Quellenstatus."))
            { LocalAddress = r.LocalAddress }).ToList();
        var adapters = new List<(string Address, string Id, string Name)>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try { adapters.AddRange(adapter.GetIPProperties().UnicastAddresses.Select(address => (address.Address.ToString(), adapter.Id, adapter.Name))); }
            catch (NetworkInformationException ex)
            {
                Trace.TraceWarning("Prozess-Adapterkorrelation: {0}", ex);
                result.Add(new("Netzwerkadapter", "provider", null, null, $"Win32 {ex.NativeErrorCode}",
                    "NetworkInterface.GetIPProperties", time, ex.NativeErrorCode == 5 ? DataAvailability.AccessDenied : DataAvailability.ProviderError));
            }
        }
        foreach (var endpoint in result.Where(r => r.LocalAddress is not null).ToArray())
        {
            var matches = adapters.Where(a => a.Address == endpoint.LocalAddress).ToArray();
            if (matches.Length != 1) continue;
            result.Add(new("Netzwerkadapter", matches[0].Id, endpoint.OwnerPid, null,
                matches[0].Name + " / lokale Adresse " + endpoint.LocalAddress,
                "Exakte eindeutige Unicastadresse des Owner-PID-Endpoints; keine Routing-/Wildcardheuristik", time, DataAvailability.Available));
        }
        return result;
    }

    internal const string SystemScript = """
            $ErrorActionPreference='Stop'
            $rows=[Collections.Generic.List[object]]::new()
            function Row($kind,$key,$pidValue,$exe,$value,$source,$status='Available',$local=$null) {
                if($rows.Count -ge 20000){throw 'Korrelationsgrenze erreicht'}
                $rows.Add(@{Kind=$kind;Key=[string]$key;OwnerPid=$pidValue;Executable=$exe;Value=[string]$value;Source=$source;Availability=$status;LocalAddress=$local;Timestamp=[DateTimeOffset]::UtcNow.ToString('O')})
            }
            function ExePath($command) {
                $m=[regex]::Match([string]$command,'^\s*"([^"]+\.exe)"(?:\s|$)','IgnoreCase')
                if(-not $m.Success){$m=[regex]::Match([string]$command,'^\s*([^\s"]+\.exe)(?:\s|$)','IgnoreCase')}
                if($m.Success){return ExactPath $m.Groups[1].Value}
                return $null
            }
            function ExactPath($value) {
                $v=[Environment]::ExpandEnvironmentVariables([string]$value)
                if($v -match '^"([^"]+)"$'){$v=$matches[1]}
                if($v -notmatch '^[A-Za-z]:\\' -or $v.IndexOfAny([IO.Path]::GetInvalidPathChars()) -ge 0 -or $v.Contains('*') -or $v.Contains('?')){return $null}
                try {return [IO.Path]::GetFullPath($v)} catch [ArgumentException] {return $null}
            }
            function FailureState($failure) {
                if($failure.Exception -is [UnauthorizedAccessException] -or $failure.Exception.HResult -eq -2147024891 -or
                    $failure.FullyQualifiedErrorId -match 'AccessDenied|UnauthorizedAccess|PermissionDenied'){return 'AccessDenied'}
                return 'ProviderError'
            }
            try {
                Get-CimInstance Win32_Service -OperationTimeoutSec 6 | ForEach-Object {
                    Row 'Dienst' $_.Name ([int]$_.ProcessId) $null ($_.Name+' / '+$_.State+' / Starttyp: '+$_.StartMode) 'Win32_Service.ProcessId/Name/StartMode'
                }
            } catch {Row 'Dienst' 'provider' $null $null ('Providerfehler: '+$_.Exception.HResult) 'Win32_Service' (FailureState $_)}
            try {
                Get-NetTCPConnection -ErrorAction Stop | ForEach-Object {
                    $key=$_.LocalAddress+':'+$_.LocalPort+' -> '+$_.RemoteAddress+':'+$_.RemotePort
                    Row 'TCP' $key ([int]$_.OwningProcess) $null ($key+' / '+$_.State) 'Get-NetTCPConnection / Owner-PID' 'Available' $_.LocalAddress
                }
            } catch {Row 'TCP' 'provider' $null $null ('Providerfehler: '+$_.Exception.HResult) 'Get-NetTCPConnection' (FailureState $_)}
            try {
                Get-NetUDPEndpoint -ErrorAction Stop | ForEach-Object {
                    $key=$_.LocalAddress+':'+$_.LocalPort
                    Row 'UDP' $key ([int]$_.OwningProcess) $null ($key+' / lokaler Endpoint, kein Remoteziel') 'Get-NetUDPEndpoint / Owner-PID' 'Available' $_.LocalAddress
                }
            } catch {Row 'UDP' 'provider' $null $null ('Providerfehler: '+$_.Exception.HResult) 'Get-NetUDPEndpoint' (FailureState $_)}
            try {
                foreach($root in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce',
                    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run',
                    'HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce')) {
                    if(-not (Test-Path -LiteralPath $root)){continue}
                    $key=Get-Item -LiteralPath $root
                    foreach($name in $key.GetValueNames()){
                        $exe=ExePath ($key.GetValue($name))
                        Row 'Autostart' ($root+'\'+$name) $null $exe $(if($exe){$exe}else{'Ausführbare Datei nicht eindeutig auflösbar; keine Zuordnung'}) 'Registry Run/RunOnce / exakter ausführbarer Pfad'
                    }
                }
            } catch {Row 'Autostart' 'provider' $null $null ('Providerfehler: '+$_.Exception.HResult) 'Run/RunOnce' (FailureState $_)}
            try {
                Get-ScheduledTask -ErrorAction Stop | ForEach-Object {
                    $task=$_
                    foreach($action in $task.Actions) {
                        $exe=ExactPath $action.Execute
                        Row 'Geplante Aufgabe' ($task.TaskPath+$task.TaskName) $null $exe $(if($exe){$exe}else{'Keine eindeutige absolute Executable-Angabe; keine Prozesszuordnung'}) 'Get-ScheduledTask.Actions.Execute / exakter Pfad'
                    }
                }
            } catch {Row 'Geplante Aufgabe' 'provider' $null $null ('Providerfehler: '+$_.Exception.HResult) 'Get-ScheduledTask' (FailureState $_)}
            try {
                Get-AppxPackage -ErrorAction Stop | ForEach-Object {
                    $package=$_
                    $manifest=Get-AppxPackageManifest -Package $package.PackageFullName -ErrorAction Stop
                    foreach($app in $manifest.Package.Applications.Application) {
                        if($app.Executable -and $package.InstallLocation) {
                            $exe=[IO.Path]::GetFullPath((Join-Path $package.InstallLocation ([string]$app.Executable)))
                            Row 'Softwarepaket' ($package.PackageFullName+'/'+$app.Id) $null $exe $package.PackageFullName 'AppX-Paketmanifest / Applications.Application.Executable + InstallLocation'
                        }
                    }
                }
            } catch {Row 'Softwarepaket' 'provider' $null $null ('Providerfehler: '+$_.Exception.HResult) 'Get-AppxPackageManifest' (FailureState $_)}
            ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 4 -Compress
            """;

    private sealed record RawRelation(string Kind, string Key, int? OwnerPid, string? Executable, string Value, string Source, string Availability, string? LocalAddress, DateTimeOffset Timestamp);
    internal static IReadOnlyList<ProcessRelation> Events(ProcessIdentity identity, CancellationToken token)
    {
        var result = new List<ProcessRelation>();
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName,
                "*[System[TimeCreated[timediff(@SystemTime) <= 3600000]]]") { ReverseDirection = true });
            for (var index = 0; index < 256; index++)
            {
                token.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent(TimeSpan.FromMilliseconds(100));
                if (record is null) break;
                if (record.TimeCreated is not { } created || created.ToUniversalTime().Ticks < identity.StartTicks) continue;
                var xml = XDocument.Parse(record.ToXml());
                if (!EventTargets(xml, identity.Pid)) continue;
                result.Add(new("Ereignis", record.RecordId?.ToString(CultureInfo.InvariantCulture) ?? "", identity.Pid, null,
                    $"Application / Event {record.Id} / {created:O} / strukturierte Ziel-PID innerhalb beobachteter Lebenszeit",
                    "Windows EventData (nicht System.Execution.ProcessID)", DateTimeOffset.UtcNow, DataAvailability.Available));
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Trace.TraceError("Prozessereignisse: {0}", ex);
            result.Add(new("Ereignis", "provider", null, null, ex.GetType().Name, "Application EventData",
                DateTimeOffset.UtcNow, ex is UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.ProviderError));
        }
        return result;
    }
    internal static bool EventTargets(XDocument xml, int pid)
    {
        var system = xml.Descendants().SingleOrDefault(n => n.Name.LocalName == "System");
        var provider = system?.Elements().SingleOrDefault(n => n.Name.LocalName == "Provider")?.Attribute("Name")?.Value;
        var eventId = system?.Elements().SingleOrDefault(n => n.Name.LocalName == "EventID")?.Value;
        if (!(provider == "Application Error" && eventId == "1000" || provider == "Application Hang" && eventId == "1002")) return false;
        return xml.Descendants().Where(n => n.Name.LocalName == "EventData").Elements().Any(n =>
            n.Name.LocalName == "Data" && n.Attribute("Name")?.Value is "ProcessId" or "ProcessID" &&
            TryPid(n.Value, out var value) && value == pid);
    }
    private static bool TryPid(string value, out int pid) => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pid)
        : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out pid);
}
