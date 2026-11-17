using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

internal sealed record GuardianBudget(int Days = 30, int Files = 256, long Bytes = 128L * 1024 * 1024)
{
    internal void Validate()
    {
        if (Days is < 1 or > 365 || Files is < 8 or > 2048 || Bytes is < 1048576 or > 1073741824)
            throw new ArgumentOutOfRangeException(nameof(GuardianBudget), "Ungültiges Guardian-Speicherbudget.");
    }
}
internal sealed class GuardianVault(string root, GuardianBudget? budget = null)
{
    internal static string BudgetGroup(RecoveryKind kind) => kind switch
    {
        RecoveryKind.Transaction => "Journal",
        RecoveryKind.RecoveryPoint => "Recovery Points",
        RecoveryKind.ConfigurationBackup => "Konfigurationssicherungen",
        RecoveryKind.Maintenance => "Maintenance Sessions",
        RecoveryKind.RescuePackage => "Rescue Packages",
        _ => "Vaultobjekte"
    };
    internal static GuardianBudget GroupBudget(string group) => group switch
    {
        "Journal" => new(90, 128, 32L * 1024 * 1024),
        "Recovery Points" => new(90, 32, 32L * 1024 * 1024),
        "Konfigurationssicherungen" => new(90, 32, 8L * 1024 * 1024),
        "Maintenance Sessions" => new(90, 32, 16L * 1024 * 1024),
        "Rescue Packages" => new(30, 16, 16L * 1024 * 1024),
        "Vaultobjekte" => new(30, 128, 32L * 1024 * 1024),
        _ => throw new ArgumentException("Unbekannte Guardian-Budgetgruppe.")
    };
    internal static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(CockpitSettings.PathName)!, "Guardian");
    internal string Root => root;
    internal GuardianBudget Budget { get; } = budget ?? new();
    internal const int MaximumObjectBytes = 16 * 1024 * 1024;
    internal List<string> Issues { get; } = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal static GuardianArtifact Artifact(RecoveryKind kind, string name, GuardianContext context, string summary,
        IReadOnlyDictionary<string, string>? fields = null, IntelligenceSnapshot? snapshot = null,
        IReadOnlyList<Guid>? dependencies = null, bool pinned = false) =>
        new(1, Guid.NewGuid(), kind, name, DateTimeOffset.UtcNow, context,
            IntelligenceModel.Metadata("AETHER Guardian / lokale Evidenz", DateTimeOffset.UtcNow),
            dependencies ?? [], pinned, summary, fields ?? new Dictionary<string, string>(), snapshot);
    internal static GuardianArtifact Wrap(GuardianTransaction tx) =>
        new(1, tx.Id, RecoveryKind.Transaction, tx.Title, tx.Created, tx.Context, tx.Metadata,
            tx.RollbackOf is { } id ? [id] : [], tx.InFlight, tx.VerificationResult,
            new Dictionary<string, string>(), Transaction: tx);
    internal Task SaveTransactionAsync(GuardianTransaction tx, CancellationToken token) => SaveAsync(Wrap(tx), token);
    internal Task CreateTransactionAsync(GuardianTransaction tx, CancellationToken token) => SaveAsync(Wrap(tx), token, true);
    internal async Task SaveAsync(GuardianArtifact artifact, CancellationToken token, bool createOnly = false)
    {
        artifact.Validate(); Budget.Validate();
        var bytes = EvidenceFile.Encode("guardian", artifact);
        if (bytes.Length > MaximumObjectBytes || bytes.Length > Budget.Bytes) throw new IOException("Guardian-Einzelobjekt überschreitet das Budget.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            PrepareRoot();
            await using var lease = Lease();
            var existing = await ReadAllAsync(token).ConfigureAwait(false);
            var previous = existing.SingleOrDefault(a => a.Id == artifact.Id);
            if (createOnly && previous is not null) throw new InvalidOperationException("Transaktionsplan wurde bereits verwendet.");
            if (createOnly && artifact.Transaction?.RollbackOf is { } original && existing.Any(a =>
                a.Transaction?.RollbackOf == original && a.Transaction.State == ChangeState.RolledBack))
                throw new InvalidOperationException("Für diese Transaktion wurde bereits ein bestätigter Rückweg ausgeführt.");
            if (previous?.Kind != artifact.Kind && previous is not null) throw new InvalidOperationException("Vault-Typ darf nicht geändert werden.");
            if (previous?.Transaction is { } old && artifact.Transaction is { } next &&
                (next.Journal.Count <= old.Journal.Count || !old.Journal.SequenceEqual(next.Journal.Take(old.Journal.Count)) ||
                old.Context != next.Context || System.Text.Json.JsonSerializer.Serialize(old.Plan, IntelligenceModel.Json) !=
                System.Text.Json.JsonSerializer.Serialize(next.Plan, IntelligenceModel.Json)))
                throw new InvalidOperationException("Journal darf nur unverändert erweitert werden.");
            if (artifact.Dependencies.Any(id => existing.All(a => a.Id != id)))
                throw new IOException("Recovery-Abhängigkeit fehlt.");
            Prune(existing.Where(a => a.Id != artifact.Id).ToArray(), artifact, bytes.Length);
            await EvidenceFile.WriteAsync(FileName(artifact.Id), bytes, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    internal async Task<IReadOnlyList<GuardianArtifact>> LoadAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            PrepareRoot();
            await using var lease = Lease();
            return await ReadAllAsync(token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    internal async Task DeleteAsync(Guid id, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            PrepareRoot();
            await using var lease = Lease();
            var objects = await ReadAllAsync(token).ConfigureAwait(false);
            if (objects.Any(a => a.Id == id && (a.Pinned || a.Transaction?.InFlight == true)) ||
                objects.Any(a => a.Id != id && a.Dependencies.Contains(id)))
                throw new InvalidOperationException("Aktiver Vorgang oder Recovery-Abhängigkeit darf nicht gelöscht werden.");
            File.Delete(FileName(id));
        }
        finally { _gate.Release(); }
    }
    private string FileName(Guid id) => Path.Combine(root, id.ToString("N") + ".guardian");
    private FileStream Lease() => new(Path.Combine(root, "vault.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private void PrepareRoot()
    {
        Budget.Validate(); EvidenceFile.RequireLocalDirectory(root); Directory.CreateDirectory(root);
        for (var directory = new DirectoryInfo(Path.GetFullPath(root)); directory is not null; directory = directory.Parent)
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Guardian-Speicher darf nicht über eine Dateiumleitung erreichbar sein.");
    }
    private async Task<IReadOnlyList<GuardianArtifact>> ReadAllAsync(CancellationToken token)
    {
        var result = new List<GuardianArtifact>();
        foreach (var file in Directory.EnumerateFiles(root, "*.guardian").Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) continue;
            var info = new FileInfo(file);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Guardian-Dateiumleitung nicht zugelassen.");
            try
            {
                if (info.Length > MaximumObjectBytes) throw new JsonException("Recovery-Datei zu groß.");
                var artifact = EvidenceFile.Decode<GuardianArtifact>("guardian", await File.ReadAllTextAsync(file, token).ConfigureAwait(false));
                artifact.Validate();
                if (artifact.Id != id) throw new JsonException("Recovery-Dateiname und Identität stimmen nicht überein.");
                result.Add(artifact);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                Trace.TraceError("Guardian-Vault: {0}: {1}", file, ex);
                Issues.Add(Path.GetFileName(file) + ": " + ex.Message);
                if (Issues.Count > 64) Issues.RemoveAt(0);
                File.Move(file, file + ".corrupt-" + Guid.NewGuid().ToString("N"));
            }
        }
        return result;
    }
    private void Prune(IReadOnlyList<GuardianArtifact> existing, GuardianArtifact incoming, int bytes)
    {
        var group = BudgetGroup(incoming.Kind);
        var groupBudget = GroupBudget(group);
        var sameGroup = existing.Where(a => BudgetGroup(a.Kind) == group).ToArray();
        var groupCount = sameGroup.Length + 1;
        long groupBytes = sameGroup.Sum(a => new FileInfo(FileName(a.Id)).Length) + bytes;
        var protectedIds = existing.Where(a => a.Pinned || a.Transaction?.InFlight == true).Select(a => a.Id).ToHashSet();
        protectedIds.UnionWith(incoming.Dependencies);
        foreach (var a in existing) protectedIds.UnionWith(a.Dependencies);
        var all = new DirectoryInfo(root).EnumerateFiles().Where(f => f.Name != "vault.lock").ToArray();
        if (all.Any(f => f.Attributes.HasFlag(FileAttributes.ReparsePoint))) throw new IOException("Vault enthält eine Dateiumleitung.");
        var replacement = all.FirstOrDefault(f => f.FullName == FileName(incoming.Id));
        long total = all.Sum(f => f.Length) - (replacement?.Length ?? 0) + bytes;
        var count = all.Length - (replacement is null ? 0 : 1) + 1;
        var deletions = new List<Guid>();
        foreach (var candidate in sameGroup.OrderBy(a => a.Created))
        {
            if (protectedIds.Contains(candidate.Id)) continue;
            if (total <= Budget.Bytes && count <= Budget.Files && groupBytes <= groupBudget.Bytes && groupCount <= groupBudget.Files &&
                DateTimeOffset.UtcNow - candidate.Created <= TimeSpan.FromDays(Math.Min(Budget.Days, groupBudget.Days))) continue;
            var removedBytes = new FileInfo(FileName(candidate.Id)).Length;
            total -= removedBytes; groupBytes -= removedBytes; count--; groupCount--; deletions.Add(candidate.Id);
        }
        if (total > Budget.Bytes || count > Budget.Files || groupBytes > groupBudget.Bytes || groupCount > groupBudget.Files)
            throw new IOException($"Guardian-Budget ({group} oder Gesamtbudget) erschöpft; aktive Abhängigkeiten oder quarantänisierte Dateien erfordern eine bewusste Bereinigung. Andere Budgetgruppen werden nicht verdrängt.");
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        if (!drive.IsReady || drive.AvailableFreeSpace < bytes * 2L + 1048576)
            throw new IOException("Nicht genügend nachgewiesener freier Speicher für atomare Guardian-Sicherung.");
        foreach (var id in deletions) File.Delete(FileName(id));
    }
    internal static IReadOnlyList<RecoveryReadiness> Readiness(IReadOnlyList<GuardianArtifact> objects, GuardianContext context)
    {
        var matching = objects.Where(a => a.Context == context).ToArray();
        var ids = objects.Select(a => a.Id).ToHashSet();
        var point = matching.Where(a => a.Kind == RecoveryKind.RecoveryPoint).MaxBy(a => a.Created);
        var manifest = matching.Where(a => a.Kind == RecoveryKind.SoftwareManifest).MaxBy(a => a.Created);
        var result = new List<RecoveryReadiness>
        {
            new("Systemzustand", point is null ? ReadinessKind.NotProtected : point.Dependencies.Any(d => !ids.Contains(d)) ?
                ReadinessKind.NotAssessable : ReadinessKind.DocumentedOnly,
                point is null ? "Kein gültiger passender Recovery Point." : $"Geprüfter Vault-Bestand vom {point.Created:O}.",
                "Zustandsdokumentation, kein Image und kein Dateibackup; Aktualität vor jeder Nutzung prüfen."),
            new("Software", manifest is null ? ReadinessKind.NotProtected : ReadinessKind.ExternalSourceRequired,
                manifest is null ? "Kein Softwaremanifest." : $"{manifest.Software?.Count ?? 0} dokumentierte Einträge.",
                "Installationsmedien, proprietäre Quellen und Lizenzen sind nicht gesichert."),
            new("Konfigurationen", matching.Any(a => a.Kind == RecoveryKind.ConfigurationBackup) ? ReadinessKind.DocumentedOnly : ReadinessKind.NotProtected,
                "Gespeicherte konkrete Konfigurationen; wiederherstellbaren Teilumfang in den Fachkategorien prüfen.", "Kein allgemeines Windows- oder AETHER-Einstellungsbackup."),
            new("Persönliche Dateien", ReadinessKind.NotProtected, "Guardian erfasst keine persönlichen Dateien.", "Separates echtes Dateibackup erforderlich."),
            new("Unterbrochene Vorgänge", matching.Any(a => a.Transaction?.InFlight == true || a.Kind == RecoveryKind.Maintenance && a.Pinned) ?
                ReadinessKind.NotAssessable : ReadinessKind.DocumentedOnly,
                "Gespeicherte offene Vorgänge müssen erneut gegen den tatsächlichen Zustand geprüft werden.", "Keine automatische Fortsetzung oder Wiederholung.")
        };
        var config = matching.Where(a => a.Kind == RecoveryKind.ConfigurationBackup && a.Fields.GetValueOrDefault("scope") == "settings,rules,preferences,views")
            .MaxBy(a => a.Created);
        config?.Validate();
        foreach (var (area, scope, limit) in new[]
        {
            ("AETHER-Einstellungen", "Anzeige-/Schwellenwerte", "Keine Tokens, Managerverbindungen, Pfade, Verknüpfungen oder persönliche Dateien"),
            ("Watch-Regeln", "Explizite nicht destruktive Watch-Regeln", "Keine Rekonstruktion unbeobachteter Zustände; Zielidentitäten und Aktualität neu prüfen"),
            ("System-Intelligence-Einstellungen", "Persistenz, Retention und Inventurpräferenz", "Keine Wiederherstellung bereits gelöschter Historie"),
            ("Gespeicherte Ansichten", "Vorhandene Befehlspaletten-Favoriten und Verlauf", "Keine davon unabhängigen Ansichten oder freien Befehle gesichert")
        })
            result.Add(new(area, config is null ? ReadinessKind.NotProtected : ReadinessKind.PartiallyRecoverable,
                config is null ? "Keine kontextgebundene Konfigurationssicherung." : $"{scope}, gesichert {config.Created:O}. Rückweg mit eigenem Change Plan.",
                limit + "; gleicher Rechner-/Benutzerkontext erforderlich."));
        foreach (var (area, kind) in new[] { ("Baselines", IntelligenceKind.Snapshot), ("Snapshots", IntelligenceKind.Snapshot),
            ("Diagnoseaufnahmen", IntelligenceKind.Recording), ("Treiberbestand", IntelligenceKind.Driver),
            ("Tasks", IntelligenceKind.Task), ("Autostarts", IntelligenceKind.Startup), ("Dienstkonfigurationen", IntelligenceKind.Service) })
        {
            var count = matching.Where(a => a.Snapshot is not null).SelectMany(a => a.Snapshot!.Entities).Count(e => e.Kind == kind);
            result.Add(new(area, count > 0 ? ReadinessKind.DocumentedOnly : ReadinessKind.NotProtected,
                $"{count} dokumentierte Entitäten im Vault; vorhandene Originalquellen separat ausweisen.",
                "Kein Vollbackup. Aktivierungszustände nur mit passender Guardian-Transaktion rücksetzbar; Medien, Binärdateien, vollständige Konfiguration und externe Geheimnisse nicht gesichert."));
        }
        result.Add(new("Guardian-Transaktionen", matching.Any(a => a.Transaction is not null) ? ReadinessKind.DocumentedOnly : ReadinessKind.NotProtected,
            $"{matching.Count(a => a.Transaction is not null)} geprüfte lokale Transaktionen.",
            "Rückweg nur gemäß Capability, verfügbarer Zielidentität, tatsächlichem Zustand und Abhängigkeiten."));
        result.Add(new("Rescue Packages", matching.Any(a => a.Kind == RecoveryKind.RescuePackage) ? ReadinessKind.DocumentedOnly : ReadinessKind.NotProtected,
            $"{matching.Count(a => a.Kind == RecoveryKind.RescuePackage)} Exportnachweise; externe Datei bei Verwendung erneut prüfen.",
            "DPAPI-Privatpakete benötigen passenden Windows-Benutzer-/Hostkontext; Supportpakete enthalten keine wiederherstellbaren Rohdaten."));
        result.Add(new("Recovery Points", point is null ? ReadinessKind.NotProtected : ReadinessKind.DocumentedOnly,
            point is null ? "Kein passender Recovery Point." : $"Zustandsdokumentation {point.Created:O}.",
            "Kein Windows-Wiederherstellungspunkt, Image, persönliches Dateibackup oder vollständige Neuinstallation."));
        return result;
    }
}
