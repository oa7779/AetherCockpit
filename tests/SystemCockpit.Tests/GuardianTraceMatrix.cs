using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using SystemCockpit.Services;

internal static class GuardianTraceMatrix
{
    internal sealed record Resources(int Handles, int Threads, long PrivateBytes, long WorkingSet);
    private sealed record Phase(string Name, double Milliseconds, DateTimeOffset At, Resources Resources,
        int NativePropertiesBufferBalance, bool TokenScopeOpen, int OwnConvertersPending);
    internal static bool WithinWarmBudget(Resources before, Resources after) =>
        after.Handles - before.Handles <= 64 && after.Threads - before.Threads <= 8;
    internal static bool WarmPlateau(Resources previous, Resources after) =>
        after.Handles <= previous.Handles && after.Threads <= previous.Threads;
    internal static readonly string[] RepeatModes = ["cold", "warm-1", "warm-2"];
    internal static async Task SettleAsync()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await Task.Delay(1000).ConfigureAwait(false);
    }
    private static Resources Measure()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(process.HandleCount, process.Threads.Count, process.PrivateMemorySize64, process.WorkingSet64);
    }
    internal static int Workload()
    {
        var file = Path.Combine(Path.GetTempPath(), "AetherTraceWorkload-" + Guid.NewGuid().ToString("N") + ".bin");
        EvidenceFile.RequireLocalDirectory(Path.GetDirectoryName(file)!);
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
        stream.Write(new byte[65536]); stream.Flush(flushToDisk: true); stream.Position = 0; stream.ReadExactly(new byte[65536]);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect((System.Net.IPEndPoint)listener.LocalEndpoint);
            using var server = listener.AcceptTcpClient();
            client.GetStream().WriteByte(1);
            if (server.GetStream().ReadByte() != 1) throw new IOException("Eigener Loopback-Workload wurde nicht bestätigt.");
        }
        finally { listener.Stop(); }
        var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < 100) Thread.SpinWait(1000);
        var thread = new Thread(() => Thread.Sleep(20)); thread.Start(); thread.Join();
        return 0;
    }
    internal static int Run(string output) => RunAsync(output).GetAwaiter().GetResult();
    internal static int RunCpu(string output) => RunAsync(output, cpuOnly: true).GetAwaiter().GetResult();
    internal static int RunCpuDiagnostic(string output) => RunAsync(output, cpuOnly: true, diagnosticOnly: true).GetAwaiter().GetResult();
    internal static int RunCustom(string output) => RunAsync(output, customOnly: true).GetAwaiter().GetResult();
    internal static int RunLossProbe(string output) => RunAsync(output, lossProbe: true).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string output, bool cpuOnly = false, bool lossProbe = false, bool customOnly = false,
        bool diagnosticOnly = false)
    {
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Neue absolute Ergebnisdatei erforderlich.");
        EvidenceFile.RequireLocalDirectory(Path.GetDirectoryName(output)!);
        using var identity = WindowsIdentity.GetCurrent();
        var administrator = GuardianTraceProfiles.IsAdministrator();
        var token = new
        {
            Administrator = administrator,
            identity.AuthenticationType,
            ImpersonationLevel = identity.ImpersonationLevel.ToString(),
            ElevatedAutomatically = false,
            OperatingSystem = Environment.OSVersion.VersionString,
            SeSystemProfilePrivilege = GuardianTraceProfiles.ReadProfilePrivilege()
        };
        Console.WriteLine(JsonSerializer.Serialize(token));
        Console.WriteLine(cpuOnly
            ? "Separater manueller CPU-Test: explizite zeitweilige Aktivierung des bereits vorhandenen SeSystemProfilePrivilege nur im aktuellen Prozess; danach Rücksetzung. Keine Rechtezuweisung, Selbsterhöhung oder Produktfreigabe."
            : "Manueller Guardian-Profiltest: nur eigene serielle Sessions / kurze eigene Workloads. Keine Privilegaktivierung. ETL und XML nach sicherem Stop löschen. Keine Produktfreigabe.");
        if (diagnosticOnly)
            Console.WriteLine("DIAGNOSTIC ONLY: niemals Abnahme/PASS. Begrenzte rohe XML-Stichproben auch unbekannter Ereignisse können sensible Metadaten enthalten; nur lokal prüfen und vor Weitergabe redigieren.");
        var runs = new List<RunResult>(); var allPassed = administrator;
        var privilegeJournal = new List<GuardianCpuPrivilege.Entry>(); string? privilegeError = null;
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, args) => { args.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            if (administrator)
            {
                var scenarios = new List<(string Profile, string Mode, int MiB, string[]? Components)>
            {
                ("process", "cold", 8, null), ("process", "warm-1", 8, null), ("process", "warm-2", 8, null),
                ("process", "cancel", 8, null), ("process", "shutdown-token", 8, null), ("process", "size", 8, null)
            };
                scenarios.AddRange(GuardianTraceProfiles.All.Where(p => p.Id != "process")
                    .Select(p => (p.Id, "profile", 8, p.Id == "custom" ? new[] { "process", "file", "network" } : null)));
                if (cpuOnly) scenarios = RepeatModes.Select(m => ("cpu", m, 8, (string[]?)null)).ToList();
                if (customOnly) scenarios = RepeatModes.Select(m => ("custom", m, 8, (string[]?)["process", "file", "network"])).ToList();
                if (lossProbe) scenarios = [("process", "hard-loss-probe", 1, null)];
                foreach (var scenario in scenarios)
                {
                    if (scenario.Profile == "custom")
                        Console.WriteLine(JsonSerializer.Serialize(GuardianTraceProfiles.CustomPlan(scenario.Components ?? ["process"])));
                    var run = await CaptureAsync(scenario.Profile, scenario.Mode, scenario.MiB, scenario.Components, shutdown.Token,
                        cpuOnly, diagnosticOnly, runs.LastOrDefault(r => r.Mode == "warm-1")?.After).ConfigureAwait(false);
                    runs.Add(run);
                    privilegeJournal.AddRange(run.PrivilegeJournal);
                    allPassed &= run.Passed;
                    Console.WriteLine($"{scenario.Profile}/{scenario.Mode}: {(run.Passed ? "PASS" : "NICHT BESTANDEN")}");
                    foreach (var gate in run.Gates.Where(g => g.Required && g.Passed != true))
                        Console.WriteLine($"- {gate.Name}: {gate.Actual}; erwartet {gate.Expected} [{gate.Area} / {gate.Source}]");
                    if (run.Analysis?.Decoder is { } decoder)
                        Console.WriteLine($"- Decoder: XML validiert={decoder.XmlValidated}; Fakten={decoder.FactsProduced}; FactLimit={decoder.FactLimitReached}; EventLimit={decoder.EventLimitReached}; übersprungene Provider={decoder.UnknownProvidersSkipped}; unvollständig={decoder.DecodeIncomplete}; tracerpt-XML-FactLimit={run.XmlDecoder?.FactLimitReached}");
                    if (run.Error is not null) Console.WriteLine($"- {run.FailedStep ?? "Managed"}: {run.Error}; NativeCode={run.NativeCode?.ToString() ?? "nicht nativ"}");
                    foreach (var group in new[]
                    {
                    ("Lifecycle", new[] { "SessionStarted", "ProvidersEnabled", "StopSucceeded", "SessionAbsentAfterStop", "CaptureStateExpected" }),
                    ("Decoder", new[] { "DecoderInvocationPassed", "XmlValidated" }), ("MalformedFields", new[] { "NoMalformedFields" }),
                    ("TimeCorrelation", new[] { "CoreEventTimesWithinCapture", "SourceContractVerified", "CpuSampleTimesWithinCapture" }),
                    ("WorkloadIdentity", new[] { "WorkloadIdentityObserved" }), ("RequiredFacts", new[] { "ExpectedWorkloadObserved" }),
                    ("LostEvents", new[] { "LostEventsAcceptable" }), ("LostBuffers", new[] { "LostBuffersAcceptable" }),
                    ("Cleanup", new[] { "CleanupVerified", "RetainedDirectoryIsNull", "ManagedNativeBufferBalanceIsZero" })
                })
                    {
                        var values = run.Gates.Where(g => group.Item2.Contains(g.Name)).ToArray();
                        Console.WriteLine($"- {group.Item1}: " + (values.All(g => !g.Required) ? "nicht erforderlich; Befund separat" :
                            values.All(g => !g.Required || g.Passed == true) ? "PASS" : "NICHT BESTANDEN"));
                    }
                    Console.WriteLine($"- Fact/EventLimits: {run.Analysis?.Decoder?.FactLimitReached}/{run.Analysis?.Decoder?.EventLimitReached}; Privilege: Present={GuardianTraceProfiles.ReadProfilePrivilege().Present}, Enabled={GuardianTraceProfiles.ReadProfilePrivilege().Enabled}; Native={run.NativeCode?.ToString() ?? "kein Fehler"}");
                    Console.WriteLine($"- Resources ({scenario.Mode}): Handles {run.Before.Handles}->{run.After.Handles}; Threads {run.Before.Threads}->{run.After.Threads}; PrivateBytes {run.Before.PrivateBytes}->{run.After.PrivateBytes}; WorkingSet {run.Before.WorkingSet}->{run.After.WorkingSet}; nach festem Nachlauf");
                    foreach (var gate in run.Gates.Where(g => g.Name is "ResourceGatePassed" or "WarmPlateau" or "OwnConvertersExited" or "PrivilegeRestoredAndTokenClosed"))
                        Console.WriteLine($"- {gate.Name}: {(gate.Required ? gate.Passed == true ? "PASS" : "NICHT BESTANDEN" : "nicht erforderlich (kalt)")}; {gate.Actual}");
                    // Retain ambiguous artifacts for inspection and never accumulate possibly active test sessions.
                    if (!run.CleanupVerified || cpuOnly && !PrivilegePassed(run.PrivilegeJournal) || shutdown.IsCancellationRequested) break;
                }
            }
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or InvalidOperationException)
        { privilegeError = ex.Message; allPassed = false; }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
        var privilegePassed = !cpuOnly || privilegeError is null && runs.Count == RepeatModes.Length &&
            runs.All(r => PrivilegePassed(r.PrivilegeJournal));
        if (cpuOnly && !privilegePassed) privilegeError ??= "Nicht alle drei Aktivierungs-/Rücksetzungszyklen nachgewiesen; siehe Einzeljournale.";
        allPassed &= privilegePassed && !shutdown.IsCancellationRequested;
        if (cpuOnly || customOnly) allPassed &= runs.Count == RepeatModes.Length;
        if (cpuOnly) Console.WriteLine($"- PrivilegeActivation/Restore/HandleCleanup: {(privilegePassed ? "PASS" : "NICHT BESTANDEN")}; {privilegeError}");
        if (lossProbe || diagnosticOnly) allPassed = false;
        await using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, true);
        await JsonSerializer.SerializeAsync(target, new
        {
            Schema = "aether.etw.matrix/1",
            TestMode = diagnosticOnly ? "CpuProviderDiagnostic" : cpuOnly ? "CpuPrivilegeAcceptance" : customOnly ? "BoundedCustomAcceptance" :
                lossProbe ? "DiagnosticLossProbe" : "LegacyFullMatrix",
            Token = token,
            AllGatesPassed = allPassed,
            ProductProfilesReleased = false,
            Runs = runs,
            CpuPrivilegeExplicitlyRequested = cpuOnly,
            CpuPrivilegeGatesPassed = cpuOnly ? privilegePassed : (bool?)null,
            ShutdownRequested = shutdown.IsCancellationRequested,
            CustomAcceptanceOnly = customOnly,
            CustomPlan = customOnly ? GuardianTraceProfiles.CustomPlan(["process", "file", "network"]) : null,
            DiagnosticLossProbeOnly = lossProbe,
            DiagnosticOnly = diagnosticOnly,
            HostProcessId = Environment.ProcessId,
            RepeatContract = cpuOnly || customOnly ? RepeatModes : [],
            SizeTestContract = cpuOnly ? "CPU: zwei Sekunden / acht MiB; kein Größenendszenario" :
                customOnly ? "Custom: höchstens eine Sekunde / acht MiB; Soft-Stopp ab512KiB/nach500ms; Decoder64MiB je Datei" :
                lossProbe ? "B: harte Dateilimit-Erschöpfung; niemals Profilfreigabe oder Exit0" :
                "A: Soft-Stopp ab512KiB/nach500ms mit8MiB hartem Dateilimit; nur Stop0 und Verluste0 bestehen",
            PrivilegeJournal = privilegeJournal,
            PrivilegeError = privilegeError,
            ResourceGate = "Kaltlauf separat (ResourceGate nicht erforderlich). Nach jedem Lauf einschließlich Privilegrücksetzung/Handlefreigabe: GC/Finalizer/GC + fester 1s-Nachlauf, kein Minimum-Picking. Warmdelta <=64 Handles / <=8 Threads; warm2-Endstand darf warm1-Endstand nicht überschreiten. Keine Leakfreiheitsbehauptung. Kein UI im Runner, daher kein Dispatcher-Pump auf fremdem Thread; Threadpool bleibt aktiv.",
            Error = administrator ? null : "AccessDenied: Prozess nicht erhöht; kein ETW-Start versucht.",
            Limits = "Kaltlauf separat, Wiederholungen im selben Host; kurze Ressourcenmessung ist keine Leakfreiheits-/Langzeitfreigabe. Shutdown-Token ist kein physischer App-/Windows-Absturz. Keine fremden Sessions.",
            Output = output
        }, new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(false);
        return allPassed ? 0 : 2;
    }
    internal sealed record Gate(string Name, bool? Passed, string Actual, string Expected, string Source, string Area, bool Required = true);
    internal sealed record Step(string Name, uint? NativeCode, bool Verified, double Milliseconds);
    internal static bool PrivilegePassed(IReadOnlyList<GuardianCpuPrivilege.Entry> journal) =>
        journal.All(e => e.Succeeded) &&
        new[] { "ReadBefore", "AdjustTokenPrivileges(enable)", "ReadEnabled", "AdjustTokenPrivileges(restore)", "ReadRestored", "TokenHandleClosed" }
            .All(name => journal.Count(e => e.Step == name && e.Succeeded) == 1);
    private sealed record RunResult(string Profile, string Mode, Resources Before, Resources After, IReadOnlyList<Step> Steps,
        GuardianTraceResult? Result, GuardianTraceAnalysis? Analysis, ProcessIdentity? Workload,
        bool WorkloadExited, bool WorkloadIdentityObserved, bool Passed, string? Error, int? NativeCode,
        bool CleanupVerified, string? RetainedDirectory, int ManagedNativeBufferBalance,
        IReadOnlyList<Gate> Gates, string? FailedStep, string? ExceptionType, bool DecoderStarted, bool DecoderInvocationPassed,
        string? DecoderPhase, GuardianTraceDecodeStatus? XmlDecoder, IReadOnlyList<string> XmlTimeSamples)
    {
        public IReadOnlyList<GuardianTraceConversion> Conversions { get; init; } = [];
        public IReadOnlyList<Phase> ResourcePhases { get; init; } = [];
        public IReadOnlyList<GuardianCpuPrivilege.Entry> PrivilegeJournal { get; init; } = [];
        public IReadOnlyList<GuardianTraceRejectionEvidence> RejectionEvidence { get; init; } = [];
        public int RejectionEvidenceDropped { get; init; }
        public string? ManagedException => NativeCode is null && Error is not null ? ExceptionType + ": " + Error : null;
        public bool WorkloadFactsAvailable => Gates.Any(g => g.Name == "ExpectedWorkloadObserved" && g.Passed == true);
    }
    private static async Task<RunResult> CaptureAsync(string profileId, string mode, int maximumMiB, string[]? components,
        CancellationToken shutdown = default, bool enableCpuPrivilege = false, bool diagnosticOnly = false,
        Resources? previousWarmAfter = null)
    {
        GuardianTraceProfiles.RequireAdministrator();
        var directory = Path.Combine(Path.GetTempPath(), "AetherGuardianMatrix-" + Guid.NewGuid().ToString("N"));
        EvidenceFile.RequireLocalDirectory(directory);
        Directory.CreateDirectory(directory);
        var profile = GuardianTraceProfiles.Get(profileId);
        var providers = GuardianTraceProfiles.Resolve(profile, components); var enabled = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var before = Measure(); var clock = Stopwatch.StartNew(); var steps = new List<Step>();
        var privilegeJournal = new List<GuardianCpuPrivilege.Entry>(); GuardianCpuPrivilege? privilegeScope = null;
        var balance = 0;
        var conversions = new List<GuardianTraceConversion>();
        var phases = new List<Phase> { new("BeforePrivilegeAndCapture", 0, DateTimeOffset.UtcNow, before, 0, false, 0) };
        void Sample(string name) => phases.Add(new(name, clock.Elapsed.TotalMilliseconds, DateTimeOffset.UtcNow, Measure(), balance,
            privilegeScope is not null && !privilegeJournal.Any(e => e.Step == "TokenHandleClosed" && e.Succeeded),
            conversions.Count(c => c.State == "ProcessStarted") - conversions.Count(c => c.State == "ProcessExited")));
        GuardianTraceResult? trace = null; GuardianTraceAnalysis? analysis = null;
        Process? child = null; ProcessIdentity? workload = null; var exited = false; var observed = false;
        var started = false; var cleaned = false; string? error = null; int? native = null;
        var decoderStarted = false; var decoderPassed = false; string? exceptionType = null; string? failedStep = null;
        string? decoderPhase = null; GuardianTraceDecodeStatus? xmlDecoder = null; IReadOnlyList<string> xmlTimes = [];
        IReadOnlyList<GuardianTraceRejectionEvidence> rejectionEvidence = [];
        var rejectionEvidenceDropped = 0;
        try
        {
            if (enableCpuPrivilege) privilegeScope = GuardianCpuPrivilege.Enable(true, privilegeJournal);
            Sample("AfterPrivilegeBeforeCapture");
            trace = await new GuardianTrace().RunProfileCoreAsync(directory, profile, profileId == "custom" ? 1 : 2, maximumMiB, components, stop.Token, (name, code) =>
            {
                if (name == "PropertiesBufferAllocated") balance++;
                if (name == "PropertiesBufferFreed") balance--;
                if (name == "StartTrace" && code == 0) started = true;
                var managed = name is "PropertiesBufferAllocated" or "PropertiesBufferFreed" or "EtlFileExists";
                steps.Add(new(name, managed ? null : code, code == 0 || code == 4201 &&
                    name is "SessionAbsentAfterStop" or "QueryStatus" or "QueryBeforeStop" or "ControlTraceStop", clock.Elapsed.TotalMilliseconds));
                if (name.StartsWith("Provider:", StringComparison.Ordinal) && code == 0 && ++enabled == providers.Count)
                {
                    Sample("EtwProvidersEnabled");
                    child = Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(AppContext.BaseDirectory, "SystemCockpit.Tests.exe"),
                        Arguments = "--guardian-trace-workload",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }) ?? throw new InvalidOperationException("Eigener Trace-Testprozess nicht gestartet.");
                    workload = new(child.Id, child.StartTime.ToUniversalTime().Ticks);
                    Sample("OwnWorkloadStarted");
                    if (mode is "cancel" or "shutdown-token") stop.CancelAfter(650);
                }
            }, softStopBytes: mode == "size" ? 524288 : null).ConfigureAwait(false);
            Sample("AfterEtwStop");
            if (child is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(timeout.Token).ConfigureAwait(false); exited = child.ExitCode == 0;
            }
            Sample("AfterWorkloadExitBeforeDecoder");
            decoderStarted = true;
            analysis = await GuardianTraceDecoder.DecodeAsync(trace, CancellationToken.None, workload, (phase, partial) =>
            {
                decoderPhase = phase;
                if (phase != "tracerpt XML parsed" || partial is null) return;
                xmlDecoder = partial.Decoder;
                xmlTimes = partial.Facts.Where(f => f.Kind is "ProcessStart" or "ProcessEnd" &&
                    workload is { } own && f.Identity == own.ToString()).Take(8)
                    .Select(f => $"XML={f.OriginalSystemTime}; Parsed={f.Time:O}; Identity={f.Identity}; CreateTime={f.Data.GetValueOrDefault("CreateTime")?.Trim()}").ToArray();
            }, conversionProgress: value =>
            {
                lock (conversions)
                {
                    conversions.Add(value);
                    if (value.State is "ProcessStarted" or "ProcessExited") Sample(value.Format + "/" + value.State);
                }
            }, diagnoseEventTypes: diagnosticOnly).ConfigureAwait(false);
            decoderPassed = true;
            Sample("AfterDecoderReadersClosed");
            observed = workload is { } known && analysis.Facts.Any(f => f.Kind == "ProcessStart" && f.Identity == known.ToString());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or
            JsonException or TimeoutException or OperationCanceledException or System.Xml.XmlException or PlatformNotSupportedException or
            System.Diagnostics.Eventing.Reader.EventLogException)
        {
            error = ex.Message; native = (ex as Win32Exception)?.NativeErrorCode; exceptionType = ex.GetType().FullName;
            if (ex is GuardianTraceFactContractException contractFailure)
            {
                rejectionEvidence = contractFailure.RejectionEvidence;
                rejectionEvidenceDropped = contractFailure.DroppedEvidence;
            }
            failedStep = steps.LastOrDefault(s => !s.Verified)?.Name ?? (decoderStarted ? decoderPhase ?? "DecodeAsync" : "Capture/Workload");
            if (failedStep.StartsWith("Provider:", StringComparison.Ordinal)) failedStep = "EnableTraceEx2(" + failedStep[9..] + ")";
        }
        finally
        {
            try
            {
                if (child is not null)
                {
                    try
                    {
                        if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync().ConfigureAwait(false); error ??= "Eigener Workload musste beendet werden."; }
                        else exited = child.ExitCode == 0;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { error = "Workload-Cleanup: " + ex.Message; }
                    child.Dispose();
                    steps.Add(new("OwnWorkloadHandleClosed", null, true, clock.Elapsed.TotalMilliseconds));
                }
                try
                {
                    var safe = !started;
                    if (started)
                    {
                        var marker = Directory.GetFiles(directory, "*.etl.session").Single();
                        var stem = Path.GetFileName(marker)[..^12];
                        if (!Guid.TryParseExact(stem, "N", out var id)) throw new InvalidDataException("Sessionmarker besitzt keine eigene GUID.");
                        safe = GuardianTrace.InspectOrStop(id, directory, false) == "Nicht aktiv";
                        steps.Add(new("CleanupSessionAbsent", null, safe, clock.Elapsed.TotalMilliseconds));
                    }
                    if (safe)
                    {
                        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                        Directory.Delete(directory); cleaned = true;
                        steps.Add(new("OwnArtifactCleanup", null, true, clock.Elapsed.TotalMilliseconds));
                    }
                    else error = "Eigene Session nicht nachweislich beendet; Artefakte erhalten, weitere Starts unterbunden.";
                }
                catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
                { error = "Cleanupprüfung: " + ex.Message; }
                if (balance != 0) error = "Native Pufferbilanz ist " + balance;
                Sample("AfterOwnChildAndArtifactCleanup");
            }
            finally
            {
                try { privilegeScope?.Dispose(); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                { error = "Privilegrücksetzung: " + ex.Message; }
                Sample("AfterPrivilegeRestoreAndTokenClose");
            }
        }
        await SettleAsync().ConfigureAwait(false);
        Sample("AfterFixedSettling");
        var after = phases[^1].Resources;
        var gates = EvaluateCore(profileId, mode, maximumMiB, trace, analysis, workload, exited, steps, decoderStarted, decoderPassed, xmlDecoder).ToList();
        gates.Add(new("CleanupVerified", cleaned, cleaned.ToString(), "True", "Eigene Sessionabfrage + Artefaktbereinigung", "Fixture"));
        gates.Add(new("RetainedDirectoryIsNull", cleaned, cleaned ? "null" : directory, "null", "CaptureAsync finally", "Fixture"));
        gates.Add(new("ManagedNativeBufferBalanceIsZero", balance == 0, balance.ToString(), "0", "PropertiesBufferAllocated/Freed", "Produktcode"));
        gates.Add(new("OwnWorkloadHandleClosed", child is null || steps.Any(s => s.Name == "OwnWorkloadHandleClosed" && s.Verified),
            child is null ? "Kein Workload gestartet" : "Process.Dispose ausgeführt", "Eigenen Workload beenden und Prozesshandle freigeben",
            "CaptureAsync finally", "Cleanup"));
        gates.Add(new("ResourceGatePassed", mode == "cold" ? null : WithinWarmBudget(before, after),
            $"Handles {after.Handles - before.Handles}; Threads {after.Threads - before.Threads}",
            mode == "cold" ? "Kaltlauf separat, kein Warmbudget angewendet" : "Handles <=64; Threads <=8",
            "Process.GetCurrentProcess nach festem Nachlauf", "Gate", mode != "cold"));
        if (mode == "warm-2" && (enableCpuPrivilege || profileId == "custom"))
            gates.Add(new("WarmPlateau", previousWarmAfter is not null && WarmPlateau(previousWarmAfter, after),
                $"warm1={previousWarmAfter}; warm2={after}", "warm2 Handles/Threads <= warm1; kein wachsender warmer Endstand",
                "Endmessungen desselben Hosts nach gleichem Nachlauf", "Gate"));
        var converters = conversions.Where(c => c.State == "ProcessStarted").ToArray();
        gates.Add(new("OwnConvertersExited", converters.All(c => conversions.Any(e =>
            e.Format == c.Format && e.State == "ProcessExited" && e.Detail == c.Detail)), converters.Length.ToString(),
            "Alle gestarteten eigenen Konverter beendet und Streams abgewartet", "CommandRunner finally", "Cleanup"));
        if (enableCpuPrivilege)
            gates.Add(new("PrivilegeRestoredAndTokenClosed", PrivilegePassed(privilegeJournal),
                string.Join("; ", privilegeJournal.Select(e => e.Step + "=" + e.Succeeded)),
                "Aktivierung + Rücksetzung + Nachlesen + Handlefreigabe pro Lauf", "Eigenes Tokenjournal", "Privilege"));
        if (diagnosticOnly) gates.Add(new("DiagnosticOnly", false, "Diagnose", "Kein Abnahmeersatz, niemals PASS", "Runnervertrag", "Gate"));
        gates.Add(new("NoExecutionException", error is null, error ?? "keine", "keine", failedStep ?? "CaptureAsync", "Ausführung",
            mode is not ("cancel" or "shutdown-token") || !decoderStarted));
        return new(profileId, mode, before, after, steps, trace, analysis, workload, exited, observed,
            gates.All(g => !g.Required || g.Passed == true), error, native, cleaned, cleaned ? null : directory, balance,
            gates, failedStep, exceptionType, decoderStarted, decoderPassed, decoderPhase, xmlDecoder, xmlTimes)
        {
            Conversions = conversions,
            ResourcePhases = phases,
            PrivilegeJournal = privilegeJournal,
            RejectionEvidence = rejectionEvidence,
            RejectionEvidenceDropped = rejectionEvidenceDropped
        };
    }
    internal static IReadOnlyList<Gate> EvaluateCore(string profile, string mode, int maximumMiB, GuardianTraceResult? trace,
        GuardianTraceAnalysis? analysis, ProcessIdentity? workload, bool exited, IReadOnlyList<Step> steps,
        bool decoderStarted, bool decoderPassed, GuardianTraceDecodeStatus? xmlDecoder = null)
    {
        var gates = new List<Gate>(); var cancelled = mode is "cancel" or "shutdown-token";
        void Check(string name, bool? pass, object? actual, string expected, string source, string area, bool required = true) =>
            gates.Add(new(name, pass, actual?.ToString() ?? "nicht beobachtet", expected, source, area, required));
        bool StepPassed(string name) => steps.Any(s => s.Name == name && s.Verified);
        var providers = GuardianTraceProfiles.Get(profile);
        Check("SessionStarted", StepPassed("StartTrace"), steps.FirstOrDefault(s => s.Name == "StartTrace")?.NativeCode, "0", "StartTraceW", "Produktcode");
        var enables = steps.Where(s => s.Name.StartsWith("Provider:", StringComparison.Ordinal)).ToArray();
        Check("ProvidersEnabled", enables.Length > 0 && enables.All(s => s.NativeCode == 0) &&
            (profile == "custom" ? enables.Length == trace?.Components.Count : enables.Length == providers.Components.Count),
            string.Join("; ", enables.Select(s => $"{s.Name}={s.NativeCode}")), "Alle gewählten Provider: 0", "EnableTraceEx2", "Produktcode");
        Check("QuerySucceeded", steps.Any(s => s.Name is "QueryStatus" or "QueryBeforeStop" && s.NativeCode == 0),
            string.Join(",", steps.Where(s => s.Name == "QueryStatus").Select(s => s.NativeCode).Distinct()), "Eigene Statusabfrage 0", "ControlTrace Query", "Produktcode");
        var absent = StepPassed("SessionAbsentAfterStop");
        var automaticSize = mode == "size" && trace?.AutomaticEndCode == 4201 && absent && trace.Bytes == maximumMiB * 1048576L;
        Check("StopSucceeded", StepPassed("ControlTraceStop") && steps.Any(s => s.Name == "ControlTraceStop" && s.NativeCode == 0) || automaticSize,
            automaticSize ? "Automatisches Größenende 4201; eigene Session abwesend" : steps.LastOrDefault(s => s.Name == "ControlTraceStop")?.NativeCode,
            "Stop 0 oder belegtes eigenes Größenende", "ControlTrace Stop / Query / ETL-Größe", "Produktcode");
        Check("SessionAbsentAfterStop", absent, absent, "True", "ControlTrace Query = 4201", "Produktcode");
        Check("EtlExists", trace is { Bytes: > 0 }, trace?.Bytes, ">0 Bytes", "Eigene ETL vor Cleanup", "Produktcode");
        Check("EtlWithinBudget", trace is not null && trace.Bytes > 0 && trace.Bytes <= maximumMiB * 1048576L, trace?.Bytes,
            $"1..{maximumMiB * 1048576L}", "FileInfo.Length", "Produktcode");
        Check("DecoderStarted", decoderStarted, decoderStarted, "True", "CaptureAsync", "Fixture");
        Check("DecoderInvocationPassed", decoderPassed, decoderPassed, "True", "Windows tracerpt / EvtRender / XML-Parser", "Decoder");
        Check("DecoderCompleted", analysis is not null, analysis is not null, "True", "DecodeAsync Ergebnis", "Decoder");
        Check("DecoderOutputValid", analysis?.Decoder?.XmlValidated == true, analysis?.Decoder?.XmlValidated, "True", "ParseXml / ValidateAnalysis", "Decoder");
        var xml = xmlDecoder ?? analysis?.Decoder;
        Check("XmlProduced", xml?.XmlProduced, xml?.XmlProduced, "True", "tracerpt XML / ParseXml", "Decoder");
        Check("XmlValidated", xml?.XmlValidated, xml?.XmlValidated, "True", "tracerpt XML / XmlReader (DTD verboten)", "Decoder");
        Check("DecoderOutputComplete", analysis is null ? null : analysis.State == "DecodedObservedInterval", analysis?.State,
            "DecodedObservedInterval für Vollständigkeitsbehauptung; kein Kernfakten-Ersatz", "ParseXml.State", "Decoder", false);
        Check("NoMalformedFields", analysis?.Decoder is { MalformedFields: 0 } && (xmlDecoder is null || xmlDecoder.MalformedFields == 0),
            $"EvtRender={analysis?.Decoder?.MalformedFields}; tracerpt XML={xmlDecoder?.MalformedFields}", "0 in beiden Ausgaben", "ParseXml", "Decoder");
        Check("NoMissingIdentityFields", analysis?.Decoder is { MissingFields: 0 } &&
            (xmlDecoder is null || xmlDecoder.MissingFields == 0),
            $"Primary={analysis?.Decoder?.MissingFields}; Confirmation={xmlDecoder?.MissingFields}", "0 fehlende Pflichtfelder",
            "ParseXml Field / Missing", "Decoder", !cancelled);
        Check("EventsObserved", analysis?.TotalEvents > 0, analysis?.TotalEvents, ">0", "ParseXml", "Decoder");
        Check("FactsObserved", analysis?.Facts.Count > 0, analysis?.Facts.Count, ">0", "ParseXml", "Decoder");
        var identity = workload is { IsKnown: true } known ? known.ToString() : null;
        var own = analysis?.Facts.Where(f => identity is not null && f.Identity == identity && f.Availability == "Available").ToArray() ?? [];
        Check("WorkloadIdentityObserved", own.Any(f => f.Kind == "ProcessStart"), identity, "PID + exakte Provider-CreateTime = Workload.StartTicks", "Process.StartTime / ProcessStart.CreateTime", "Decoder", !cancelled);
        Check("ProcessStartObserved", own.Any(f => f.Kind == "ProcessStart"), own.Count(f => f.Kind == "ProcessStart"), ">=1 eigene Instanz", "ProcessStart", "Gate", !cancelled);
        Check("ProcessEndObserved", own.Any(f => f.Kind == "ProcessEnd"), own.Count(f => f.Kind == "ProcessEnd"), ">=1 gleiche eigene Instanz", "ProcessEnd", "Gate", !cancelled);
        var expectedKinds = profile switch
        {
            "cpu" => new[] { "CpuSample" },
            "io" => ["FileIO", "DiskIO"],
            "network" => ["NetworkMetadata"],
            "custom" => (trace?.Components ?? []).Where(c => c != "process")
                .Select(c => c switch { "file" => "FileIO", "disk" => "DiskIO", "network" => "NetworkMetadata", "cpu" => "CpuSample", _ => throw new InvalidDataException("Unbekannter Baustein.") }).ToArray(),
            _ => []
        };
        foreach (var kind in expectedKinds)
            Check("Required" + kind, analysis?.Facts.Any(f => f.Kind == kind && f.Availability == "Available") == true,
                analysis?.Facts.Count(f => f.Kind == kind && f.Availability == "Available"), ">=1 beobachteter Quellenfakt; keine erfundene Prozesszuordnung",
                "Provider-Allowlist / " + kind, "Gate", !cancelled);
        if (profile == "cpu")
        {
            var samples = analysis?.Facts.Where(f => f.Kind == "CpuSample" && f.Availability == "Available").ToArray() ?? [];
            Check("CpuSampleTimesWithinCapture", trace is not null && samples.Length > 0 &&
                samples.All(f => f.Time >= trace.CaptureBegan && f.Time <= trace.CaptureEnded), samples.Length,
                "Mindestens ein realer CPU-Quellenfakt, alle beobachteten Samples im Captureintervall",
                "Windows EvtRender / CPU-Allowlist", "Decoder");
        }
        Check("ExpectedWorkloadObserved", own.Any(f => f.Kind == "ProcessStart") && own.Any(f => f.Kind == "ProcessEnd") &&
            expectedKinds.All(k => analysis?.Facts.Any(f => f.Kind == k && f.Availability == "Available") == true),
            string.Join(",", own.Select(f => f.Kind).Distinct()) +
                (analysis?.Decoder?.EventLimitReached == true ? "; Eventlimit erreicht: fehlende Kernfakten innerhalb der ersten 10000 Ereignisse nicht nachgewiesen" : ""),
            "Eigener Start + Ende + verbindliche Profilquellen", "Qualifizierte Kernfakten", "Gate", !cancelled);
        Check("WorkloadExited", exited, exited, "True / Exit 0", "Process.WaitForExit / ExitCode", "Fixture");
        Check("LostEventsAcceptable", trace is { LossCountersVerified: true, LostEvents: 0 }, trace?.LostEvents,
            "0, Endzähler verifiziert", "ControlTrace Stop / OpenTrace ETL-Header", "Produktcode");
        Check("LostBuffersAcceptable", trace is { LossCountersVerified: true, LostBuffers: 0 }, trace?.LostBuffers,
            "0, Endzähler verifiziert", "ControlTrace Stop / OpenTrace ETL-Header", "Produktcode");
        Check("DurationWithinGate", trace?.ElapsedMilliseconds <= 12000, trace?.ElapsedMilliseconds, "<=12000 ms", "Stopwatch", "Gate");
        var customSoft = profile == "custom" && trace is { State: "Incomplete", SoftStopBytes: not null } &&
            trace.EndReason.StartsWith("Größenlimit: kontrollierter Soft-Stopp", StringComparison.Ordinal) &&
            steps.Any(s => s.Name == "ControlTraceStop" && s.NativeCode == 0) && trace.Bytes < maximumMiB * 1048576L;
        var expectedState = mode is "cancel" or "shutdown-token" ? "Cancelled" : mode == "size" || customSoft ? "Incomplete" : "Completed";
        Check("CaptureStateExpected", trace?.State == expectedState && (mode != "size" || trace.EndReason.Contains("Größe", StringComparison.Ordinal)),
            trace is null ? null : trace.State + " / " + trace.EndReason, expectedState + (mode == "size" ? " / Größenlimit" : ""), "RunProfileCoreAsync", "Produktcode");
        Check("SizeWithinGate", trace is not null && trace.Bytes <= maximumMiB * 1048576L &&
            (mode != "size" || trace.SoftStopBytes is { } threshold && trace.Bytes >= threshold &&
                trace.Bytes < maximumMiB * 1048576L && steps.Any(s => s.Name == "ControlTraceStop" && s.NativeCode == 0)),
            trace is null ? null : $"{trace.Bytes}; SoftStop={trace.SoftStopBytes}",
            mode == "size" ? "Soft-Grenze erreicht, echter Stop 0 vor hartem Dateilimit; Verluste weiter 0" : $"<={maximumMiB} MiB", "FileInfo.Length / SoftStopBytes", "Gate");
        var qpc = trace is null ? double.NaN : (trace.QpcEnd - trace.QpcStart) / (double)trace.QpcFrequency;
        Check("UtcQpcAnchorsConsistent", trace is not null && Math.Abs((trace.Ended - trace.Started).TotalSeconds - qpc) <= 1,
            trace is null ? null : ((trace.Ended - trace.Started).TotalSeconds - qpc), "Differenz <=1 s; keine Zeitrekonstruktion", "UTC-/Stopwatch-Grenzanker", "Gate");
        Check("CoreEventTimesWithinCapture", trace is not null && own.Length > 0 &&
            own.Where(f => f.Kind is "ProcessStart" or "ProcessEnd").All(f => f.Time >= trace.CaptureBegan && f.Time <= trace.CaptureEnded),
            string.Join("; ", own.Where(f => f.Kind is "ProcessStart" or "ProcessEnd").Select(f => f.Time.ToString("O"))),
            "Eigener Start/Ende im beobachteten Aufnahmeintervall", "Windows EvtRender SystemTime", "Decoder", !cancelled);
        var sourceRows = analysis?.Decoder?.Sources.Where(s => s.Identity == identity).ToArray() ?? [];
        Check("SourceContractVerified", sourceRows.Length >= 2 && sourceRows.All(s => s.IdentityState == "Confirmed" && s.PrimaryTimeVerified),
            string.Join("; ", sourceRows.Select(s => $"{s.Kind}: Identity={s.IdentityState}; Time={s.TimeState}; PrimaryVerified={s.PrimaryTimeVerified}")),
            "Primary EvtRender PID+CreateTime bestätigt durch tracerpt; Primary-Zeit unabhängig provider-/capturebestätigt; kein Feldmix",
            "ConfirmSources", "Decoder", !cancelled && decoderPassed && analysis?.Decoder?.TimeSource.Contains("EvtRender", StringComparison.Ordinal) == true);
        Check("FactLimitReached", analysis?.Decoder is { FactLimitReached: false }, analysis?.Decoder?.FactLimitReached,
            "Begrenzung separat; Kernfakten bleiben Pflicht", "Priorisierte Auswahl max.2000", "Decoder", false);
        Check("EventLimitReached", analysis?.Decoder is { EventLimitReached: false }, analysis?.Decoder?.EventLimitReached,
            "Begrenzung separat; keine Vollständigkeit", "ParseXml max.10000", "Decoder", false);
        if (cancelled)
            for (var index = 0; index < gates.Count; index++)
                if (gates[index].Name is "NoMalformedFields" or "DecoderStarted" or "DecoderInvocationPassed" or "DecoderCompleted" or
                    "DecoderOutputValid" or "XmlProduced" or "XmlValidated" or "EventsObserved" or "FactsObserved")
                    gates[index] = gates[index] with { Required = false };
        return gates;
    }
}
