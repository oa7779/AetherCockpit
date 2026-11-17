using System.Globalization;
using System.Text;
using System.Text.Json;
using AetherOperations;

namespace SystemCockpit.Services;

public enum HardwareClass
{
    System, Cpu, Gpu, Npu, Mainboard, Firmware, Chipset, Memory, Storage, Pci,
    Network, Wlan, Bluetooth, Audio, Display, Usb, Security, Power, Sensors, Peripheral
}

public enum HardwareCadence { Inventory, Dynamic, Live }

public sealed record HardwareFact(string Key, string Name, SourceObservation<string> Observation,
    HardwareCadence Cadence = HardwareCadence.Inventory, string? Calculation = null,
    string? DatabaseVersion = null, string? Match = null)
{
    public string Display => Observation.HasValue
        ? Observation.Value + (Observation.Unit.Length == 0 ? "" : " " + Observation.Unit)
        : "Nicht verfügbar";
    public string State => Observation.Metadata.Availability.ToString();
    public string Provenance => Observation.Metadata.SourceType.ToString();
    public string Privacy => Observation.Metadata.Classification.ToString();
    public string Time => Observation.Metadata.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public HardwareFact At(DateTimeOffset now)
    {
        var maximumAge = Cadence switch
        {
            HardwareCadence.Live => TimeSpan.FromSeconds(10),
            HardwareCadence.Dynamic => TimeSpan.FromMinutes(1),
            _ => TimeSpan.MaxValue
        };
        return Observation.HasValue && Observation.Metadata.Availability == DataAvailability.Available &&
            now - Observation.Metadata.Timestamp > maximumAge
            ? this with { Observation = Observation.AsStale("Gültigkeitsdauer überschritten; erneute Abfrage erforderlich.") }
            : this;
    }

    public static HardwareFact Missing(string key, string name, string source, DateTimeOffset time,
        string reason, DataAvailability state = DataAvailability.Unavailable, string unit = "",
        DataClassification classification = DataClassification.Normal) =>
        new(key, name, SourceObservation<string>.Missing(unit, new(source, SourceType.Direct, time,
            DataConfidence.Unknown, state, requiresElevation: state == DataAvailability.AccessDenied,
            classification: classification, error: reason)));
}

public sealed record HardwareComponent(string Id, HardwareClass Class, string Name,
    IReadOnlyList<HardwareFact> Facts, string? ParentId = null)
{
    public override string ToString() => Name;
}

public sealed record HardwareInventory(DateTimeOffset GeneratedAt, IReadOnlyList<HardwareComponent> Components)
{
    public HardwareInventory At(DateTimeOffset now) =>
        new(now, Components.Select(c => c with { Facts = c.Facts.Select(f => f.At(now)).ToArray() }).ToArray());
}

// Identities are internal join keys, never report labels or exported identifiers.
public static class HardwareReport
{
    public static string Export(HardwareInventory inventory, string format, bool sensitiveConsent)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var current = inventory.At(DateTimeOffset.UtcNow);
        var aliases = current.Components.Select((c, i) => (c.Id, Alias: $"component-{i + 1}"))
            .ToDictionary(x => x.Id, x => x.Alias, StringComparer.OrdinalIgnoreCase);
        var rows = current.Components.SelectMany(c => c.Facts
            .Where(f => f.Observation.Metadata.Classification != DataClassification.Secret)
            .Select(f =>
            {
                var observation = f.Observation;
                var m = observation.Metadata;
                var permitted = DiagnosticDataPolicy.CanShare(m.Classification, sensitiveConsent);
                return new
                {
                    Component = aliases[c.Id], Class = c.Class.ToString(),
                    Parent = c.ParentId is { } parent ? aliases.GetValueOrDefault(parent) : null,
                    Field = f.Name, Kind = f.Cadence.ToString(), Value = permitted && observation.HasValue ? observation.Value : null,
                    Redacted = !permitted, observation.HasValue, observation.Unit,
                    m.Source, SourceType = m.SourceType.ToString(), m.Timestamp,
                    Confidence = m.Confidence.ToString(), Availability = m.Availability.ToString(),
                    m.RequiresElevation, m.IsEstimated, m.IsCalculated, m.IsSensitive,
                    Classification = m.Classification.ToString(),
                    Error = permitted ? m.Error : "Sensitive: nicht freigegeben",
                    DocumentationLink = m.DocumentationLink?.AbsoluteUri,
                    Calculation = permitted ? f.Calculation : null,
                    DatabaseVersion = permitted ? f.DatabaseVersion : null, Match = permitted ? f.Match : null
                };
            })).ToArray();
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { Schema = "aether.hardware/1", current.GeneratedAt, Rows = rows },
                new JsonSerializerOptions { WriteIndented = true });
        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new StringBuilder();
            builder.AppendLine("Component,Class,Parent,Field,Kind,Value,Unit,HasValue,Redacted,Source,SourceType,Timestamp,Confidence,Availability,RequiresElevation,IsEstimated,IsCalculated,Classification,Error,Documentation,Calculation,DatabaseVersion,Match");
            foreach (var r in rows)
                builder.AppendLine(string.Join(",", new object?[] { r.Component, r.Class, r.Parent, r.Field, r.Kind,
                    r.Value, r.Unit, r.HasValue, r.Redacted, r.Source, r.SourceType, r.Timestamp.ToString("O"),
                    r.Confidence, r.Availability, r.RequiresElevation, r.IsEstimated, r.IsCalculated,
                    r.Classification, r.Error, r.DocumentationLink, r.Calculation, r.DatabaseVersion, r.Match }.Select(Csv)));
            return builder.ToString();
        }
        if (!format.Equals("txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unbekanntes Hardware-Berichtsformat.", nameof(format));
        var text = new StringBuilder($"AETHER HARDWARE / {current.GeneratedAt:O}\n");
        foreach (var r in rows)
        {
            text.AppendLine($"[{r.Class} / {r.Component} / Parent={r.Parent ?? "-"}] {r.Field}");
            text.AppendLine($"  {(r.Redacted ? "[Sensitive: redigiert]" : r.HasValue ? r.Value : "Nicht verfügbar")} {r.Unit}");
            text.AppendLine($"  {r.Kind} | {r.SourceType} | {r.Source} | {r.Timestamp:O} | {r.Availability} | {r.Confidence} | {r.Classification}");
            text.AppendLine($"  Elevation={r.RequiresElevation} Estimated={r.IsEstimated} Calculated={r.IsCalculated}");
            if (r.Error is not null) text.AppendLine("  Ursache: " + r.Error);
            if (r.DocumentationLink is not null) text.AppendLine("  Dokumentation: " + r.DocumentationLink);
            if (r.Calculation is not null) text.AppendLine("  Berechnung: " + r.Calculation);
            if (r.DatabaseVersion is not null) text.AppendLine($"  Database={r.DatabaseVersion} Match={r.Match}");
        }
        return text.ToString();
    }

    internal static string Csv(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') ||
            text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@') ||
            text.StartsWith('\t') || text.StartsWith('\r'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public sealed record HardwareDatabaseEntry(string Id, string Source, string License, DateOnly Reviewed,
    string Version, string Match, DataConfidence Confidence, Uri Documentation);

public static class HardwareDatabase
{
    public static bool IsValidMatch(HardwareDatabaseEntry entry, string exactIdentity) =>
        entry.Id.Length > 0 && entry.Source.Length > 0 && entry.Version.Length > 0 &&
        entry.Reviewed != default && entry.Reviewed <= DateOnly.FromDateTime(DateTime.UtcNow) &&
        entry.License is "CC0-1.0" or "MIT" or "BSD-3-Clause" &&
        entry.Match.Length > 0 && entry.Match.Equals(exactIdentity, StringComparison.OrdinalIgnoreCase) &&
        entry.Documentation.IsAbsoluteUri && entry.Documentation.Scheme == "https" &&
        entry.Documentation.UserInfo.Length == 0 &&
        entry.Confidence is DataConfidence.ProviderReported or DataConfidence.Corroborated;
}

public sealed record HardwareLogoLicense(string Manufacturer, Uri Source, string RightsHolder,
    string Terms, string Purpose, string ModificationRight, string Attribution,
    string Sha256, DateOnly Reviewed, bool Approved);

public static class HardwareLogoRegistry
{
    public static bool CanActivate(HardwareLogoLicense license, string actualHash) =>
        license.Approved && license.Manufacturer.Length > 0 && license.RightsHolder.Length > 0 &&
        license.Terms.Length > 0 && license.Purpose.Length > 0 && license.ModificationRight.Length > 0 &&
        license.Attribution.Length > 0 && license.Source.IsAbsoluteUri && license.Source.Scheme == "https" &&
        license.Source.UserInfo.Length == 0 && license.Reviewed != default &&
        license.Reviewed <= DateOnly.FromDateTime(DateTime.UtcNow) &&
        license.Sha256.Length == 64 && license.Sha256.All(Uri.IsHexDigit) &&
        license.Sha256.Equals(actualHash, StringComparison.OrdinalIgnoreCase);
}
