using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemCockpit.Services;

internal sealed record GuardianConfigurationData(IReadOnlyDictionary<string, JsonElement> Settings,
    IReadOnlyList<IntelligenceRule> Rules, IntelligencePreferences Intelligence, TaskCenterPreferences Views);

internal static class GuardianConfiguration
{
    private static readonly string[] AllowedSettings =
    [
        nameof(CockpitSettings.CpuWarningC), nameof(CockpitSettings.GpuWarningC), nameof(CockpitSettings.StorageWarningC),
        nameof(CockpitSettings.FreeSpaceWarningPercent), nameof(CockpitSettings.VoltageTolerancePercent),
        nameof(CockpitSettings.CmosMinimumV), nameof(CockpitSettings.RecordIntervalSeconds),
        nameof(CockpitSettings.Skin), nameof(CockpitSettings.Language), nameof(CockpitSettings.DisplayMode),
        nameof(CockpitSettings.AppOpacity), nameof(CockpitSettings.SidebarOpacity), nameof(CockpitSettings.GadgetOpacity)
    ];
    internal static GuardianConfigurationData Capture(CockpitSettings settings, IReadOnlyList<IntelligenceRule> rules,
        IntelligencePreferences preferences, TaskCenterPreferences views)
    {
        settings.Validate();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        var data = new GuardianConfigurationData(AllowedSettings.ToDictionary(k => k, k => json.RootElement.GetProperty(k).Clone()),
            rules.ToArray(), preferences, views);
        Validate(data);
        return data;
    }
    internal static void Validate(GuardianConfigurationData data)
    {
        if (data.Settings is null || !data.Settings.Keys.Order(StringComparer.Ordinal).SequenceEqual(AllowedSettings.Order(StringComparer.Ordinal)) ||
            data.Rules is null || data.Rules.Count > 32 || data.Rules.Any(r => r is null) || data.Rules.Select(r => r.Id).Distinct().Count() != data.Rules.Count ||
            data.Intelligence is null || data.Views is null || data.Views.Version != 1 || data.Views.Favorites is null ||
            data.Views.Recent is null || data.Views.Favorites.Count > 64 || data.Views.Recent.Count > 12 ||
            data.Views.Favorites.Distinct(StringComparer.Ordinal).Count() != data.Views.Favorites.Count ||
            data.Views.Recent.Distinct(StringComparer.Ordinal).Count() != data.Views.Recent.Count ||
            data.Views.Favorites.Concat(data.Views.Recent).Any(t => !TaskCatalog.All.Any(c => c.Title == t)))
            throw new JsonException("Konfigurationssicherung außerhalb des erlaubten Umfangs.");
        data.Intelligence.Validate();
        foreach (var rule in data.Rules) rule.Validate();
        Merge(new CockpitSettings(), data);
        if (Serialize(data).Length > 30000) throw new JsonException("Konfigurationssicherung zu groß für den begrenzten Change Plan.");
    }
    internal static string Serialize(GuardianConfigurationData data) => JsonSerializer.Serialize(data, IntelligenceModel.Json);
    internal static GuardianConfigurationData Decode(string json)
    {
        var data = JsonSerializer.Deserialize<GuardianConfigurationData>(json, new JsonSerializerOptions(IntelligenceModel.Json)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new JsonException("Konfigurationsinhalt fehlt.");
        Validate(data); return data;
    }
    internal static CockpitSettings Merge(CockpitSettings current, GuardianConfigurationData data)
    {
        var node = JsonSerializer.SerializeToNode(current)!.AsObject();
        foreach (var key in AllowedSettings)
            node[key] = JsonNode.Parse(data.Settings[key].GetRawText());
        var result = node.Deserialize<CockpitSettings>() ?? throw new JsonException("Einstellungen fehlen.");
        result.Validate(); return result;
    }
    internal static GuardianArtifact Backup(GuardianConfigurationData data, GuardianContext context)
    {
        Validate(data);
        return GuardianVault.Artifact(RecoveryKind.ConfigurationBackup, "AETHER-Konfiguration sichern", context,
            "Wiederherstellbar: Anzeige-/Schwellenwerte, Watch-Regeln, System-Intelligence-Präferenzen und Befehlspaletten-Favoriten/Verlauf. " +
            "Nicht enthalten: Tokens, Schlüssel, Manager-Verbindung, freie Befehle, Verknüpfungen, persönliche Dateien, Installationsmedien und externe Pfade.",
            new Dictionary<string, string> { ["payload"] = Serialize(data), ["scope"] = "settings,rules,preferences,views" });
    }
}

internal sealed class GuardianConfigurationAction(Func<GuardianConfigurationData> read,
    Func<GuardianConfigurationData, CancellationToken, Task> apply) : IGuardianAction
{
    internal GuardianTarget Target(GuardianContext context, string desired)
    {
        GuardianConfiguration.Decode(desired);
        var current = GuardianConfiguration.Serialize(read());
        return new("configuration", "aether-settings", EvidenceFile.Hash(context.Host + "/" + context.User + "/settings-v1"),
            EvidenceFile.Hash(current), new Dictionary<string, string> { ["payload"] = current, ["desired"] = desired });
    }
    public Task<GuardianTarget> ReadAsync(GuardianTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var current = GuardianConfiguration.Serialize(read());
        return Task.FromResult(target with { State = EvidenceFile.Hash(current),
            Fields = new Dictionary<string, string>(target.Fields) { ["payload"] = current } });
    }
    public async Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token)
    {
        if (!GuardianCore.Matches(plan.Before, await ReadAsync(plan.Before, token)))
            throw new InvalidOperationException("AETHER-Konfiguration seit der Planung geändert.");
        var desired = GuardianConfiguration.Decode(plan.Before.Fields["desired"]);
        await apply(desired, token);
        return "Konfiguration übernommen; gespeicherten und laufenden Zustand erneut prüfen.";
    }
    internal static GuardianPlan Plan(GuardianTarget target) => new("restore-configuration", target, EvidenceFile.Hash(target.Fields["desired"]),
        "Aktueller Benutzer", "Nur erlaubte AETHER-Konfigurationsfelder", "Watch-/Anzeige-/Intelligence-/Favoriteneinstellungen ersetzen",
        "Watch-Verhalten und lokale Historienpräferenzen ändern sich; keine destruktiven Watch-Aktionen",
        "Theme-/Sprachdarstellung gegebenenfalls nach manuellem App-Neustart vollständig wirksam",
        "Exakter erlaubter Vorherinhalt vor Wirkung dauerhaft im sensiblen Journal",
        "Erneut serialisierte laufende und gespeicherte Konfiguration gegen den freigegebenen Inhalt vergleichen",
        RollbackCapability.Available, "Erneut bestätigen und erlaubten Vorherinhalt konfliktgeprüft zurückschreiben");
}
