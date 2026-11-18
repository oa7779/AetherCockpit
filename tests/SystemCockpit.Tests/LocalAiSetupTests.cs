using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AetherOperations.Intelligence;

internal static class LocalAiSetupTests
{
    private sealed class SetupOperations : IAetherLocalAiSetupOperations
    {
        public AetherOllamaRuntimeStatus Runtime { get; set; } =
            new(false, false, false, null, null, true, false, false, null, false, false);
        public int Inspections { get; private set; }
        public int Installations { get; private set; }
        public int Configurations { get; private set; }
        public int Pulls { get; private set; }
        public int Verifications { get; private set; }
        public int HealthChecks { get; private set; }
        public int PersistedCatalogs { get; private set; }
        public int OwnershipRecoveries { get; private set; }
        public int ServerStops { get; private set; }
        public bool HealthCheckPasses { get; set; } = true;
        public string? DigestOverride { get; set; }
        public bool CancelPull { get; set; }

        public Task<AetherOllamaRuntimeStatus> InspectRuntimeAsync(AetherProductModel model, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Inspections++;
            return Task.FromResult(Runtime);
        }

        public Task<AetherModelVerification> RecoverAcceptanceStoreOwnershipAsync(
            AetherProductModel model, bool licenseAccepted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            OwnershipRecoveries++;
            return Task.FromResult(new AetherModelVerification(model.ExpectedOllamaDigest!,
                model.ExpectedLayers.Select(layer => layer.Digest).ToImmutableArray()));
        }

        public Task InstallOfficialRuntimeAsync(IProgress<AetherLocalAiSetupProgress>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Installations++;
            Runtime = Runtime with
            {
                Installed = true,
                OfficialSignatureValid = true,
                ApiCompatible = true,
                Version = "fixture",
                ExecutablePath = @"C:\fixture\ollama.exe",
                LoopbackOnly = true,
                ListenerAvailable = false,
                StoreConfigurationVerified = true,
                ModelStorePath = AetherLocalAiStoreContract.ModelStorePath,
                ModelStoreOwned = true
            };
            return Task.CompletedTask;
        }

        public Task ConfigureManagedStoreAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Configurations++;
            Runtime = Runtime with
            {
                ModelStorePath = AetherLocalAiStoreContract.ModelStorePath,
                ModelStoreOwned = true,
                StoreConfigurationVerified = true,
                LoopbackOnly = true,
                ListenerAvailable = false
            };
            return Task.CompletedTask;
        }

        public Task StartManagedServerAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Runtime = Runtime with
            {
                ApiCompatible = true,
                ListenerAvailable = true,
                StoreConfigurationVerified = true
            };
            return Task.CompletedTask;
        }

        public Task StopManagedServerAsync()
        {
            ServerStops++;
            Runtime = Runtime with { ListenerAvailable = false };
            return Task.CompletedTask;
        }

        public Task PullModelAsync(AetherProductModel model, IProgress<AetherLocalAiSetupProgress>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Pulls++;
            progress?.Report(new(AetherLocalAiSetupPhase.DownloadingModel, model.DownloadSizeBytes, model.DownloadSizeBytes));
            if (CancelPull) throw new OperationCanceledException(token);
            Runtime = Runtime with { ModelPresent = true, ModelStoreOwned = true, ModelOwned = true };
            return Task.CompletedTask;
        }

        public Task<AetherModelVerification> VerifyModelAsync(AetherProductModel model, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Verifications++;
            return Task.FromResult(new AetherModelVerification(DigestOverride ?? model.ExpectedOllamaDigest!,
                model.ExpectedLayers.Select(layer => layer.Digest).ToImmutableArray()));
        }

        public Task<bool> RunFixedHealthCheckAsync(AetherProductModel model, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            HealthChecks++;
            return Task.FromResult(HealthCheckPasses);
        }

        public Task PersistQualifiedCatalogAsync(AetherProductModel model, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            PersistedCatalogs++;
            return Task.CompletedTask;
        }
    }

    internal static int Run()
    {
        var assertions = 0;
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(message);
        }

        var catalog = AetherProductModelCatalog.Current;
        var phi = catalog.Models.Single(model => model.ModelId == "phi4-mini:3.8b");
        Check(catalog.Schema == 1 && catalog.Version == 3 && phi.Quantization == "Q4_K_M" &&
            phi.ExpectedOllamaDigest == "78fad5d182a7c33065e153a5f8ba210754207ba9d91973f57dffa7f487363753" &&
            phi.ExpectedLayers.Single(layer => layer.MediaType == "application/vnd.ollama.image.model").SizeBytes == 2491874624 &&
            phi.DownloadSizeBytes == 2491876774 && phi.InstallationSizeBytes == 2491876774 &&
            phi.ContextLimitTokens == 8192 && phi.QualificationStatus == AetherModelQualificationStatus.Qualified &&
            phi.ApprovedForAutomaticSelection && phi.MinimumRamBytes == 17L * 1024 * 1024 * 1024 &&
            phi.RequiredModelRamBytes == 600L * 1024 * 1024 &&
            phi.RequiredModelVramBytes == 3171L * 1024 * 1024 &&
            phi.MinimumAvailableRamBytes == (8L * 1024 * 1024 * 1024) + (600L * 1024 * 1024) &&
            phi.MinimumVramBytes == (3171L * 1024 * 1024) + (2L * 1024 * 1024 * 1024) &&
            phi.MinimumStorageBytes == 3L * 2491876774 &&
            phi.StorageSafetyReserveBytes == 2L * 2491876774 && phi.MaximumParallelRequests == 1 &&
            phi.MinimumWindowsVersion == new Version(10, 0, 19045) &&
            phi.MinimumOllamaVersion == "0.5.13" && phi.RecommendedRamBytes == 68719476736,
            "Phi catalog qualification contract or resource thresholds changed.");
        var nemotron4B = catalog.Models.Single(model => model.ModelId == "nemotron-3-nano:4b");
        var nemotron30B = catalog.Models.Single(model => model.ModelId == "nemotron-3-nano:30b");
        Check(nemotron4B.LicenseAcceptanceRequired && nemotron4B.LicenseTextUrl?.AbsoluteUri ==
            "https://www.nvidia.com/en-us/agreements/enterprise-software/nvidia-open-model-license/" &&
            nemotron4B.QualificationStatus == AetherModelQualificationStatus.Rejected &&
            !nemotron4B.ApprovedForAutomaticSelection && nemotron4B.ModelId == "nemotron-3-nano:4b" &&
            nemotron4B.Quantization == "Q4_K_M" && nemotron4B.ParameterCountBillions == 3.97m &&
            nemotron4B.ExpectedOllamaDigest == "d94b6dfae26d340fd16599d8cd5301945bb42b6bd7a7c70f2078559562f60cbe" &&
            nemotron4B.ExpectedLayers.Single(layer => layer.MediaType == "application/vnd.ollama.image.model").SizeBytes == 2837586496 &&
            nemotron4B.DownloadSizeBytes == 2837597147 && nemotron4B.AutomaticSelectionPriority > phi.AutomaticSelectionPriority &&
            nemotron30B.QualificationStatus == AetherModelQualificationStatus.Pending &&
            !nemotron30B.ApprovedForAutomaticSelection && nemotron30B.DownloadSource is null &&
            nemotron30B.ExpectedOllamaDigest is null,
            "Nemotron registry metadata, license, priority, or rejected qualification state changed.");
        assertions += RunOwnershipRecoveryTests(nemotron4B);

        var localRoot = AetherLocalAiStoreContract.GetRootPath(@"C:\Users\fixture\AppData\Local");
        Check(localRoot == Path.Combine(@"C:\Users\fixture\AppData\Local", "AETHER", "LocalAI") &&
            AetherModelCatalog.DefaultPath == AetherLocalAiStoreContract.CatalogPath &&
            AetherModelCatalog.ModelStorePath == AetherLocalAiStoreContract.ModelStorePath &&
            AetherLocalAiStoreContract.Host == "127.0.0.1:11434",
            "Setup and AETHER do not share the stable local model-store contract.");

        var eligible = phi with
        {
            Revision = "fixture-revision",
            QualificationStatus = AetherModelQualificationStatus.Qualified,
            QualifiedAt = DateTimeOffset.UnixEpoch,
            QualificationEvidence = new Uri("https://fixture.invalid/qualification"),
            InstallationSizeBytes = 1,
            MinimumRamBytes = 100,
            MinimumAvailableRamBytes = 50,
            MinimumVramBytes = 0,
            MinimumStorageBytes = 100,
            RecommendedRamBytes = 100,
            RequiredModelRamBytes = 1,
            RequiredModelVramBytes = 0,
            RamSafetyReserveBytes = 49,
            VramSafetyReserveBytes = 0,
            StorageSafetyReserveBytes = 0,
            MaximumParallelRequests = 1,
            ApprovedForAutomaticSelection = true,
            SupportedLanguages = ["de"],
            ModelFamily = "Fixture Model",
            ParameterCountBillions = 1,
            AutomaticSelectionPriority = 50,
            FallbackModelId = "aether.deterministic",
            DigestVerificationPassed = true,
            GermanTestPassed = true,
            StructuredOutputTestPassed = true,
            ToolContractTestPassed = true,
            OfflineTestPassed = true,
            ResourceMeasurementPassed = true
        };
        var fixtureCatalog = catalog with { Models = [eligible] };
        var baseHardware = new AetherSetupHardware(new Version(10, 0, 22631), true, "CPU fixture",
            8, 16, 1000, 500, ImmutableArray<AetherSetupGpu>.Empty, 1000);

        using (var foreignOllama = new TcpListener(IPAddress.Loopback, 0))
        {
            foreignOllama.Start();
            var foreignEndpoint = new Uri(
                $"http://127.0.0.1:{((IPEndPoint)foreignOllama.LocalEndpoint).Port}/");
            var previousModels = Environment.GetEnvironmentVariable(
                AetherLocalAiStoreContract.EnvironmentVariable, EnvironmentVariableTarget.User);
            var previousHost = Environment.GetEnvironmentVariable(
                AetherLocalAiStoreContract.HostEnvironmentVariable, EnvironmentVariableTarget.User);
            var operations = new AetherLocalAiSetup.OllamaSetupOperations(foreignEndpoint);
            var status = operations.InspectRuntimeAsync(phi, CancellationToken.None).GetAwaiter().GetResult();
            Check(status.UserOllamaListenerPreserved && !status.ListenerAvailable &&
                operations.ManagedEndpoint != foreignEndpoint && operations.ManagedEndpoint.Host == foreignEndpoint.Host,
                "The occupied user Ollama endpoint was not isolated from the AETHER setup server.");
            Check(!foreignOllama.Pending() &&
                Environment.GetEnvironmentVariable(AetherLocalAiStoreContract.EnvironmentVariable,
                    EnvironmentVariableTarget.User) == previousModels &&
                Environment.GetEnvironmentVariable(AetherLocalAiStoreContract.HostEnvironmentVariable,
                    EnvironmentVariableTarget.User) == previousHost,
                "Setup inspection contacted or reconfigured a foreign Ollama listener.");
        }

        var ready = AetherSetupCapabilityGate.Evaluate(fixtureCatalog, baseHardware);
        Check(ready.Capability == AetherLocalAiCapability.LocalAiReady && ready.CanInstall &&
            ready.ModelId == eligible.ModelId && ready.Label == "Lokale KI bereit",
            "A qualified CPU-only model was not enabled on a matching fixture.");
        Check(AetherSetupCapabilityGate.Evaluate(fixtureCatalog,
            baseHardware with { Is64BitOperatingSystem = false }).Capability == AetherLocalAiCapability.LocalAiUnavailable &&
            AetherSetupCapabilityGate.Evaluate(fixtureCatalog,
                baseHardware with { WindowsVersion = new Version(10, 0, 19044) }).Capability == AetherLocalAiCapability.LocalAiUnavailable,
            "Unsupported architecture or Windows version was not rejected.");
        Check(AetherSetupCapabilityGate.Evaluate(fixtureCatalog,
            baseHardware with { InstalledRamBytes = 99 }).Capability == AetherLocalAiCapability.LocalAiUnavailable &&
            AetherSetupCapabilityGate.Evaluate(fixtureCatalog,
                baseHardware with { AvailableRamBytes = 49 }).Capability == AetherLocalAiCapability.LocalAiUnavailable,
            "Insufficient installed or available RAM was not rejected.");
        Check(AetherSetupCapabilityGate.Evaluate(fixtureCatalog,
            baseHardware with { FreeModelStoreBytes = 99 }).Capability == AetherLocalAiCapability.LocalAiUnavailable,
            "Insufficient model-store space was not rejected.");

        var vramModel = eligible with { MinimumVramBytes = 100 };
        var vramCatalog = fixtureCatalog with { Models = [vramModel] };
        var vramHardware = baseHardware with
        {
            Gpus = [new("GPU fixture", 99, 99, "driver-fixture")]
        };
        Check(AetherSetupCapabilityGate.Evaluate(vramCatalog, vramHardware).Capability ==
            AetherLocalAiCapability.LocalAiUnavailable,
            "Insufficient dedicated VRAM was not rejected.");
        Check(AetherSetupCapabilityGate.Evaluate(vramCatalog, baseHardware with
            { Gpus = [new("NVIDIA fixture", null, null, null)] }).Capability ==
            AetherLocalAiCapability.LocalAiLimited,
            "Missing dedicated VRAM evidence was not reported as limited.");

        var preferredNemotron = eligible with
        {
            ModelId = "nemotron-fixture:4b",
            DisplayName = "Nemotron fixture",
            ModelFamily = "Nemotron 3 Nano",
            ParameterCountBillions = 3.97m,
            AutomaticSelectionPriority = 100,
            FallbackModelId = eligible.ModelId,
            MinimumVramBytes = 100,
            RequiredModelVramBytes = 1,
            VramSafetyReserveBytes = 99,
            RequiredGpuVendor = "NVIDIA"
        };
        var preferredCatalog = fixtureCatalog with { Models = [eligible, preferredNemotron] };
        Check(AetherSetupCapabilityGate.Evaluate(preferredCatalog,
            baseHardware with { Gpus = [new("NVIDIA fixture", 100, 100, "driver-fixture")] }).ModelId ==
                preferredNemotron.ModelId &&
            AetherSetupCapabilityGate.Evaluate(preferredCatalog, baseHardware).ModelId == eligible.ModelId,
            "Nemotron was not prioritized on compatible hardware with Phi retained as the fallback.");

        var measuredGpu = AetherSetupGpuMeasurements.ApplyNvidiaSmi(
            [new("NVIDIA GeForce RTX 4080 SUPER", null, null, "inventory-driver")],
            "NVIDIA GeForce RTX 4080 SUPER, 16376, 11389, 621.24");
        Check(measuredGpu.Single().DedicatedVramBytes == 16376L * 1024 * 1024 &&
            measuredGpu.Single().AvailableVramBytes == 11389L * 1024 * 1024 &&
            measuredGpu.Single().DriverVersion == "621.24",
            "NVIDIA-SMI VRAM measurements were not associated with the matching GPU.");
        try
        {
            AetherSetupGpuMeasurements.ApplyNvidiaSmi(
                [new("NVIDIA GPU", null, null, null)], "NVIDIA GPU, 8, not-a-number, 1");
            throw new InvalidOperationException("Malformed NVIDIA-SMI data was accepted.");
        }
        catch (InvalidDataException)
        {
            assertions++;
        }

        var advanced = eligible with { ModelId = "fixture-advanced:1", Advanced = true };
        var advancedResult = AetherSetupCapabilityGate.Evaluate(
            fixtureCatalog with { Models = [advanced] }, baseHardware);
        Check(advancedResult.Capability == AetherLocalAiCapability.AdvancedLocalAiPossible &&
            advancedResult.Label == "Erweiterte lokale KI möglich",
            "Advanced capability was not returned as the fourth defined status.");
        Check(!AetherSetupCapabilityGate.Evaluate(catalog, baseHardware).CanInstall &&
            AetherSetupCapabilityGate.Evaluate(catalog, baseHardware).Capability == AetherLocalAiCapability.LocalAiUnavailable &&
            AetherSetupCapabilityGate.Evaluate(catalog, baseHardware).Label ==
                "Lokale KI ist auf diesem System nicht verfügbar.",
            "The production catalog did not reject hardware below the qualified Phi contract.");

        var noLicenseOperations = new SetupOperations();
        try
        {
            new AetherLocalAiSetupWorkflow(noLicenseOperations).RunAsync(fixtureCatalog, eligible, baseHardware,
                licenseAccepted: false, allowManagedStoreConfiguration: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("Setup proceeded without explicit model license acceptance.");
        }
        catch (UnauthorizedAccessException)
        {
            Check(noLicenseOperations.Inspections == 0,
                "Ollama was inspected or modified before required license acceptance.");
        }

        var unsupportedOperations = new SetupOperations();
        try
        {
            new AetherLocalAiSetupWorkflow(unsupportedOperations).RunAsync(catalog, phi, baseHardware,
                licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("Setup proceeded below the production Phi hardware contract.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Systemressourcen", StringComparison.Ordinal))
        {
            Check(unsupportedOperations.Inspections == 0 && unsupportedOperations.Installations == 0 &&
                unsupportedOperations.Pulls == 0,
                "Phi triggered runtime installation or model acquisition below its hardware contract.");
        }

        var missingRuntime = new SetupOperations();
        new AetherLocalAiSetupWorkflow(missingRuntime).RunAsync(fixtureCatalog, eligible, baseHardware,
            licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check(missingRuntime.Installations == 1 && missingRuntime.Configurations == 1 &&
            missingRuntime.Pulls == 1 &&
            missingRuntime.Verifications == 1 && missingRuntime.HealthChecks == 1 &&
            missingRuntime.PersistedCatalogs == 1 && missingRuntime.ServerStops == 1,
            "Missing-Ollama bootstrap did not install, verify, health-check, and persist in order.");

        var existingModel = new SetupOperations
        {
            Runtime = new(true, true, true, "fixture", @"C:\fixture\ollama.exe", true, true, true,
                AetherLocalAiStoreContract.ModelStorePath, true, true, true)
        };
        new AetherLocalAiSetupWorkflow(existingModel).RunAsync(fixtureCatalog, eligible, baseHardware,
            licenseAccepted: true, allowManagedStoreConfiguration: false, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check(existingModel.Installations == 0 && existingModel.Pulls == 0 && existingModel.Verifications == 1 &&
            existingModel.ServerStops == 0,
            "An owned model already in the managed store was downloaded again.");

        var foreignModel = new SetupOperations
        {
            Runtime = new(true, true, true, "fixture", @"C:\fixture\ollama.exe", true, true, false,
                @"C:\Users\fixture\.ollama\models", false, true)
        };
        try
        {
            new AetherLocalAiSetupWorkflow(foreignModel).RunAsync(fixtureCatalog, eligible, baseHardware,
                licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("A foreign model was adopted from the existing Ollama store.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Fremdmodell", StringComparison.Ordinal))
        {
            Check(foreignModel.Configurations == 0 && foreignModel.Pulls == 0,
                "A foreign Ollama store was switched or modified while it contained the selected model.");
        }

        var unownedModelInManagedStore = new SetupOperations
        {
            Runtime = new(true, true, true, "fixture", @"C:\fixture\ollama.exe", true, true, true,
                AetherLocalAiStoreContract.ModelStorePath, true, true, false)
        };
        try
        {
            new AetherLocalAiSetupWorkflow(unownedModelInManagedStore).RunAsync(fixtureCatalog, eligible, baseHardware,
                licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("An unowned model in the AETHER store was adopted.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Fremdmodell", StringComparison.Ordinal))
        {
            Check(unownedModelInManagedStore.Configurations == 0 && unownedModelInManagedStore.Pulls == 0,
                "A model without an AETHER ownership record was adopted from the managed store.");
        }

        var storeConfirmation = new SetupOperations
        {
            Runtime = new(true, true, true, "fixture", @"C:\fixture\ollama.exe", true, false, false,
                @"C:\Users\fixture\.ollama\models", false, false)
        };
        try
        {
            new AetherLocalAiSetupWorkflow(storeConfirmation).RunAsync(fixtureCatalog, eligible, baseHardware,
                licenseAccepted: true, allowManagedStoreConfiguration: false, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("The existing Ollama store was changed without confirmation.");
        }
        catch (UnauthorizedAccessException)
        {
            Check(storeConfirmation.Configurations == 0 && storeConfirmation.Pulls == 0,
                "AETHER store reconfiguration proceeded without explicit confirmation.");
        }

        var storeConfiguration = new SetupOperations
        {
            Runtime = new(true, true, true, "fixture", @"C:\fixture\ollama.exe", true, false, false,
                @"C:\Users\fixture\.ollama\models", false, false)
        };
        new AetherLocalAiSetupWorkflow(storeConfiguration).RunAsync(fixtureCatalog, eligible, baseHardware,
            licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check(storeConfiguration.Configurations == 1 && storeConfiguration.Pulls == 1,
            "Explicit AETHER store configuration did not route the model into the managed store.");

        var foreignListenerCoexistence = new SetupOperations
        {
            Runtime = new(true, true, false, "fixture", @"C:\fixture\ollama.exe", true, false, false,
                @"C:\Users\fixture\.ollama\models", false, false, false, true)
        };
        new AetherLocalAiSetupWorkflow(foreignListenerCoexistence).RunAsync(fixtureCatalog, eligible, baseHardware,
            licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check(foreignListenerCoexistence.Configurations == 1 && foreignListenerCoexistence.Pulls == 1 &&
            foreignListenerCoexistence.PersistedCatalogs == 1 && foreignListenerCoexistence.ServerStops == 1,
            "A preserved user Ollama listener blocked or was treated as the AETHER-managed server.");

        var canceledDownload = new SetupOperations { CancelPull = true };
        try
        {
            new AetherLocalAiSetupWorkflow(canceledDownload).RunAsync(fixtureCatalog, eligible, baseHardware,
                licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("Canceled model acquisition was reported as successful.");
        }
        catch (OperationCanceledException)
        {
            Check(canceledDownload.Verifications == 0 && canceledDownload.PersistedCatalogs == 0 &&
                canceledDownload.ServerStops == 1,
                "Canceled model acquisition was verified or persisted, or left the managed server running.");
        }

        var hashFailure = new SetupOperations { DigestOverride = new string('0', 64) };
        try
        {
            new AetherLocalAiSetupWorkflow(hashFailure).RunAsync(fixtureCatalog, eligible, baseHardware,
                licenseAccepted: true, allowManagedStoreConfiguration: true, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("A model with the wrong manifest digest was accepted.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Manifest oder Modelllayer", StringComparison.Ordinal))
        {
            Check(hashFailure.HealthChecks == 0 && hashFailure.PersistedCatalogs == 0 &&
                hashFailure.ServerStops == 1,
                "Digest mismatch reached health check or catalog persistence, or left the managed server running.");
        }

        return assertions;
    }

    private static int RunOwnershipRecoveryTests(AetherProductModel nemotron)
    {
        var assertions = 0;
        var recoveryCandidate = nemotron with { QualificationStatus = AetherModelQualificationStatus.Pending };
        var recoveryCatalog = AetherProductModelCatalog.Current with
        {
            Models = AetherProductModelCatalog.Current.Models
                .Select(model => model.ModelId == recoveryCandidate.ModelId ? recoveryCandidate : model)
                .ToImmutableArray()
        };
        void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(message);
        }

        var noConsent = new SetupOperations();
        try
        {
            new AetherLocalAiStoreOwnershipRecoveryWorkflow(noConsent)
                .RunAsync(recoveryCatalog, recoveryCandidate, false, CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("Ownership recovery proceeded without license acceptance.");
        }
        catch (UnauthorizedAccessException)
        {
            Check(noConsent.OwnershipRecoveries == 0,
                "Ownership recovery reached the store before license acceptance.");
        }

        var recovery = new SetupOperations();
        var result = new AetherLocalAiStoreOwnershipRecoveryWorkflow(recovery)
            .RunAsync(recoveryCatalog, recoveryCandidate, true, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check(recovery.OwnershipRecoveries == 1 && recovery.Pulls == 0 &&
            recovery.Verifications == 0 && recovery.HealthChecks == 0 && recovery.PersistedCatalogs == 0 &&
            result.ManifestDigest == recoveryCandidate.ExpectedOllamaDigest &&
            recoveryCandidate.QualificationStatus == AetherModelQualificationStatus.Pending &&
            !recoveryCandidate.ApprovedForAutomaticSelection &&
            nemotron.QualificationStatus == AetherModelQualificationStatus.Rejected,
            "Ownership recovery downloaded, qualified, or automatically selected Nemotron.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "Aether-ownership-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = CreateRecoveryFixture(tempRoot, recoveryCandidate);
            var recovered = AetherAcceptanceStoreOwnershipRecovery.RecoverAsync(
                recoveryCatalog with { Models = [fixture.Model] },
                fixture.Model, fixture.ProfilePath, fixture.AcceptanceRoot, fixture.StoreRoot,
                fixture.ModelStorePath, licenseAccepted: true, CancellationToken.None).GetAwaiter().GetResult();
            using (var ownership = JsonDocument.Parse(File.ReadAllBytes(fixture.OwnershipPath)))
            {
                var models = ownership.RootElement.GetProperty("models");
                Check(models.GetArrayLength() == 1 &&
                    models[0].GetProperty("modelId").GetString() == recoveryCandidate.ModelId &&
                    models[0].GetProperty("digest").GetString() == fixture.Model.ExpectedOllamaDigest &&
                    recovered.ManifestDigest == fixture.Model.ExpectedOllamaDigest &&
                    recovered.LayerDigests.Length == 3 &&
                    !File.Exists(Path.Combine(fixture.StoreRoot, "model-catalog.json")),
                    "Verified acceptance data were not recovered as ownership only.");
            }

            var tampered = CreateRecoveryFixture(tempRoot + "-tampered", recoveryCandidate);
            var modelLayer = tampered.Model.ExpectedLayers.Single(layer =>
                layer.MediaType == "application/vnd.ollama.image.model");
            var modelPath = Path.Combine(tampered.ModelStorePath, "blobs", "sha256-" + modelLayer.Digest);
            var corrupted = File.ReadAllBytes(modelPath);
            corrupted[0] ^= 0x01;
            File.WriteAllBytes(modelPath, corrupted);
            try
            {
                AetherAcceptanceStoreOwnershipRecovery.RecoverAsync(
                    recoveryCatalog with { Models = [tampered.Model] },
                    tampered.Model, tampered.ProfilePath, tampered.AcceptanceRoot, tampered.StoreRoot,
                    tampered.ModelStorePath, licenseAccepted: true, CancellationToken.None).GetAwaiter().GetResult();
                throw new InvalidOperationException("Ownership recovery accepted a corrupted model blob.");
            }
            catch (InvalidDataException)
            {
                using var ownership = JsonDocument.Parse(File.ReadAllBytes(tampered.OwnershipPath));
                Check(ownership.RootElement.GetProperty("models").GetArrayLength() == 0,
                    "A failed integrity check modified the ownership metadata.");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            if (Directory.Exists(tempRoot + "-tampered")) Directory.Delete(tempRoot + "-tampered", recursive: true);
        }
        return assertions;
    }

    private static RecoveryFixture CreateRecoveryFixture(string root, AetherProductModel template)
    {
        var acceptanceRoot = Path.Combine(root, "AETHER", "LocalAI.Acceptance");
        var storeRoot = Path.Combine(acceptanceRoot, "acceptance-nemotron-fixture");
        var modelStorePath = Path.Combine(storeRoot, "models");
        var blobsPath = Path.Combine(modelStorePath, "blobs");
        Directory.CreateDirectory(blobsPath);
        Directory.CreateDirectory(Path.Combine(modelStorePath, "metadata"));

        static (string Digest, long Size) AddBlob(string directory, byte[] bytes)
        {
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(directory, "sha256-" + digest), bytes);
            return (digest, bytes.LongLength);
        }

        var configBytes = Encoding.UTF8.GetBytes("{}");
        var config = AddBlob(blobsPath, configBytes);
        var modelLayer = AddBlob(blobsPath, Encoding.UTF8.GetBytes("nemotron fixture weights"));
        var licenseLayer = AddBlob(blobsPath,
            Encoding.UTF8.GetBytes("NVIDIA Open Model License Agreement\nNVIDIA Corporation\n"));
        var paramsLayer = AddBlob(blobsPath, Encoding.UTF8.GetBytes("{}"));
        var expectedLayers = ImmutableArray.Create(
            new AetherModelLayer("application/vnd.ollama.image.model", modelLayer.Digest, modelLayer.Size),
            new AetherModelLayer("application/vnd.ollama.image.license", licenseLayer.Digest, licenseLayer.Size),
            new AetherModelLayer("application/vnd.ollama.image.params", paramsLayer.Digest, paramsLayer.Size));
        var configDescriptor = new
        {
            mediaType = "application/vnd.docker.container.image.v1+json",
            digest = "sha256:" + config.Digest,
            size = config.Size
        };
        var childLayers = expectedLayers.Select(layer => new
        {
            mediaType = layer.MediaType,
            digest = "sha256:" + layer.Digest,
            size = layer.SizeBytes
        }).ToArray();
        var childManifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            mediaType = "application/vnd.docker.distribution.manifest.v2+json",
            config = configDescriptor,
            layers = childLayers
        });
        var manifestDigest = Convert.ToHexString(SHA256.HashData(childManifestBytes)).ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(blobsPath, "sha256-" + manifestDigest), childManifestBytes);

        var manifestListBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            mediaType = "application/vnd.ollama.manifest.list.v2+json",
            manifests = new[]
            {
                new
                {
                    mediaType = "application/vnd.docker.distribution.manifest.v2+json",
                    digest = "sha256:" + manifestDigest,
                    runner = "llamacpp",
                    format = "gguf"
                }
            }
        });
        var manifestList = AddBlob(blobsPath, manifestListBytes);
        var wrapperLayers = childLayers.Concat(
        [
            new
            {
                mediaType = "application/vnd.ollama.manifest.list.v2+json",
                digest = "sha256:" + manifestList.Digest,
                size = manifestList.Size
            },
            new
            {
                mediaType = "application/vnd.docker.distribution.manifest.v2+json",
                digest = "sha256:" + manifestDigest,
                size = childManifestBytes.LongLength
            }
        ]).ToArray();
        var wrapperManifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            config = configDescriptor,
            layers = wrapperLayers,
            runner = "llamacpp",
            format = "gguf"
        });
        var registryManifestPath = Path.Combine(modelStorePath, "manifests", "registry.ollama.ai",
            "library", "nemotron-3-nano", "4b");
        Directory.CreateDirectory(Path.GetDirectoryName(registryManifestPath)!);
        File.WriteAllBytes(registryManifestPath, wrapperManifestBytes);
        var tagManifestListPath = Path.Combine(modelStorePath, "manifests-v2", "ollama.com",
            "library", "nemotron-3-nano", "4b");
        Directory.CreateDirectory(Path.GetDirectoryName(tagManifestListPath)!);
        File.WriteAllBytes(tagManifestListPath, manifestListBytes);
        File.WriteAllText(Path.Combine(modelStorePath, "metadata", "sha256-" + modelLayer.Digest + ".json"),
            "{\"kv\":{\"general.architecture\":\"nemotron_h\",\"general.parameter_count\":3973556832}}");

        var profilePath = Path.Combine(storeRoot, "aether-localai-acceptance.json");
        File.WriteAllText(profilePath, JsonSerializer.Serialize(new
        {
            schema = 1,
            storeRoot,
            ollamaHost = "127.0.0.1:11435"
        }));
        var ownershipPath = Path.Combine(storeRoot, "aether-store.json");
        File.WriteAllText(ownershipPath, JsonSerializer.Serialize(new
        {
            schema = 1,
            modelStorePath,
            models = Array.Empty<object>(),
            previousModelsEnvironment = (string?)null,
            previousHostEnvironment = (string?)null
        }));

        var model = template with
        {
            ExpectedOllamaDigest = manifestDigest,
            ExpectedConfigDigest = config.Digest,
            ExpectedConfigSizeBytes = config.Size,
            ExpectedLayers = expectedLayers,
            DownloadSizeBytes = expectedLayers.Sum(layer => layer.SizeBytes) + config.Size
        };
        return new(model, profilePath, acceptanceRoot, storeRoot, modelStorePath, ownershipPath);
    }

    private sealed record RecoveryFixture(
        AetherProductModel Model, string ProfilePath, string AcceptanceRoot, string StoreRoot,
        string ModelStorePath, string OwnershipPath);
}
