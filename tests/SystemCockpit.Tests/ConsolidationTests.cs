using System.IO;
using System.Text.Json;
using AetherOperations;
using SystemCockpit;
using SystemCockpit.Services;

internal static class ConsolidationTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void Reject<T>(Action action) where T : Exception
        {
            count++;
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }

        var at = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        SourceMetadata Metadata(DataAvailability availability, SourceType type = SourceType.Direct) =>
            new("fixture-provider", type, at, DataConfidence.Unknown, availability);
        var zero = new SensorReading("fixture", "fixture", "load", "Load", 0, "%");
        var observation = zero.Observe(Metadata(DataAvailability.Available));
        Check(observation.HasValue && observation.Value == 0, "A measured zero remains present.");
        foreach (var state in Enum.GetValues<DataAvailability>().Where(s => s is not (DataAvailability.Available or DataAvailability.Stale)))
        {
            var missing = (zero with { Value = null }).Observe(Metadata(state));
            Check(!missing.HasValue && missing.Metadata.Availability == state, "Missing states remain distinct: " + state);
            Reject<ArgumentException>(() => zero.Observe(Metadata(state)));
        }
        Reject<ArgumentException>(() => (zero with { Value = null }).Observe(Metadata(DataAvailability.Available)));
        Reject<ArgumentException>(() => (zero with { Value = double.NaN }).Observe(Metadata(DataAvailability.Available)));
        Reject<ArgumentOutOfRangeException>(() => SourceObservation<double>.Present(double.PositiveInfinity, "%", Metadata(DataAvailability.Available)));
        var stale = observation.AsStale("refresh-failed");
        Check(stale.HasValue && stale.Metadata.Timestamp == at && stale.Metadata.Availability == DataAvailability.Stale,
            "A retained value keeps its original timestamp and explicitly becomes stale.");
        Check(observation.Metadata.Availability == DataAvailability.Available, "Stale adaptation does not mutate the source.");
        Check(zero.Observe(Metadata(DataAvailability.Available, SourceType.Derived)).Metadata.IsCalculated, "Derived values are calculated.");
        Check(zero.Observe(Metadata(DataAvailability.Available, SourceType.Database)).Metadata.SourceType == SourceType.Database,
            "Database values never become direct measurements.");
        Reject<ArgumentException>(() => new SourceMetadata("fixture", SourceType.Unconfirmed, at,
            DataConfidence.Corroborated, DataAvailability.Available));
        Reject<ArgumentException>(() => new SourceMetadata("fixture", SourceType.Direct, at,
            DataConfidence.Unknown, DataAvailability.Available, documentationLink: new Uri("file:///C:/fixture")));
        foreach (var field in new[] { "SerialNumber", "MacAddress", "MonitorId", "PnpId", "AudioId",
                     "ComputerName", "UserName", "UserPath", "IpAddress", "Ipv6Address", "DeviceId", "DumpInformation" })
            Check(DiagnosticDataPolicy.Classify(field) == DataClassification.Sensitive, "Sensitive field: " + field);
        foreach (var field in new[] { "Password", "Token", "RecoveryKey", "PrivateKey", "CertificatePrivateKey" })
            Check(!DiagnosticDataPolicy.CanShare(DiagnosticDataPolicy.Classify(field), true), "Secrets never share: " + field);
        foreach (var field in new[] { "deviceToken", "accessToken", "refreshToken", "claimToken", "csrfToken",
                     "passwordHash", "protectedToken", "clientSecret", "recoveryPassword", "TOKEN" })
            Check(DiagnosticDataPolicy.Classify(field) == DataClassification.Secret, "Wire field aliases remain secret: " + field);
        Reject<ArgumentException>(() => DiagnosticDataPolicy.Classify(""));
        Check(!DiagnosticDataPolicy.CanShare(DiagnosticDataPolicy.Classify("new-unreviewed-field"), false), "Unknown fields default to sensitive.");
        Check(DiagnosticDataPolicy.CanShare(DataClassification.Normal, false), "Normal fields remain shareable.");
        Check(DiagnosticDataPolicy.CanShare(DataClassification.Sensitive, true), "Sensitive sharing requires explicit consent.");
        Check(!DiagnosticDataPolicy.CanShare((DataClassification)99, true), "Unknown classifications fail closed.");
        Check(OperationOutcome.FromProcess("fixture", 0, false).State == OperationState.Success, "Complete zero-exit success.");
        Check(OperationOutcome.FromProcess("fixture", 0, true).State == OperationState.Partial, "Truncated zero-exit is partial.");
        Check(OperationOutcome.FromProcess("fixture", 1, true).State == OperationState.Failed, "Truncation cannot conceal process failure.");
        Check(OperationOutcome.FromException(new UnauthorizedAccessException(), "fixture").State == OperationState.AccessDenied, "Access denied distinct.");
        Check(OperationOutcome.FromException(new FileNotFoundException(), "fixture").Failure == FailureKind.MissingTool, "Missing tool distinct.");
        Check(OperationOutcome.FromException(new System.ComponentModel.Win32Exception(2), "fixture").Failure == FailureKind.MissingTool,
            "Native missing executable is classified without parsing its localized message.");
        Check(OperationOutcome.FromException(new System.Net.Sockets.SocketException(10061), "fixture").Failure == FailureKind.Network,
            "Socket errors retain their network category.");
        Check(DiagnosticDataPolicy.Classify("Unit") == DataClassification.Normal, "Explicitly reviewed technical field remains normal.");
        Check(OperationOutcome.FromException(new TimeoutException(), "fixture").State == OperationState.TimedOut, "Timeout distinct.");
        Check(OperationOutcome.FromException(new PlatformNotSupportedException(), "fixture").State == OperationState.Unsupported, "Unsupported distinct.");
        Check(OperationOutcome.FromException(new System.Net.Http.HttpRequestException(), "fixture").Failure == FailureKind.Network, "Network failure distinct.");
        Check(OperationOutcome.FromException(new OperationCanceledException(), "fixture").State == OperationState.Unknown,
            "Unknown cancellation origin is not inferred as timeout.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Check(OperationOutcome.FromException(new OperationCanceledException(cancelled.Token), "fixture", cancelled.Token).State == OperationState.Cancelled,
            "Caller cancellation requires caller evidence.");
        var sensorJson = JsonSerializer.Serialize(zero);
        Check(!sensorJson.Contains("Metadata", StringComparison.Ordinal), "Legacy sensor wire shape unchanged.");
        Check(JsonSerializer.Deserialize<SensorReading>(sensorJson) == zero, "Legacy sensor roundtrip unchanged.");
        var command = new CommandResult(0, "fixture", "");
        Check(command.Outcome().State == OperationState.Success, "Command adapter is compatible.");
        Check(new ManagedActionResult(0, "fixture", true).Outcome().State == OperationState.Partial, "Shared adapter retains truncation.");
        foreach (var classification in Enum.GetValues<DataClassification>())
        {
            var entry = new TechnicalEvent("fixture", "read", OperationPhase.Finished, DiagnosticSeverity.Audit,
                "fixture-provider", new("SYNTHETIC-PRIVATE-VALUE", classification), true, null,
                StopBehavior.WaitForCurrentStep, false, Outcome: OperationOutcome.FromProcess("fixture", 0, true));
            var json = JsonSerializer.Serialize(entry.JournalDetails());
            Check(json.Contains("SYNTHETIC-PRIVATE-VALUE", StringComparison.Ordinal) == (classification == DataClassification.Normal),
                "Structured journal excludes sensitive and secret targets.");
            using var parsed = JsonDocument.Parse(json);
            Check(parsed.RootElement.GetProperty("userConfirmed").ValueKind == JsonValueKind.Null,
                "Unknown confirmation cannot be asserted as consent.");
            Check(parsed.RootElement.GetProperty("rollbackAvailable").GetBoolean() == false, "No rollback is promised.");
            Check(parsed.RootElement.GetProperty("outcome").GetProperty("state").GetString() == "Partial",
                "Journal outcomes use explicit technical names rather than opaque enum numbers.");
            var finished = entry with { Phase = OperationPhase.Finished };
            Check(finished.OperationId == entry.OperationId, "Start and end retain their operation correlation.");
            using var envelope = JsonDocument.Parse(ActionJournal.FormatTechnical("fixture-end", entry, new { target = "fixture-target", report = "fixture-report", ExitCode = 0 }));
            Check(envelope.RootElement.GetProperty("details").GetProperty("target").GetString() == "fixture-target" &&
                envelope.RootElement.GetProperty("details").GetProperty("ExitCode").GetInt32() == 0,
                "Existing journal details remain unchanged; technical metadata is additive.");
        }
        Check(Enum.GetValues<DiagnosticSeverity>().Length == 8, "All eight diagnostic/audit levels are represented.");
        Check(ActionJournal.MaxFileBytes == 16 * 1024 * 1024 && ActionJournal.MaxFiles == 30, "Journal limits preserved.");
        return count;
    }
}
