using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SystemCockpit.Services;

internal sealed record GuardianTraceResult(Guid Id, string Session, string Path, string State, DateTimeOffset Started,
    double ElapsedMilliseconds, long Bytes, uint LostEvents, uint LostBuffers, string EndReason, string Cleanup)
{
    public string Profile { get; init; } = "process";
    public IReadOnlyList<string> Components { get; init; } = ["process"];
    public long QpcStart { get; init; }
    public long QpcEnd { get; init; }
    public long QpcFrequency { get; init; }
    public DateTimeOffset Ended { get; init; }
    public DateTimeOffset CaptureBegan { get; init; } = Started;
    public DateTimeOffset CaptureEnded { get; init; } = Started;
    public string Sha256 { get; init; } = "";
    public string Host { get; init; } = "";
    public bool LossCountersVerified { get; init; } = true;
    public uint? AutomaticEndCode { get; init; }
    public long? SoftStopBytes { get; init; }
    public GuardianTraceBuffers? BufferConfiguration { get; init; }
    public string TimeBasis { get; init; } = "ETW ClientContext=1 (QPC); Windows EvtRender event time; tracerpt XML offset audited separately; boundary anchors are observations, no own QPC reconstruction";
}
internal sealed record GuardianTraceBuffers(uint RequestedKiB, uint RequestedMinimum, uint RequestedMaximum, uint FlushSeconds,
    int HardFileMiB, uint ObservedKiB, uint ObservedMinimum, uint ObservedMaximum, uint NumberOfBuffers, uint BuffersWritten);
internal interface IGuardianTraceNative
{
    uint Start(out ulong session, string name, IntPtr properties);
    uint Enable(ulong session, Guid provider, byte level, ulong keywords);
    uint Control(ulong session, string name, IntPtr properties, uint control);
}
internal sealed class GuardianTrace(IGuardianTraceNative? native = null)
{
    private readonly IGuardianTraceNative _native = native ?? new NativeApi();
    internal static bool ProductionReleased => false;
    internal const long MaximumBytes = 32 * 1024 * 1024;
    internal static readonly Guid ProcessProvider = new("22fb2cd6-0e7b-422b-a0c7-2fad1fd0e716");
    [StructLayout(LayoutKind.Sequential)]
    private struct Wnode
    {
        internal uint BufferSize, ProviderId;
        internal ulong HistoricalContext;
        internal long Timestamp;
        internal Guid Guid;
        internal uint ClientContext, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Properties
    {
        internal Wnode Wnode;
        internal uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
        internal int AgeLimit;
        internal uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        internal IntPtr LoggerThreadId;
        internal uint LogFileNameOffset, LoggerNameOffset;
    }
    private static IntPtr PropertiesBuffer(Guid id, string name, string file, uint maximumMiB = 32, bool systemLogger = false)
    {
        var size = Marshal.SizeOf<Properties>();
        var logger = Encoding.Unicode.GetBytes(name + '\0');
        var path = Encoding.Unicode.GetBytes(file + '\0');
        var length = size + logger.Length + path.Length;
        var pointer = Marshal.AllocHGlobal(length);
        Marshal.Copy(new byte[length], 0, pointer, length);
        var properties = new Properties
        {
            Wnode = new() { BufferSize = (uint)length, Guid = id, Flags = 0x20000, ClientContext = 1 },
            BufferSize = 64,
            MinimumBuffers = 2,
            MaximumBuffers = 16,
            MaximumFileSize = maximumMiB,
            LogFileMode = 1u | (systemLogger ? 0x02000000u : 0),
            FlushTimer = 1,
            LoggerNameOffset = (uint)size,
            LogFileNameOffset = (uint)(size + logger.Length)
        };
        Marshal.StructureToPtr(properties, pointer, false);
        Marshal.Copy(logger, 0, pointer + size, logger.Length);
        Marshal.Copy(path, 0, pointer + size + logger.Length, path.Length);
        return pointer;
    }
    internal static void ValidateRequest(int seconds)
    {
        if (seconds is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds), "Deep Trace erlaubt 1–300 Sekunden.");
    }
    internal static bool Owns(Guid expected, Guid actual, string name) =>
        expected != Guid.Empty && expected == actual && name == "AETHER-Guardian-" + expected.ToString("N");
    internal static string InspectOrStop(Guid id, string directory, bool stop, IGuardianTraceNative? native = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Traceidentität fehlt.");
        var name = "AETHER-Guardian-" + id.ToString("N");
        var file = Path.Combine(directory, id.ToString("N") + ".etl");
        var pointer = PropertiesBuffer(id, name, file);
        try
        {
            var api = native ?? new NativeApi();
            var error = api.Control(0, name, pointer, 0);
            if (error == 4201) return "Nicht aktiv";
            if (error != 0) throw new Win32Exception((int)error);
            var current = Marshal.PtrToStructure<Properties>(pointer);
            if (!Owns(id, current.Wnode.Guid, name))
                throw new InvalidOperationException("Gleichnamige fremde Sitzung bleibt unangetastet.");
            if (!stop) return "Eigene Sitzung aktiv";
            error = api.Control(0, name, pointer, 1);
            if (error != 0 && error != 4201) throw new Win32Exception((int)error);
            error = api.Control(0, name, pointer, 0);
            if (error != 4201) throw new IOException("Eigene verwaiste Sitzung nach Stop nicht nachweislich abwesend; nativer Status " + error + ".");
            return "Eigene unterbrochene Sitzung beendet; ETL möglicherweise unvollständig";
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    internal async Task<GuardianTraceResult> RunAsync(string directory, int seconds, CancellationToken token)
    {
        if (!ProductionReleased) throw new InvalidOperationException("Deep Trace ist implementiert, aber real nicht freigegeben. Ausschließlich den manuellen administrativen ETW-Prüfmodus verwenden.");
        return await RunAcceptanceAsync(directory, seconds, token).ConfigureAwait(false);
    }
    internal async Task<GuardianTraceResult> RunReleasedProfileAsync(string directory, string profileId, int seconds, CancellationToken token,
        IReadOnlyList<string>? components = null)
    {
        var profile = GuardianTraceProfiles.Get(profileId);
        if (!profile.Released) throw new InvalidOperationException("Profil nicht real freigegeben; keine Sitzung gestartet.");
        GuardianTraceProfiles.RequireAdministrator();
        return await RunProfileCoreAsync(directory, profile, seconds, profile.MaximumMiB, components, token).ConfigureAwait(false);
    }
    internal async Task<GuardianTraceResult> RunAcceptanceAsync(string directory, int seconds, CancellationToken token,
        Action<string, uint>? step = null) =>
        await RunProfileCoreAsync(directory, GuardianTraceProfiles.Get("process"), seconds, 4, null, token, step).ConfigureAwait(false);

    internal async Task<GuardianTraceResult> RunProfileAcceptanceAsync(string directory, string profileId, int seconds, CancellationToken token,
        Action<string, uint>? step = null, IReadOnlyList<string>? components = null)
    {
        GuardianTraceProfiles.RequireAdministrator();
        var profile = GuardianTraceProfiles.Get(profileId);
        return await RunProfileCoreAsync(directory, profile, seconds, profile.MaximumMiB, components, token, step).ConfigureAwait(false);
    }
    internal async Task<GuardianTraceResult> RunProfileCoreAsync(string directory, GuardianTraceProfile profile, int seconds,
        int maximumMiB, IReadOnlyList<string>? components, CancellationToken token, Action<string, uint>? step = null,
        long? softStopBytes = null)
    {
        ValidateRequest(seconds);
        if (profile.Id == "custom")
        {
            var plan = GuardianTraceProfiles.CustomPlan(components ?? profile.Components);
            components = plan.Effective;
            softStopBytes = softStopBytes is { } requested ? Math.Min(requested, plan.SoftStopBytes) : plan.SoftStopBytes;
        }
        var providers = GuardianTraceProfiles.Resolve(profile, components);
        if (seconds > profile.MaximumSeconds || maximumMiB is < 1 || maximumMiB > profile.MaximumMiB)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Dauer oder Größe überschreitet das Profilbudget.");
        if (softStopBytes is { } soft && (soft < 524288 || soft > (maximumMiB - 2L) * 1048576))
            throw new ArgumentOutOfRangeException(nameof(softStopBytes), "Kontrolliertes Größenende benötigt mindestens 512 KiB und zwei MiB Abstand zum harten Dateilimit.");
        if (native is null && providers.Any(p => p.SystemLogger) && Environment.OSVersion.Version.Build < 20348)
            throw new PlatformNotSupportedException("System Profile Provider benötigt Windows Build 20348 oder neuer.");
        token.ThrowIfCancellationRequested();
        EvidenceFile.RequireLocalDirectory(directory);
        Directory.CreateDirectory(directory);
        if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("ETL-Ausgabe darf keine Dateiumleitung sein.");
        var existing = new DirectoryInfo(directory).EnumerateFiles().ToArray();
        if (existing.Length >= 8 || existing.Sum(f => f.Length) + MaximumBytes > 256L * 1024 * 1024)
            throw new IOException("Separates ETL-Budget erschöpft: höchstens acht Dateien / 256 MiB; erst bewusst eigene Artefakte löschen.");
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
        if (!drive.IsReady || drive.AvailableFreeSpace < MaximumBytes + 1048576) throw new IOException("Freier ETL-Speicher nicht belegt.");
        var id = Guid.NewGuid(); var name = "AETHER-Guardian-" + id.ToString("N");
        var file = Path.Combine(directory, id.ToString("N") + ".etl");
        var started = DateTimeOffset.UtcNow; var qpc = Stopwatch.GetTimestamp(); var watch = Stopwatch.StartNew();
        ulong session = 0; var active = false; var cleanup = "Nicht gestartet"; var end = "Zeitlimit"; var state = "Completed";
        var captureBegan = started; var captureEnded = started;
        uint lost = 0, buffers = 0; uint? automaticEnd = null;
        Properties observedBuffers = default;
        var countersVerified = true;
        await EvidenceFile.WriteAsync(file + ".session", EvidenceFile.Encode("guardian-trace-session",
            new Dictionary<string, string>
            {
                ["id"] = id.ToString("D"),
                ["name"] = name,
                ["path"] = file,
                ["started"] = started.ToString("O"),
                ["profile"] = profile.Id,
                ["components"] = string.Join(",", providers.Select(p => p.Id)),
                ["maximumMiB"] = maximumMiB.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["state"] = "Starting"
            }), token).ConfigureAwait(false);
        var maximumBytes = maximumMiB * 1024L * 1024;
        var pointer = PropertiesBuffer(id, name, file, (uint)maximumMiB, providers.Any(p => p.SystemLogger));
        step?.Invoke("PropertiesBufferAllocated", 0);
        try
        {
            var error = _native.Start(out session, name, pointer);
            step?.Invoke("StartTrace", error);
            if (error != 0) throw new Win32Exception((int)error);
            active = true;
            captureBegan = DateTimeOffset.UtcNow;
            foreach (var provider in providers)
            {
                error = _native.Enable(session, provider.Guid, provider.Level, provider.Keywords);
                step?.Invoke("EnableTraceEx2", error);
                step?.Invoke("Provider:" + provider.Id, error);
                if (error != 0) throw new Win32Exception((int)error);
            }
            try
            {
                while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                    var status = _native.Control(session, name, pointer, 0);
                    step?.Invoke("QueryStatus", status);
                    if (status == 4201)
                    {
                        automaticEnd = status;
                        countersVerified = false;
                        active = false;
                        captureEnded = DateTimeOffset.UtcNow;
                        state = "Incomplete";
                        end = File.Exists(file) && new FileInfo(file).Length >= maximumBytes
                            ? "Größenlimit; eigene Sitzung automatisch beendet (4201)"
                            : "Eigene Sitzung unerwartet nicht mehr aktiv (4201)";
                        cleanup = "Eigene Sitzung nicht mehr aktiv; abschließende Abwesenheitsprüfung folgt";
                        break;
                    }
                    if (status != 0) throw new Win32Exception((int)status);
                    var observed = Marshal.PtrToStructure<Properties>(pointer);
                    observedBuffers = observed;
                    if (!Owns(id, observed.Wnode.Guid, name))
                        throw new InvalidOperationException("Eigene ETW-Identität während Aufnahme geändert.");
                    if (softStopBytes is { } threshold && watch.ElapsedMilliseconds >= 500 &&
                        File.Exists(file) && new FileInfo(file).Length >= threshold)
                    { end = "Größenlimit: kontrollierter Soft-Stopp mit Reserve zum harten Dateilimit"; state = "Incomplete"; break; }
                    if (File.Exists(file) && new FileInfo(file).Length >= maximumBytes)
                    { end = "Größenlimit"; state = "Incomplete"; break; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { end = "Benutzerabbruch oder AETHER-Shutdown"; state = "Cancelled"; }
        }
        finally
        {
            try
            {
                if (active)
                {
                    var query = _native.Control(session, name, pointer, 0);
                    step?.Invoke("QueryBeforeStop", query);
                    if (query == 0)
                    {
                        var current = Marshal.PtrToStructure<Properties>(pointer);
                        if (!Owns(id, current.Wnode.Guid, name))
                            throw new InvalidOperationException("ETW-Sitzungsidentität stimmt nicht; fremde Sitzung bleibt unangetastet.");
                        var stop = _native.Control(session, name, pointer, 1);
                        step?.Invoke("ControlTraceStop", stop);
                        if (stop != 0 && stop != 4201) throw new Win32Exception((int)stop);
                        captureEnded = DateTimeOffset.UtcNow;
                        current = Marshal.PtrToStructure<Properties>(pointer);
                        observedBuffers = current;
                        lost = current.EventsLost; buffers = current.LogBuffersLost + current.RealTimeBuffersLost;
                        cleanup = stop == 0 ? "Eigene Sitzung kontrolliert beendet" : "Eigene Sitzung bereits beendet";
                        if (stop == 4201) { state = "Incomplete"; end = "Sitzung vor Stop bereits beendet"; countersVerified = false; automaticEnd = stop; }
                    }
                    else if (query == 4201) { cleanup = "Sitzung nicht mehr aktiv"; state = "Incomplete"; end = "Sitzung vor Stop nicht mehr aktiv"; captureEnded = DateTimeOffset.UtcNow; countersVerified = false; automaticEnd = query; }
                    else throw new Win32Exception((int)query);
                }
            }
            finally { Marshal.FreeHGlobal(pointer); step?.Invoke("PropertiesBufferFreed", 0); }
        }
        if (lost != 0 || buffers != 0) state = "Incomplete";
        if (!File.Exists(file)) throw new IOException("ETW hat keine ETL-Datei erzeugt.");
        step?.Invoke("EtlFileExists", 0);
        var finalStatus = InspectOrStop(id, directory, false, _native);
        if (finalStatus != "Nicht aktiv") throw new IOException("Eigene ETW-Sitzung nach Stop weiterhin aktiv.");
        step?.Invoke("SessionAbsentAfterStop", 4201);
        if (native is null)
        {
            var header = GuardianTraceLog.ReadLossCounters(file, step);
            lost = Math.Max(lost, header.Events); buffers = Math.Max(buffers, header.Buffers);
            countersVerified = true;
        }
        if (lost != 0 || buffers != 0) state = "Incomplete";
        var bytes = new FileInfo(file).Length;
        if (bytes <= 0 || bytes > maximumBytes) { state = "Incomplete"; end = "ETL-Größe außerhalb Profilbudget"; }
        else if (bytes == maximumBytes && state is not "Cancelled") { state = "Incomplete"; end = "Größenlimit" + (automaticEnd is null ? "" : "; eigene Sitzung automatisch beendet (4201)"); }
        var result = new GuardianTraceResult(id, name, file, state, started, watch.Elapsed.TotalMilliseconds,
            bytes, lost, buffers, end, cleanup)
        {
            Profile = profile.Id,
            Components = providers.Select(p => p.Id).ToArray(),
            QpcStart = qpc,
            QpcEnd = Stopwatch.GetTimestamp(),
            QpcFrequency = Stopwatch.Frequency,
            Ended = DateTimeOffset.UtcNow,
            CaptureBegan = captureBegan,
            CaptureEnded = captureEnded,
            Host = IntelligenceSources.GuardianContext().Host,
            LossCountersVerified = countersVerified,
            AutomaticEndCode = automaticEnd,
            SoftStopBytes = softStopBytes,
            BufferConfiguration = new(64, 2, 16, 1, maximumMiB, observedBuffers.BufferSize, observedBuffers.MinimumBuffers,
                observedBuffers.MaximumBuffers, observedBuffers.NumberOfBuffers, observedBuffers.BuffersWritten),
            Sha256 = await HashAsync(file, CancellationToken.None).ConfigureAwait(false)
        };
        await EvidenceFile.WriteAsync(file + ".session", EvidenceFile.Encode("guardian-trace-result", result), CancellationToken.None).ConfigureAwait(false);
        return result;
    }
    private sealed class NativeApi : IGuardianTraceNative
    {
        public uint Start(out ulong session, string name, IntPtr properties) => StartTrace(out session, name, properties);
        public uint Enable(ulong session, Guid provider, byte level, ulong keywords) => EnableTraceEx2(session, ref provider, 1, level, keywords, 0, 1000, IntPtr.Zero);
        public uint Control(ulong session, string name, IntPtr properties, uint control) => ControlTrace(session, name, properties, control);
    }
    internal static async Task<string> HashAsync(string file, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
    internal static async Task DeleteOwnedAsync(GuardianTraceResult trace, CancellationToken token, IGuardianTraceNative? native = null)
    {
        GuardianTraceDecoder.Validate(trace);
        var directory = Path.GetDirectoryName(trace.Path)!;
        EvidenceFile.RequireLocalDirectory(directory);
        if (InspectOrStop(trace.Id, directory, false, native) != "Nicht aktiv")
            throw new InvalidOperationException("Eigene Sitzung noch aktiv; Rohdaten nicht gelöscht.");
        var marker = trace.Path + ".session";
        if (new FileInfo(trace.Path).Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            new FileInfo(marker).Attributes.HasFlag(FileAttributes.ReparsePoint) || new FileInfo(marker).Length > 65536)
            throw new IOException("Umgeleitete oder ungültige Rohdatei bleibt unangetastet.");
        var stored = EvidenceFile.Decode<GuardianTraceResult>("guardian-trace-result", await File.ReadAllTextAsync(marker, token).ConfigureAwait(false));
        if (stored.Id != trace.Id || stored.Path != trace.Path || stored.Sha256 != trace.Sha256 ||
            await HashAsync(trace.Path, token).ConfigureAwait(false) != trace.Sha256)
            throw new InvalidDataException("Rohdatei und ausgewählte eigene Traceidentität stimmen nicht überein.");
        token.ThrowIfCancellationRequested();
        File.Delete(trace.Path); File.Delete(marker);
    }
    [DllImport("advapi32.dll", EntryPoint = "StartTraceW", CharSet = CharSet.Unicode)]
    private static extern uint StartTrace(out ulong handle, string name, IntPtr properties);
    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode)]
    private static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint control);
    [DllImport("advapi32.dll")]
    private static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level,
        ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);
}
