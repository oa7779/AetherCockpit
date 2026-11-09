using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace SystemCockpit.Services;

public sealed class WindowsProbe
{
    public async Task<ProbeReport> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) })
            process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("Windows-Abfrage konnte nicht starten.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = ReadErrorStream(await stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Windows-Abfrage: Exit {process.ExitCode}. {error}");
            if (!string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException($"Windows-Abfrage meldet Fehler: {error}");
            return JsonSerializer.Deserialize<ProbeReport>(output,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Windows-Abfrage lieferte keine Daten.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Windows-Abfrage hat das Zeitlimit von 45 Sekunden erreicht.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
        }
    }

    public static string ReadErrorStream(string stream)
    {
        if (string.IsNullOrWhiteSpace(stream)) return "";
        const string marker = "#< CLIXML";
        if (!stream.StartsWith(marker, StringComparison.Ordinal)) return stream;
        using var text = new System.IO.StringReader(stream[marker.Length..].TrimStart());
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(reader);
        XNamespace ns = "http://schemas.microsoft.com/powershell/2004/04";
        if (document.Root?.Name != ns + "Objs")
            throw new XmlException("Unerwartetes PowerShell-Streamformat.");
        // Windows PowerShell can emit module-initialization progress on stderr despite ProgressPreference.
        // Fehlertexte werden als lesbarer Klartext zurueckgegeben (CLIXML-Escapes wie _x000D_ dekodiert),
        // nicht als rohe XML-Elemente.
        return string.Join(Environment.NewLine, document.Root.Elements()
            .Where(element => !string.Equals((string?)element.Attribute("S"), "progress", StringComparison.OrdinalIgnoreCase))
            .Select(element => DecodeClixmlText(element.Value))
            .Where(text => text.Length > 0));
    }

    private static string DecodeClixmlText(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "_x([0-9A-Fa-f]{4})_",
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString()).TrimEnd();

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $ProgressPreference = 'SilentlyContinue'
        $WarningPreference = 'Stop'
        function Item($name, $value, $state, $detail) {
            @{ Name = [string]$name; Value = [string]$value; State = [string]$state; Detail = [string]$detail }
        }
        function Probe($name, [scriptblock]$read) {
            try { & $read } catch { Item $name 'Nicht verfuegbar' 'unknown' $_.Exception.Message }
        }
        $cpu = 'Nicht verfuegbar'; $gpu = 'Nicht verfuegbar'; $board = 'Nicht verfuegbar'
        $memory = 'Nicht verfuegbar'; $windows = 'Nicht verfuegbar'
        $checks = @()
        try {
            $cpu = ((Get-CimInstance Win32_Processor).Name -join ' + ')
            $gpu = ((Get-CimInstance Win32_VideoController).Name -join ' + ')
            $b = Get-CimInstance Win32_BaseBoard
            $board = "$($b.Manufacturer) $($b.Product)"
            $os = Get-CimInstance Win32_OperatingSystem
            $windows = "$($os.Caption) | Build $($os.Version)"
            $memory = '{0:N1} GB nutzbarer RAM' -f ($os.TotalVisibleMemorySize / 1MB)
        } catch { $checks += Item 'Hardware-Erkennung' 'Unvollstaendig' 'unknown' $_.Exception.Message }
        $checks += Probe 'Defender Echtzeitschutz' {
            $d = Get-MpComputerStatus
            $state = if ($d.AntivirusEnabled -and $d.RealTimeProtectionEnabled) { 'ok' } else { 'warning' }
            Item 'Defender Echtzeitschutz' "AV: $($d.AntivirusEnabled) | Echtzeit: $($d.RealTimeProtectionEnabled)" $state 'Defender-Status; alternative Antivirus-Produkte werden nicht bewertet.'
        }
        $checks += Probe 'Defender Signaturen' {
            $d = Get-MpComputerStatus
            if ($null -eq $d.AntivirusSignatureLastUpdated) { throw 'Kein Signaturdatum gemeldet.' }
            $age = ((Get-Date) - $d.AntivirusSignatureLastUpdated).TotalDays
            $state = if ($age -gt 3) { 'warning' } else { 'ok' }
            Item 'Defender Signaturen' $d.AntivirusSignatureLastUpdated.ToString('yyyy-MM-dd HH:mm') $state 'Warnung bei Signaturen aelter als 3 Tage; kein Malware-Scan.'
        }
        $checks += Probe 'Windows Firewall' {
            $profiles = @(Get-NetFirewallProfile)
            if ($profiles.Count -eq 0) { throw 'Keine Firewall-Profile gemeldet.' }
            $disabled = @($profiles | Where-Object { -not $_.Enabled })
            $state = if ($disabled.Count) { 'warning' } else { 'ok' }
            Item 'Windows Firewall' (($profiles | ForEach-Object { "$($_.Name): $($_.Enabled)" }) -join ' | ') $state 'Konfigurierte Windows-Profile, keine Bewertung einzelner Regeln.'
        }
        $checks += Probe 'Secure Boot' {
            $enabled = Confirm-SecureBootUEFI
            $state = if ($enabled) { 'ok' } else { 'warning' }
            Item 'Secure Boot' ([string]$enabled) $state 'Firmware-Abfrage; kann Administratorrechte erfordern.'
        }
        $checks += Probe 'BitLocker Systemlaufwerk' {
            $v = Get-BitLockerVolume -MountPoint $env:SystemDrive
            $state = if ([string]$v.ProtectionStatus -eq 'On') { 'ok' } else { 'warning' }
            Item 'BitLocker Systemlaufwerk' "$($v.VolumeStatus) | Schutz: $($v.ProtectionStatus)" $state 'Nur Status; keine Schluessel oder Wiederherstellungsdaten werden gelesen.'
        }
        $checks += Probe 'Letztes gemeldetes Windows-Update' {
            $last = Get-HotFix | Where-Object InstalledOn | Sort-Object InstalledOn -Descending | Select-Object -First 1
            if ($null -eq $last) { throw 'Kein Installationsdatum in Get-HotFix verfuegbar.' }
            Item 'Letztes gemeldetes Windows-Update' "$($last.HotFixID) | $($last.InstalledOn.ToString('yyyy-MM-dd'))" 'info' 'Lokales Installationsdatum. Kein Online-Abgleich; beweist nicht, dass Windows aktuell ist.'
        }
        $checks += Probe 'Neustart-Indikatoren' {
            $pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
                (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
            $state = if ($pending) { 'warning' } else { 'info' }
            Item 'Neustart-Indikatoren' ([string]$pending) $state 'CBS- und Windows-Update-Registryindikatoren; keine vollstaendige Neustarterkennung.'
        }
        $checks += Probe 'Windows Diagnosedaten' {
            $path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'
            $p = $null
            if (Test-Path $path) {
                $policy = Get-ItemProperty $path
                $p = $policy.PSObject.Properties['AllowTelemetry']
            }
            if ($null -eq $p) {
                Item 'Windows Diagnosedaten' 'Keine Richtlinie gesetzt' 'info' 'Windows verwendet die Systemeinstellung. Kein Nachweis tatsaechlicher Datenuebertragung.'
            } else {
                Item 'Windows Diagnosedaten' "AllowTelemetry = $($p.Value)" 'info' 'Konfigurierter Richtlinienwert, nicht effektiver Datenverkehr. Wert 0 schaltet auf Windows Pro Diagnosedaten nicht vollstaendig ab.'
            }
        }
        $checks += Probe 'Autostarts' {
            $count = @(Get-CimInstance Win32_StartupCommand).Count
            Item 'Autostarts' "$count Eintraege" 'info' 'Win32_StartupCommand; keine vollstaendige Liste von Diensten und geplanten Aufgaben.'
        }
        $checks += Probe 'TCP Verbindungen' {
            $connections = @(Get-NetTCPConnection)
            $active = @($connections | Where-Object State -eq Established).Count
            $listening = @($connections | Where-Object State -eq Listen).Count
            Item 'TCP Verbindungen' "$active verbunden | $listening lauschend" 'info' 'Lokale TCP-Zaehler; kein Mitschnitt, keine Bewertung von Verbindungszielen.'
        }
        $disks = @()
        try {
            foreach ($disk in Get-PhysicalDisk) {
                $state = if ([string]$disk.HealthStatus -eq 'Healthy') { 'ok' } elseif ([string]$disk.HealthStatus -eq 'Unknown') { 'unknown' } else { 'warning' }
                $detail = '{0:N0} GB | {1} | Windows Storage Health' -f ($disk.Size / 1GB), $disk.MediaType
                try {
                    $r = $disk | Get-StorageReliabilityCounter
                    if ($null -ne $r.Temperature -and $r.Temperature -gt 0) { $detail += " | $($r.Temperature) C" }
                    if ($null -ne $r.PowerOnHours) { $detail += " | $($r.PowerOnHours) h" }
                    if ($null -ne $r.Wear) { $detail += " | Verschleiss: $($r.Wear)%" }
                } catch { $detail += " | Detailwerte nicht verfuegbar: $($_.Exception.Message)" }
                $disks += Item $disk.FriendlyName ([string]$disk.HealthStatus) $state $detail
            }
        } catch { $disks += Item 'Storage Health' 'Nicht verfuegbar' 'unknown' $_.Exception.Message }
        $diagnostics = @()
        $diagnostics += Probe 'Crash Analyzer / 30 Tage' {
            $events = @()
            try {
                $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000,1001; StartTime = (Get-Date).AddDays(-30) } -MaxEvents 30)
            } catch {
                if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { throw }
            }
            if ($events.Count -eq 0) {
                Item 'Crash Analyzer / 30 Tage' 'Keine passenden Eintraege' 'info' 'Application-Log, IDs 1000/1001. Kein Nachweis vollstaendiger Absturzfreiheit.'
            } else {
                foreach ($event in $events) {
                    $message = [string]$event.Message
                    if ($message.Length -gt 1400) { $message = $message.Substring(0,1400) + ' ...' }
                    Item "Crash / $($event.ProviderName)" "$($event.TimeCreated.ToString('yyyy-MM-dd HH:mm')) | ID $($event.Id)" 'warning' $message
                }
            }
        }
        $diagnostics += Probe 'Netzwerkadapter' {
            foreach ($adapter in Get-NetAdapter) {
                $state = if ([string]$adapter.Status -eq 'Up') { 'ok' } else { 'info' }
                Item "Adapter / $($adapter.Name)" "$($adapter.Status) | $($adapter.LinkSpeed)" $state $adapter.InterfaceDescription
            }
        }
        $diagnostics += Probe 'TCP Prozesszuordnung' {
            $connections = @(Get-NetTCPConnection | Where-Object State -eq Established | Select-Object -First 80)
            if ($connections.Count -eq 0) { Item 'TCP Prozesszuordnung' 'Keine aktiven TCP-Verbindungen' 'info' 'Momentaufnahme, kein Sicherheitsurteil.' }
            foreach ($connection in $connections) {
                $name = "PID $($connection.OwningProcess)"
                try { $name = (Get-Process -Id $connection.OwningProcess).ProcessName }
                catch { $name += ' / Prozess nicht lesbar' }
                Item "TCP / $name" "$($connection.RemoteAddress):$($connection.RemotePort)" 'info' "Lokal $($connection.LocalAddress):$($connection.LocalPort) | PID $($connection.OwningProcess). Maximal 80 Verbindungen. Kein Paketmitschnitt."
            }
        }
        $diagnostics += Probe 'Autostart-Details' {
            $entries = @(Get-CimInstance Win32_StartupCommand | Select-Object -First 80)
            if ($entries.Count -eq 0) { Item 'Autostart-Details' 'Keine Eintraege' 'info' 'Win32_StartupCommand; Teilansicht.' }
            foreach ($entry in $entries) {
                Item "Autostart / $($entry.Name)" $entry.Location 'info' 'Verwaltung ueber Windows Task-Manager. Keine automatische Deaktivierung.'
            }
        }
        $diagnostics += Probe 'Zertifikatsablauf' {
            $certificates = @(Get-ChildItem Cert:\LocalMachine\My,Cert:\CurrentUser\My | Where-Object NotAfter -lt (Get-Date).AddDays(30) | Select-Object -First 40)
            if ($certificates.Count -eq 0) { Item 'Zertifikatsablauf' 'Keine innerhalb von 30 Tagen ablaufenden Zertifikate' 'info' 'Nur persoenliche Zertifikatsspeicher, keine Bewertung von Root-CAs oder Vertrauensketten.' }
            foreach ($certificate in $certificates) {
                Item 'Zertifikatsablauf' $certificate.NotAfter.ToString('yyyy-MM-dd') 'warning' $certificate.Subject
            }
        }
        @{ Cpu = $cpu; Gpu = $gpu; Board = $board; Memory = $memory; Windows = $windows; Checks = @($checks); Disks = @($disks); Diagnostics = @($diagnostics) } | ConvertTo-Json -Depth 6 -Compress
        """;
}
