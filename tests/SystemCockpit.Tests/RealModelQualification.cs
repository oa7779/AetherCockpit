using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using AetherOperations;
using AetherOperations.Intelligence;
using AetherOperations.Platform;
using SystemCockpit.Services;

internal static class RealModelQualification
{
    private const string PhiId = "phi4-mini:3.8b";
    private const string NemotronId = "nemotron-3-nano:4b";
    private const string Endpoint = "http://127.0.0.1:11435/";
    private static readonly Guid LocalTarget = new("c4e6d34c-0159-4bf8-9d99-b0c6a9c5d399");
    private const long Gib = 1024L * 1024 * 1024;

    private sealed record HardwarePoint(DateTimeOffset At, long? AvailableRamBytes, long? TotalVramBytes,
        long? AvailableVramBytes, string? Gpu);
    private sealed record InferenceRun(string PromptHash, int ContextCharacters, int? InputTokens,
        int? OutputTokens, int? InitialInputTokens, int? InitialOutputTokens, int? RepairInputTokens,
        int? RepairOutputTokens, double? ProviderLoadSeconds, double? GenerationSeconds,
        double? FirstTokenSeconds, double AssistantMilliseconds, long AetherProcessWorkingSetBeforeBytes,
        long AetherProcessWorkingSetAfterBytes, bool RealModel, bool? ValidatorSucceeded,
        bool RepairRetryStarted, bool ContractShapePassed, FabricDiagnosticReport? Report);
    private sealed record ModelResult(string ModelId, string Digest, string OllamaVersion, string Endpoint,
        Guid EvidenceId, int EventCount, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
        double? ColdLoadMilliseconds, double? WarmLoadMilliseconds,
        HardwarePoint? BeforeLoad, HardwarePoint? AfterColdLoad, HardwarePoint? AfterWarmLoad,
        long? ResidentModelBytes, long? ResidentModelVramBytes, InferenceRun? ColdInference,
        InferenceRun? WarmInference, string? Failure);
    private sealed record QualificationReport(int Schema, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
        string EvidenceScope, int EventId, int EventCount, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
        Guid EvidenceId, string SharedPromptHash, int ContextCharacters,
        string DiagnosticContract, string ActionContract, string SecurityContract, ImmutableArray<ModelResult> Models);

    public static async Task<int> RunAsync(string phiStoreRoot, string nemotronStoreRoot, string reportPath)
    {
        ValidateOutputPath(reportPath);
        ValidateStoreRoot(phiStoreRoot);
        ValidateStoreRoot(nemotronStoreRoot);
        var catalog = AetherProductModelCatalog.Current;
        var phiProduct = catalog.Models.Single(model => model.ModelId == PhiId);
        var nemotronProduct = catalog.Models.Single(model => model.ModelId == NemotronId);
        if (phiProduct.QualificationStatus != AetherModelQualificationStatus.Qualified ||
            !phiProduct.ApprovedForAutomaticSelection ||
            nemotronProduct.QualificationStatus is not (AetherModelQualificationStatus.Pending or AetherModelQualificationStatus.Rejected) ||
            nemotronProduct.ApprovedForAutomaticSelection)
            throw new InvalidDataException("Der bestehende Phi-/Nemotron-Qualifikationsstatus ist unerwartet.");

        var group = ReadEventGroup();
        var started = DateTimeOffset.UtcNow;
        var evidenceId = Guid.NewGuid();
        var evidence = CreateEvidence(group, evidenceId);
        const string input = "Analysiere die konkrete Ereignisgruppe Netwaw20 Ereignis-ID 5010. Die Ursache bleibt offen; verwende ausschließlich den bereitgestellten Beleg.";
        var targets = ImmutableArray.Create(LocalTarget);
        var sharedContext = new AetherContextManager().Build(input, targets, [], [evidence]);
        var sharedPromptHash = AetherPromptRegistry.HashFor("diagnosis-report", sharedContext);

        var phi = CreateFabricModel(phiProduct, PhiId);
        var nemotron = CreateFabricModel(nemotronProduct, NemotronId);
        var results = ImmutableArray.CreateBuilder<ModelResult>(2);
        results.Add(await RunModelAsync(phi, phiStoreRoot, input, targets, evidence, evidenceId, group, CancellationToken.None));
        results.Add(await RunModelAsync(nemotron, nemotronStoreRoot, input, targets, evidence, evidenceId, group, CancellationToken.None));
        var entries = results.ToImmutable();
        if (entries.SelectMany(result => new[]
            {
                result.ColdInference?.PromptHash, result.WarmInference?.PromptHash
            }).Any(hash => hash is not null && hash != sharedPromptHash))
            throw new InvalidDataException("Die A/B-Läufe verwendeten nicht denselben AETHER-Prompt- und Evidence-Scope.");

        var report = new QualificationReport(1, started, DateTimeOffset.UtcNow,
            "Systemprotokoll, letzte 24 Stunden, Provider Netwaw20, Ereignis-ID 5010; nur aggregierte Gruppe, keine Rohmeldungen",
            group.EventId, group.Count, group.FirstSeen, group.LastSeen, evidenceId, sharedPromptHash,
            sharedContext.Length, "AETHER Diagnosebericht Schema 2; Belegbindung; Validator; Reparaturversuch begrenzt",
            "Gleicher registrierter Aktionskatalog; nur beleggebundene Aktion events.open; keine Ausführung",
            "AETHER LocalOnly; Loopback-Endpoint; keine externen Daten; keine Systemänderung",
            entries);
        var options = new JsonSerializerOptions(FabricJson.Options) { WriteIndented = true };
        await using (var stream = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, report, options).ConfigureAwait(false);

        Console.WriteLine(JsonSerializer.Serialize(report, options));
        Console.WriteLine($"Ergebnisdatei: {reportPath}");
        return entries.All(result => result.Failure is null &&
            result.ColdInference is { RealModel: true, ValidatorSucceeded: true, ContractShapePassed: true,
                ProviderLoadSeconds: not null } &&
            result.WarmInference is { RealModel: true, ValidatorSucceeded: true, ContractShapePassed: true,
                ProviderLoadSeconds: not null }) ? 0 : 2;
    }

    private static async Task<ModelResult> RunModelAsync(FabricModel model, string storeRoot, string input,
        ImmutableArray<Guid> targets, FabricEvidence evidence, Guid evidenceId, FabricEventGroup group,
        CancellationToken token)
    {
        var endpoint = new Uri(Endpoint);
        var runtime = new LocalOllamaRuntime(endpoint, storeRoot, AetherOllamaExecutable.Find,
            AetherOllamaExecutable.ReadSignatureAsync);
        using var bootstrapProvider = new CompatibleInferenceProvider("aether.qualification.bootstrap",
            FabricProviderKind.Ollama, endpoint);
        var runtimeDiagnostics = new FabricRunDiagnostics();
        CompatibleInferenceProvider? provider = null;
        try
        {
            var preflightHardware = await ReadHardwareSnapshotAsync(token).ConfigureAwait(false);
            var preflightContext = new AetherContextManager().Build(input, targets, [], [evidence]);
            var request = new FabricRequest(1, Guid.NewGuid(), "diagnosis-report", "de", preflightContext,
                AetherPromptRegistry.HashFor("diagnosis-report", preflightContext), model.Id,
                Math.Min(8192, model.DeclaredContextTokens), 2048, 60, DataClassification.Sensitive);
            new AetherResourceGovernor(() => preflightHardware).Check(model, request, 1, FabricMode.LocalOnly);
            var actualEndpoint = await runtime.EnsureAvailableAsync(bootstrapProvider, model, runtimeDiagnostics, token)
                .ConfigureAwait(false);
            if (runtimeDiagnostics.RuntimeStartedByAether != true || runtimeDiagnostics.RuntimeStoreVerified != true ||
                runtimeDiagnostics.RuntimeProcessId is not { } runtimePid ||
                !LocalOllamaRuntime.IsListenerOwnedBy(actualEndpoint.Port, runtimePid))
                throw new InvalidDataException("Die isolierte Runtime ist nicht eindeutig AETHER-eigen.");

            var versionProvider = new CompatibleInferenceProvider("aether.qualification.version",
                FabricProviderKind.Ollama, actualEndpoint);
            using (versionProvider)
            {
                var version = await versionProvider.ReadOllamaVersionAsync(token).ConfigureAwait(false);
                provider = new CompatibleInferenceProvider("aether.qualification.model",
                    FabricProviderKind.Ollama, actualEndpoint);
                await provider.CheckModelAsync(model, token).ConfigureAwait(false);

                var alreadyLoaded = await provider.IsLoadedAsync(model, token).ConfigureAwait(false);
                if (alreadyLoaded)
                    throw new InvalidDataException("Frisch gestartete isolierte Runtime meldet unerwartet ein resident geladenes Modell.");

                FabricHardware currentHardware = preflightHardware;
                var liveGovernor = new AetherResourceGovernor(() => currentHardware, async (_, refreshToken) =>
                    currentHardware = await ReadHardwareSnapshotAsync(refreshToken).ConfigureAwait(false));
                using var scheduler = new AetherInferenceScheduler(liveGovernor, new AetherModelTelemetry(), 1);
                var assistant = new AetherAssistant(scheduler);

                var beforeHardware = ToPoint(await ReadHardwareSnapshotAsync(token).ConfigureAwait(false));
                var coldDiagnostics = new FabricRunDiagnostics();
                var coldWorkingSetBefore = Environment.WorkingSet;
                var cold = await assistant.AnswerAsync(input, targets, [evidence], [], model, provider,
                    "Qualification/A-B", FabricMode.LocalOnly, token, diagnostics: coldDiagnostics)
                    .ConfigureAwait(false);
                var coldWorkingSetAfter = Environment.WorkingSet;
                var afterCold = ToPoint(await ReadHardwareSnapshotAsync(token).ConfigureAwait(false));
                var coldResident = await provider.IsLoadedAsync(model, token).ConfigureAwait(false);
                var coldInventory = provider.LastInventory;
                if (!coldResident)
                    throw new InvalidDataException("Das Modell war nach der Kaltinferenz nicht resident geladen.");

                var warmDiagnostics = new FabricRunDiagnostics();
                var warmWorkingSetBefore = Environment.WorkingSet;
                var warm = await assistant.AnswerAsync(input, targets, [evidence], [], model, provider,
                    "Qualification/A-B", FabricMode.LocalOnly, token, diagnostics: warmDiagnostics)
                    .ConfigureAwait(false);
                var warmWorkingSetAfter = Environment.WorkingSet;
                var afterWarm = ToPoint(await ReadHardwareSnapshotAsync(token).ConfigureAwait(false));
                var finalResident = await provider.IsLoadedAsync(model, token).ConfigureAwait(false);
                var warmInventory = provider.LastInventory;
                var finalInventory = provider.LastInventory;
                if (coldInventory?.Resident != true || warmInventory?.Resident != true || !finalResident ||
                    finalInventory?.ResidentModelBytes is null ||
                    finalInventory.ResidentModelVramBytes is null)
                    throw new InvalidDataException("Ollama hat den erwarteten residenten Modellstatus nicht vollständig gemeldet.");

                var modelResult = new ModelResult(model.Id, model.Digest, version, actualEndpoint.ToString(),
                    evidenceId, group.Count, group.FirstSeen, group.LastSeen, cold.LoadSeconds is { } coldLoad
                        ? coldLoad * 1000 : null, warm.LoadSeconds is { } warmLoad ? warmLoad * 1000 : null,
                    beforeHardware, afterCold, afterWarm, finalInventory.ResidentModelBytes,
                    finalInventory.ResidentModelVramBytes,
                    CaptureInference(cold, coldDiagnostics, coldWorkingSetBefore, coldWorkingSetAfter, evidenceId),
                    CaptureInference(warm, warmDiagnostics, warmWorkingSetBefore, warmWorkingSetAfter, evidenceId),
                    null);
                return modelResult;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or
            InvalidOperationException or UnauthorizedAccessException or FabricResourceException or
            System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return new(model.Id, model.Digest, "", "", evidenceId, group.Count, group.FirstSeen,
                group.LastSeen, null, null, null, null, null, null, null, null, null,
                ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            provider?.Dispose();
            await LocalOllamaRuntime.StopOwnedAsync().ConfigureAwait(false);
        }
    }

    private static FabricModel CreateFabricModel(AetherProductModel product, string modelId)
    {
        if (product.ExpectedOllamaDigest is not { } digest || product.DownloadSource is not { } origin ||
            product.LicenseName is not { } license || product.Quantization is not { } quantization ||
            product.Revision is not { } revision)
            throw new InvalidDataException($"Der freigegebene Katalogeintrag für {modelId} ist unvollständig.");
        return new(modelId, revision, product.ModelFamily ?? product.DisplayName, quantization, license,
            origin, digest, product.SupportedLanguages, Math.Min(8192, product.ContextLimitTokens ?? 8192),
            product.RequiredModelRamBytes ?? (4 * Gib),
            product.RequiredModelVramBytes ?? (3 * Gib),
            FabricTrust.OperatorApproved, true, true, true, "Ollama");
    }

    private static FabricEvidence CreateEvidence(FabricEventGroup group, Guid evidenceId)
    {
        var now = DateTimeOffset.UtcNow;
        var value = JsonSerializer.Serialize(group, FabricJson.Options);
        return new(evidenceId, LocalTarget, "eventGroup.0", value, "Ereignisse",
            new SourceMetadata("Windows-Systemprotokoll; aggregierte Ereignisgruppe", SourceType.Direct,
                now, DataConfidence.ProviderReported, DataAvailability.Available,
                classification: DataClassification.Sensitive), "AETHER-Modellqualifikation", false);
    }

    private static bool HasContractShape(FabricDiagnosticReport? report, Guid evidenceId)
    {
        if (report is not { Schema: 2, Language: "de" } ||
            !report.SystemState.SequenceEqual([evidenceId]))
            return false;
        var diagnosis = report.Diagnosis;
        return diagnosis.Findings.Length == 1 &&
            diagnosis.Findings[0].Proof == FabricDiagnosticProof.Measured &&
            diagnosis.Findings[0].Evidence.SequenceEqual([evidenceId]) &&
            diagnosis.Hypotheses.Length == 1 &&
            diagnosis.Hypotheses[0].Confidence == FabricHypothesisConfidence.Unconfirmed &&
            diagnosis.Hypotheses[0].Evidence.SequenceEqual([evidenceId]) &&
            !diagnosis.Hypotheses[0].MissingOrContradictory.IsEmpty &&
            diagnosis.Checks.Length == 1 && diagnosis.Checks[0].HypothesisIndex == 0 &&
            diagnosis.Checks[0].SourceEvidence == evidenceId &&
            diagnosis.Solutions.Length == 1 && diagnosis.Solutions[0].HypothesisIndex == 0 &&
            diagnosis.Solutions[0].CheckIndex == 0 &&
            diagnosis.Solutions[0].Evidence.SequenceEqual([evidenceId]) &&
            diagnosis.Solutions[0].RequiresApproval &&
            diagnosis.Solutions[0].Risk == FabricSolutionRisk.Unknown &&
            diagnosis.Actions.Length == 1 && diagnosis.Actions[0].ActionId == "events.open" &&
            diagnosis.Actions[0].Evidence.SequenceEqual([evidenceId]);
    }

    private static InferenceRun CaptureInference(FabricAssistantResult result, FabricRunDiagnostics diagnostics,
        long workingSetBefore, long workingSetAfter, Guid evidenceId) =>
        new(result.PromptHash, result.ContextCharacters, result.InputTokens, result.OutputTokens,
            diagnostics.InitialInputTokens, diagnostics.InitialOutputTokens,
            diagnostics.RepairInputTokens, diagnostics.RepairOutputTokens,
            result.LoadSeconds, result.GenerationSeconds, result.FirstTokenSeconds,
            result.Milliseconds, workingSetBefore, workingSetAfter, result.RealModel,
            diagnostics.ValidatorSucceeded, diagnostics.RepairRetryStarted,
            HasContractShape(result.DiagnosticReport, evidenceId), result.DiagnosticReport);

    private static FabricEventGroup ReadEventGroup()
    {
        const string xpath = "*[System[Provider[@Name='Netwaw20'] and EventID=5010 and TimeCreated[timediff(@SystemTime) <= 86400000]]]";
        var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = false };
        using var reader = new EventLogReader(query);
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;
        byte? level = null;
        var count = 0;
        EventRecord? record;
        while ((record = reader.ReadEvent()) is not null)
        {
            using (record)
            {
                if (record.TimeCreated is not { } time || record.Level is not (1 or 2))
                    throw new InvalidDataException("Netwaw20/5010-Ereignis hat keinen gültigen Zeitstempel oder Level.");
                var observedLevel = checked((byte)record.Level.Value);
                if (level is not null && level != observedLevel)
                    throw new InvalidDataException("Die Netwaw20/5010-Gruppe enthält uneinheitliche Ereignisstufen.");
                level = observedLevel;
                var timestamp = new DateTimeOffset(time).ToUniversalTime();
                first = first is null || timestamp < first ? timestamp : first;
                last = last is null || timestamp > last ? timestamp : last;
                count++;
                if (count > 128)
                    throw new InvalidDataException("Die aktuelle Ereignisgruppe überschreitet das AETHER-Gruppenlimit von 128.");
            }
        }
        if (count == 0 || first is null || last is null || level is null)
            throw new InvalidDataException("Im aktuellen 24-Stunden-Fenster liegt keine qualifizierbare Netwaw20/5010-Gruppe vor.");
        return new("System", "Netwaw20", 5010, level, null, first.Value, last.Value, count);
    }

    private static async Task<FabricHardware> ReadHardwareSnapshotAsync(CancellationToken token)
    {
        var sample = new NativeMetrics().Read();
        var state = IntelligenceHardwareProbe.FromSample(sample);
        return await IntelligenceHardwareProbe.ReadGpuAsync(state, token).ConfigureAwait(false);
    }

    private static HardwarePoint ToPoint(FabricHardware state) =>
        new(state.At, state.AvailableRamBytes, state.TotalVramBytes, state.AvailableVramBytes, state.Gpu);

    private static void ValidateStoreRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Storepfade müssen absolut sein.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var acceptanceRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AETHER", "LocalAI.Acceptance");
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(acceptanceRoot),
                StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullPath))
            throw new InvalidDataException("Modellstore muss ein vorhandener direkter Unterordner des AETHER-Acceptance-Root sein.");
    }

    private static void ValidateOutputPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || File.Exists(path) || Directory.Exists(path))
            throw new ArgumentException("Ergebnisdatei muss ein neuer absoluter Pfad sein.", nameof(path));
    }
}
