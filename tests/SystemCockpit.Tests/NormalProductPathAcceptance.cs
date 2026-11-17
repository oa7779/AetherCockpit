using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AetherOperations.Intelligence;
using SystemCockpit.Controls;
using SystemCockpit.Services;

internal static class NormalProductPathAcceptance
{
    private const string PhiId = "phi4-mini:3.8b";

    public static int Run()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var operation = RunAsync();
            if (!operation.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = operation.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                    TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            return operation.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static async Task<int> RunAsync()
    {
        if (AetherLocalAiStoreContract.AcceptanceProfilePath is not null ||
            AetherLocalAiStoreContract.IsAcceptanceMode)
            throw new InvalidOperationException("Der Normalpfadtest darf kein Acceptance-Profil verwenden.");

        var root = AetherLocalAiStoreContract.GetRootPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var modelStore = Path.Combine(root, "models");
        var catalogPath = Path.Combine(root, "model-catalog.json");
        if (!PathEquals(root, AetherLocalAiStoreContract.RootPath) ||
            !PathEquals(modelStore, AetherLocalAiStoreContract.ModelStorePath) ||
            !File.Exists(catalogPath))
            throw new InvalidDataException("Der normale AETHER-Modellstore oder Produktkatalog fehlt.");

        var product = AetherProductModelCatalog.Current.Models.Single(model => model.ModelId == PhiId);
        if (product.QualificationStatus != AetherModelQualificationStatus.Qualified ||
            !product.ApprovedForAutomaticSelection || product.ExpectedOllamaDigest is null)
            throw new InvalidDataException("Phi-4 Mini ist im Produktkatalog nicht für automatische Auswahl freigegeben.");

        var catalog = AetherModelCatalog.Read(catalogPath);
        var entry = catalog.Models.SingleOrDefault(model => model.Model.Id == PhiId);
        if (entry is null || entry.Provider != FabricProviderKind.Ollama ||
            entry.Endpoint != new Uri("http://" + AetherLocalAiStoreContract.Host + "/") ||
            !PathEquals(entry.ModelStorePath, modelStore) ||
            entry.Model.Digest != product.ExpectedOllamaDigest ||
            !entry.Model.LicenseAccepted || !entry.HardwareQualified)
            throw new InvalidDataException("Der normale Laufzeitkatalog stimmt nicht mit dem qualifizierten Phi-Produktmodell überein.");

        var window = new SystemCockpit.MainWindow(new CockpitSettings());
        var view = window.FindName("IntelligenceFabric") as IntelligenceFabricView
            ?? throw new InvalidOperationException("Die AETHER-Intelligence-Ansicht konnte nicht initialisiert werden.");
        var runtimeStopped = false;
        try
        {
            // Designated realistic product cases: together they must prove Windows identity,
            // CPU or GPU identity, RAM or VRAM measurement, storage availability, and storage
            // measurement claims within one accepted product run.
            var productCases = new (string Question, string[][] RequiredCategoryGroups,
                bool RequiresStorageAvailability, bool RequiresStorageMeasurement)[]
            {
                ("Fasse den aktuellen AETHER-Systemstatus zusammen.",
                    [["WindowsIdentity"], ["RamMeasurement", "VramMeasurement"]], false, false),
                ("Fasse den aktuellen AETHER-Systemstatus zusammen und nenne dabei die Namen von CPU und Grafikkarte.",
                    [["CpuIdentity", "GpuIdentity"]], false, false),
                ("Fasse den aktuellen AETHER-Systemstatus zusammen und nenne dabei die physischen Datenträger und ihre Gesamtkapazität.",
                    [], true, true)
            };
            var button = (Button)view.FindName("InvestigateButton");
            var passSummaries = new List<string>();
            string? runtimeHost = null;
            string? endpoint = null;
            string? lastDigest = null;
            int? runtimePid = null;
            foreach (var productCase in productCases)
            {
            view.Question.Text = productCase.Question;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            await view.PendingOperation;
            await view.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);

            var runText = ((TextBox)view.FindName("RunDiagnosticText")).Text;
            var runIdLine = runText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("RunId: ", StringComparison.Ordinal));
            if (runIdLine is null || !Guid.TryParse(runIdLine["RunId: ".Length..], out var runId))
                throw new InvalidDataException("Der reale Produktlauf hat keine auswertbare RunId protokolliert.");

            var diagnosticsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AETHER", "IntelligenceFabric", "diagnostics", $"run-{runId:N}.jsonl");
            var finalLine = File.ReadLines(diagnosticsPath).LastOrDefault()
                ?? throw new InvalidDataException("Der reale Produktlauf hat keine Abschlussdiagnose geschrieben.");
            using var diagnostics = JsonDocument.Parse(finalLine);
            var data = diagnostics.RootElement;
            var actualDigest = ReadString(data, "ActualDigest");
            var expectedDigest = ReadString(data, "ExpectedDigest");
            endpoint = ReadString(data, "OllamaEndpoint");
            runtimeHost = ReadString(data, "RuntimeHost");
            runtimePid = ReadInt32(data, "RuntimeProcessId");
            lastDigest = actualDigest;
            var provider = ReadString(data, "FinalProvider");
            var realModel = ReadBoolean(data, "RealModel");
            var validatorSucceeded = ReadBoolean(data, "ValidatorSucceeded");

            if (ReadBoolean(data, "ProfileFound") ||
                ReadBoolean(data, "AcceptanceMode") ||
                !PathEquals(ReadString(data, "ExecutablePath"), Environment.ProcessPath) ||
                !ReadBoolean(data, "CatalogFound") ||
                !PathEquals(ReadString(data, "CatalogPath"), catalogPath) ||
                ReadString(data, "CatalogSource") != "ProductCatalog" ||
                !PathEquals(ReadString(data, "StoreRoot"), root) ||
                !PathEquals(ReadString(data, "ModelStore"), modelStore) ||
                ReadString(data, "CatalogModelId") != PhiId ||
                ReadString(data, "SelectedModelId") != PhiId ||
                ReadString(data, "RequestedModelRole") != FabricModelRole.FastAssistant.ToString() ||
                ReadString(data, "InferenceTask") != "assistant-report" ||
                ReadString(data, "PromptVersion") != AetherPromptRegistry.Version ||
                ReadString(data, "AnswerType") != nameof(FabricFastAssistantAnswer) ||
                ReadInt32(data, "MaximumOutputTokens") != 1536 ||
                ReadInt32(data, "InitialInputTokens") is null or <= 0 or >= 2560 ||
                ReadInt32(data, "RepairInputTokens") is { } repairInputTokens && repairInputTokens >= 2560 ||
                !ReadBoolean(data, "ValidatorStarted") ||
                ReadString(data, "QualificationStatus") != AetherModelQualificationStatus.Qualified.ToString() ||
                !ReadBoolean(data, "ApprovedForAutomaticSelection") ||
                !ReadBoolean(data, "AvailableRamAccepted") ||
                !ReadBoolean(data, "AvailableVramAccepted") ||
                ReadString(data, "IntendedProvider") != FabricProviderKind.Ollama.ToString() ||
                !ReadBoolean(data, "RuntimeStartedByAether") ||
                !ReadBoolean(data, "RuntimeStoreVerified") ||
                !PathEquals(ReadString(data, "RuntimeModelStorePath"), modelStore) ||
                !ReadBoolean(data, "EnsureReadySucceeded") ||
                !(ReadBoolean(data, "LoadSucceeded") || ReadBoolean(data, "Resident")) ||
                !ReadBoolean(data, "InferenceSucceeded") ||
                ReadString(data, "InitialDoneReason") != "stop" ||
                !ReadBoolean(data.GetProperty("InitialResponseShape"), "CompleteJson") ||
                ReadBoolean(data, "RepairRetryStarted") &&
                    (ReadString(data, "RepairDoneReason") != "stop" ||
                     !ReadBoolean(data.GetProperty("RepairResponseShape"), "CompleteJson")) ||
                !validatorSucceeded ||
                !realModel ||
                actualDigest is null || expectedDigest is null ||
                !actualDigest.Equals(expectedDigest, StringComparison.OrdinalIgnoreCase) ||
                !expectedDigest.Equals(product.ExpectedOllamaDigest, StringComparison.OrdinalIgnoreCase) ||
                ReadString(data, "FallbackReason") is not null ||
                provider != "aether.ollama.local")
                throw new InvalidDataException(
                    $"Der normale Phi-Produktpfad hat das Laufzeit-, Hardware-, Digest-, Inferenz- oder Validator-Gate nicht bestanden. " +
                    $"Frage={productCase.Question}; RunId={runId}; Initial={ReadString(data, "InitialValidationReason") ?? "keiner"}; " +
                    $"Final={ReadString(data, "ExceptionDetail") ?? "keiner"}; " +
                    $"Tokens={ReadInt32(data, "InitialOutputTokens")?.ToString() ?? "?"}/" +
                    $"{ReadInt32(data, "RepairOutputTokens")?.ToString() ?? "?"}");

            var status = ((TextBlock)view.FindName("Status")).Text;
            if (!status.Contains("Modellantwort formal geprüft", StringComparison.Ordinal) ||
                ((TextBox)view.FindName("ModelAnswerText")).Text.Contains("Regelbasierte Zusammenfassung", StringComparison.Ordinal))
                throw new InvalidDataException("Die normale Untersuchung zeigte keine validierte reale FastAssistant-Antwort.");
            var answerDetails = ((TextBox)view.FindName("ModelAnswerText")).Text.Split('\n');
            var answerSummary = answerDetails.Length > 1 ? answerDetails[1] : "";
            var answerText = ((TextBox)view.FindName("ModelAnswerText")).Text;
            if (Regex.IsMatch(answerText,
                    @"(?i)\b(?:Datenträger|Festplatte|physical\s+drive|disk|drive|SSD|HDD|NVMe)\s*(?:Nummer|Nr\.?|#)?\s*\d{1,3}\b",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
                Regex.IsMatch(answerText, @"(?i)\b(?:physicalDrive|volume)\.\d+\b",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidDataException("Der normale Phi-Produktpfad zeigte eine interne Storageposition als Datenträgernummer.");
            if (string.IsNullOrWhiteSpace(answerSummary) ||
                !((TextBlock)view.FindName("SummaryText")).Text.Contains(answerSummary, StringComparison.Ordinal) ||
                ((ItemsControl)view.FindName("FindingsItems")).Items.Count == 0 ||
                ((Border)view.FindName("FindingsPanel")).Visibility != Visibility.Visible)
                throw new InvalidDataException("Die validierte FastAssistant-Zusammenfassung oder die Befunde sind nicht sichtbar.");

            var categories = data.TryGetProperty("AcceptedClaimCategories", out var categoriesElement) &&
                categoriesElement.ValueKind == JsonValueKind.Array
                    ? categoriesElement.EnumerateArray().Select(value => value.GetString())
                        .Where(value => value is not null).Select(value => value!).ToArray()
                    : [];
            foreach (var group in productCase.RequiredCategoryGroups)
                if (!group.Any(categories.Contains))
                    throw new InvalidDataException(
                        $"Der Produktfall deckte keine der geforderten Claimkategorien ab. Frage={productCase.Question}; " +
                        $"RunId={runId}; gefordert={string.Join("/", group)}; belegt={string.Join(",", categories)}.");
            var storageAvailability = ReadInt32(data, "AcceptedStorageAvailabilityClaims") ?? 0;
            var storageMeasurement = ReadInt32(data, "AcceptedStorageMeasurementClaims") ?? 0;
            if (productCase.RequiresStorageAvailability && storageAvailability < 1 ||
                productCase.RequiresStorageMeasurement && storageMeasurement < 1)
                throw new InvalidDataException(
                    $"Der Storage-Produktfall belegte keine geforderten Storage-Claims. Frage={productCase.Question}; " +
                    $"RunId={runId}; Availability={storageAvailability}; Measurement={storageMeasurement}.");

            passSummaries.Add(
                $"RunId={runId}; InputTokens={ReadInt32(data, "InitialInputTokens")}; " +
                $"OutputTokens={ReadInt32(data, "InitialOutputTokens")}; " +
                $"RepairInputTokens={ReadInt32(data, "RepairInputTokens")}; RepairOutputTokens={ReadInt32(data, "RepairOutputTokens")}; " +
                $"InitialDone={ReadString(data, "InitialDoneReason")}; RepairDone={ReadString(data, "RepairDoneReason")}; " +
                $"ClaimCategories={string.Join(",", categories)}; " +
                $"StorageAvailability={storageAvailability}; StorageMeasurement={storageMeasurement}");
            }

            var runtimePort = new Uri(runtimeHost is null ? endpoint! : "http://" + runtimeHost + "/").Port;
            await view.ShutdownAsync();
            runtimeStopped = true;
            if (runtimePid is { } pid && LocalOllamaRuntime.IsListenerOwnedBy(runtimePort, pid))
                throw new InvalidOperationException("Die von AETHER gestartete Ollama-Runtime wurde nicht beendet.");

            Console.WriteLine(
                $"PASS normaler Phi-Produktpfad: Provider=Ollama; Runtime={runtimeHost}; " +
                $"Digest={lastDigest}; Modell geladen; RealModel=True; Validator=True; " +
                $"Fallback=nein; Ollama-Port freigegeben. Produktfälle:");
            foreach (var summary in passSummaries)
                Console.WriteLine("  " + summary);
            return 0;
        }
        finally
        {
            if (!runtimeStopped)
                await view.ShutdownAsync();
            window.Close();
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? ReadInt32(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result) ? result : null;

    private static bool PathEquals(string? left, string? right) => left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
