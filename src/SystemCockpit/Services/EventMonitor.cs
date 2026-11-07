using System.IO;
using System.Text;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed record MonitorEvent(DateTimeOffset Timestamp, string Log, long RecordId, int Id,
    int Level, string Provider, string Message)
{
    public string Severity => Level switch { 1 => "Kritisch", 2 => "Fehler", 3 => "Warnung", 4 => "Information", _ => "Sonstige" };
    public string SearchText => $"{Log} {Id} {Severity} {Provider} {Message}";
    public DateTime LocalTime => Timestamp.LocalDateTime;
}
public sealed record DiskActivity(string Name, double ReadMbps, double WriteMbps, double? BusyPercent, double QueueLength);
public sealed record EventSnapshot(DateTimeOffset Timestamp, List<MonitorEvent> Events, List<string> Issues,
    List<DiskActivity>? DiskActivity = null);

public static class EventMonitor
{
    public static async Task<EventSnapshot> ReadAsync(CancellationToken token)
    {
        var result = await CommandRunner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(Script))],
            TimeSpan.FromSeconds(20), token, utf8: true);
        var error = WindowsProbe.ReadErrorStream(result.Error);
        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"Ereignismonitor Exit {result.ExitCode}: {error}");
        return Parse(result.Output);
    }

    public static EventSnapshot Parse(string json)
    {
        var snapshot = JsonSerializer.Deserialize<EventSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Ereignismonitor lieferte keine Daten.");
        if (snapshot.Events is null || snapshot.Issues is null || snapshot.Events.Count > 400 ||
            snapshot.Events.Any(e => e.Log is not ("System" or "Application") || e.RecordId < 0 ||
                e.Level is < 0 or > 5 || e.Provider is null || e.Message is null) ||
            snapshot.DiskActivity?.Any(d => d.Name is null || !double.IsFinite(d.ReadMbps) || d.ReadMbps < 0 ||
                !double.IsFinite(d.WriteMbps) || d.WriteMbps < 0 || !double.IsFinite(d.QueueLength) || d.QueueLength < 0 ||
                d.BusyPercent is { } busy && (!double.IsFinite(busy) || busy is < 0 or > 100)) == true)
            throw new InvalidOperationException("Ungueltiges Ereignismonitor-Datenformat.");
        return snapshot;
    }

    public static async Task<EventSnapshot> ReadChannelAsync(string channel, int days, int limit, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(channel) || channel.Length > 256 || days is < 1 or > 30 || limit is < 1 or > 2000)
            throw new ArgumentException("Ungueltiger Ereigniskanal/Zeitraum/Limit.");
        var json = await CommandRunner.PowerShellAsync("""
            $records=@(); $issues=@()
            try {
            """ + $"$records=@(Get-WinEvent -FilterHashtable @{{LogName={AdminManagement.Quote(channel)};StartTime=(Get-Date).AddDays(-{days})}} -MaxEvents {limit} -ErrorAction Stop)" +
            """
            } catch { if($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { throw } }
            $rows=@(foreach($e in $records) {
                try {$message=[string]$e.Message} catch {$message='Nachricht nicht lesbar: '+$_.Exception.Message; $issues+=$message}
                if($message.Length -gt 400) {$message=$message.Substring(0,400)+"`n[Tabellenvorschau gekuerzt; Details separat laden]"}
                @{Timestamp=$e.TimeCreated.ToUniversalTime().ToString('o');Log=$e.LogName;RecordId=[long]$e.RecordId;Id=[int]$e.Id;Level=[int]$e.Level;Provider=[string]$e.ProviderName;Message=$message}
            })
            @{Events=@($rows);Issues=@($issues)} | ConvertTo-Json -Depth 4 -Compress
            """, token);
        using var document = JsonDocument.Parse(json);
        var events = document.RootElement.GetProperty("Events").Deserialize<List<MonitorEvent>>() ?? throw new InvalidOperationException("Ereignisse fehlen.");
        var issues = document.RootElement.GetProperty("Issues").Deserialize<List<string>>() ?? throw new InvalidOperationException("Ereignis-Abfragestatus fehlt.");
        if (events.Count > limit || events.Any(e => e.Log != channel || e.Message is null || e.Provider is null))
            throw new InvalidOperationException("Unerwartete Ereignisantwort.");
        return new(DateTimeOffset.Now, events, issues);
    }

    private const string Script = """
        $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $events=@(); $issues=@()
        foreach($log in @('System','Application')) {
            try {
                $records=@(Get-WinEvent -FilterHashtable @{LogName=$log; StartTime=(Get-Date).AddHours(-24)} -MaxEvents 200 -ErrorAction Stop)
                foreach($e in $records) {
                    try { $message=[string]$e.Message } catch { $message='Nachricht nicht lesbar: '+$_.Exception.Message; $issues+=$message }
                    if($message.Length -gt 2000) { $message=$message.Substring(0,2000)+"`n[Nachricht auf 2000 Zeichen gekuerzt]" }
                    $events+=@{Timestamp=$e.TimeCreated.ToUniversalTime().ToString('o'); Log=$log; RecordId=[long]$e.RecordId;
                        Id=[int]$e.Id; Level=[int]$e.Level; Provider=[string]$e.ProviderName; Message=$message}
                }
            } catch {
                if($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { $issues+=($log+': '+$_.Exception.Message) }
            }
        }
        $disks=@()
        try {
            foreach($d in @(Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk | Where-Object Name -ne '_Total')) {
                $busy=$null
                if([double]$d.PercentIdleTime -le 100) { $busy=100-[double]$d.PercentIdleTime }
                $disks+=@{Name=[string]$d.Name; ReadMbps=([double]$d.DiskReadBytesPersec/1MB);
                    WriteMbps=([double]$d.DiskWriteBytesPersec/1MB); BusyPercent=$busy;
                    QueueLength=[double]$d.CurrentDiskQueueLength}
            }
            if($disks.Count -eq 0) { $issues+='Disk-I/O: Keine Windows-Performancezaehler gemeldet.' }
        } catch { $issues+=('Disk-I/O: '+$_.Exception.Message) }
        @{Timestamp=(Get-Date).ToUniversalTime().ToString('o'); Events=@($events); Issues=@($issues); DiskActivity=@($disks)} | ConvertTo-Json -Depth 5 -Compress
        """;
}
