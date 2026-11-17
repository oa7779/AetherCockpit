using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace AetherOperations.Platform;

public sealed record PlatformInventory(int Schema, ImmutableArray<AdminTarget> Targets, ImmutableArray<TargetGroup> Groups);

public static class TargetInventory
{
    public const int MaximumTargets = 1000;
    public static readonly ImmutableArray<string> Fields =
    [
        "fqdn", "addresses", "operatingSystem", "build", "architecture", "role", "domainOrWorkgroup",
        "reachability", "lastContact", "authentication", "permission", "clockSkew", "reboot",
        "updates", "protection", "storage", "services", "software", "hardware", "certificates",
        "adapters", "warnings", "freeBytes"
    ];

    public static Guid StableId(string scope, string identity) =>
        new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            scope + ":" + identity.ToUpperInvariant())).AsSpan(0, 16));

    public static AdminTarget Create(string name, string host, TargetKind kind, TransportKind transport, bool simulated = false)
    {
        var target = new AdminTarget(StableId(simulated ? "simulation" : "explicit-target", host), name, kind, host,
            transport, simulated, ImmutableDictionary<string, PlatformFact>.Empty);
        Validate(target);
        return target;
    }

    public static void Validate(AdminTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Id == Guid.Empty || string.IsNullOrWhiteSpace(target.Name) || target.Name.Length > 160 ||
            target.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(target.Host) || target.Host.Length > 253 ||
            Uri.CheckHostName(target.Host) == UriHostNameType.Unknown || !Enum.IsDefined(target.Kind) ||
            !Enum.IsDefined(target.Transport) || target.Simulated != (target.Transport == TransportKind.Simulated) ||
            target.Facts is null || target.Facts.Count > Fields.Length || target.Facts.Any(pair =>
                !Fields.Contains(pair.Key) || pair.Value is null || pair.Value.Metadata is null ||
                pair.Value.Value?.Length > 4096 ||
                (pair.Value.Value is not null) != (pair.Value.Metadata.Availability is DataAvailability.Available or DataAvailability.Stale)))
            throw new InvalidDataException("Ungültiges Ziel, Quellfeld oder Transport; keine Verbindung wurde gestartet.");
    }

    public static void Validate(PlatformInventory inventory)
    {
        if (inventory.Schema != 1 || inventory.Targets.IsDefault || inventory.Groups.IsDefault ||
            inventory.Targets.Length > MaximumTargets || inventory.Groups.Length > 100 ||
            inventory.Targets.Any(t => t is null) || inventory.Groups.Any(g => g is null) ||
            inventory.Targets.Select(t => t.Id).Distinct().Count() != inventory.Targets.Length ||
            inventory.Groups.Select(g => g.Id).Distinct().Count() != inventory.Groups.Length)
            throw new InvalidDataException("Ungültiges oder zu großes Zielinventar.");
        foreach (var target in inventory.Targets) Validate(target);
        foreach (var group in inventory.Groups) _ = Resolve(group, inventory.Targets);
    }

    public static ImmutableArray<AdminTarget> Resolve(TargetGroup group, ImmutableArray<AdminTarget> targets)
    {
        if (group.Id == Guid.Empty || string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 160 ||
            group.Members.IsDefault || group.Members.Distinct().Count() != group.Members.Length ||
            group.Members.Any(id => !targets.Any(t => t.Id == id)) ||
            group.Kind is { } kind && !Enum.IsDefined(kind) ||
            group.Transport is { } transport && !Enum.IsDefined(transport) ||
            group.Dynamic && (!group.Members.IsEmpty || group.Kind is null && group.Transport is null))
            throw new InvalidDataException("Ungültige Gruppe oder unbekannte Mitglieder.");
        return targets.Where(t => (group.Dynamic || group.Members.Contains(t.Id)) &&
            (group.Kind is null || t.Kind == group.Kind) &&
            (group.Transport is null || t.Transport == group.Transport)).ToImmutableArray();
    }

    public static PlatformFact Read(AdminTarget target, string field)
    {
        if (!Fields.Contains(field)) throw new ArgumentException("Unbekanntes Inventarfeld.", nameof(field));
        return target.Facts.GetValueOrDefault(field) ?? new(null,
            new("Noch nicht erfasst", SourceType.Unconfirmed, DateTimeOffset.UtcNow, DataConfidence.Unknown, DataAvailability.Unknown));
    }

    public static ImmutableArray<AdminTarget> ImportJson(string text)
    {
        if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("Zielimport ist zu groß.");
        var targets = JsonSerializer.Deserialize<ImmutableArray<AdminTarget>>(text, PlatformJson.Options);
        Validate(new PlatformInventory(1, targets, []));
        if (targets.Any(t => t.Kind == TargetKind.LocalComputer || t.Simulated || t.Facts.Count != 0))
            throw new InvalidDataException("Zielimport erlaubt nur Adressen und Typen, keine lokalen/simulierten Identitäten oder behaupteten Messquellen.");
        return targets;
    }

    public static ImmutableArray<AdminTarget> ImportCsv(string text)
    {
        try { return ParseCsv(text); }
        catch (MalformedLineException ex) { throw new InvalidDataException("Ungültige CSV-Zeile oder Anführungszeichen.", ex); }
    }

    private static ImmutableArray<AdminTarget> ParseCsv(string text)
    {
        if (text.Length > 1024 * 1024) throw new InvalidDataException("CSV-Zielimport ist zu groß.");
        using var parser = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        if (parser.ReadFields() is not ["Name", "Host"])
            throw new InvalidDataException("CSV benötigt genau die Spalten Name,Host.");
        var rows = ImmutableArray.CreateBuilder<AdminTarget>();
        while (!parser.EndOfData)
        {
            if (parser.ReadFields() is not [var name, var host] || rows.Count >= MaximumTargets)
                throw new InvalidDataException("Ungültige CSV-Zeile oder zu viele Ziele.");
            rows.Add(Create(name, host, TargetKind.WindowsClient, TransportKind.WinRM));
        }
        var result = rows.ToImmutable();
        Validate(new PlatformInventory(1, result, []));
        return result;
    }

    public static ImmutableArray<TopologyEdge> Topology(PlatformInventory inventory)
    {
        Validate(inventory);
        var metadata = new SourceMetadata("Explizites lokales Ziel-/Gruppeninventar", SourceType.Direct,
            DateTimeOffset.UtcNow, DataConfidence.ProviderReported, DataAvailability.Available, classification: DataClassification.Sensitive);
        return inventory.Groups.SelectMany(group => Resolve(group, inventory.Targets).Select(target =>
            new TopologyEdge(group.Id, target.Id, "Explizite Gruppenmitgliedschaft", metadata)))
            .Concat(inventory.Targets.Select(target => new TopologyEdge(target.Id,
                StableId("transport", target.Transport.ToString()), "Konfigurierter Transport, kein Verbindungsnachweis", metadata)))
            .ToImmutableArray();
    }

    public static ComplianceFinding Evaluate(AdminTarget target, DesiredState desired, DateTimeOffset now)
    {
        Validate(target);
        if (desired.Field != "freeBytes" || desired.Unit != "Bytes" || desired.Minimum < 0)
            throw new ArgumentException("Qualifiziert ist nur freier Speicher in Bytes.", nameof(desired));
        var fact = target.Facts.GetValueOrDefault(desired.Field);
        var metadata = fact?.Metadata ?? new("Keine Quelle", SourceType.Unconfirmed, now, DataConfidence.Unknown, DataAvailability.Unknown);
        if (fact?.Value is null || metadata.Availability != DataAvailability.Available ||
            metadata.Timestamp > now || now - metadata.Timestamp > TimeSpan.FromMinutes(10) ||
            !decimal.TryParse(fact.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return new(target.Id, "Nicht bewertbar", "Aktuelle belegte Freiplatzdaten fehlen.", metadata);
        return new(target.Id, value >= desired.Minimum ? "Erfüllt" : "Abweichung",
            $"Gemeldeter freier Speicher: {value.ToString(CultureInfo.InvariantCulture)} Bytes; Soll: {desired.Minimum.ToString(CultureInfo.InvariantCulture)} Bytes. Keine Korrektur ausgeführt.", metadata);
    }
}
