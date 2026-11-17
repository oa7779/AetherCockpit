using System.Collections.Immutable;

namespace AetherOperations.Platform;

public static class PlatformActions
{
    public static ImmutableArray<AdminAction> All { get; } = [Build("inventory.basic", false), Build("test.marker", true)];
    public static AdminAction Get(string id) => All.SingleOrDefault(a => a.ActionId == id)
        ?? throw new ArgumentException("Aktion ist nicht für die Plattform qualifiziert.", nameof(id));
    public static ImmutableArray<RunbookStep> Steps { get; } =
    [
        new("inventory", RunbookStepKind.Inventory, [], "Explizite Ziele und vorhandene Quellen prüfen"),
        new("preview", RunbookStepKind.Preview, ["inventory"], "Ziele, Vorbedingungen, Wirkung und Wellen einfrieren"),
        new("approval", RunbookStepKind.Approval, ["preview"], "Genauen Vorschauhash zeitlich begrenzt freigeben"),
        new("action", RunbookStepKind.Action, ["approval"], "Canary und begrenzte Wellen ausführen"),
        new("verify", RunbookStepKind.Verify, ["action"], "Jedes Ziel separat verifizieren"),
        new("report", RunbookStepKind.Report, ["verify"], "Ergebnisse und Audit berichten")
    ];
    public static ImmutableArray<AdminRunbook> Runbooks { get; } =
    [
        new("inventory.checked", 1, "Grundbestand geführt prüfen", "inventory.basic", Steps),
        new("marker.reversible", 1, "Auftragsablauf mit Rückweg prüfen", "test.marker", Steps)
    ];
    public static AdminRunbook Runbook(string actionId) => Runbooks.Single(r => r.ActionId == actionId);

    private static AdminAction Build(string id, bool marker) => new(id, 1,
        marker ? "Eigene reversible Testmarke" : "Grundbestand und freien Speicher lesen",
        marker ? "Kontrollierter Plattformtest" : "Inventar",
        marker ? "Auftragsablauf mit vollständigem Rückweg prüfen." : "Belegte Betriebssystem-, Architektur- und Freiplatzdaten erfassen.",
        "Fester eingebauter Handler; kein freier Befehl und keine automatische Remoteverbindung.",
        new([TargetKind.LocalComputer, TargetKind.WindowsClient, TargetKind.WindowsServer, TargetKind.WorkgroupComputer, TargetKind.DomainComputer],
            ".NET-10-Host; reale Demonstration nur lokales Windows", "Bestehende AETHER-.NET-10-Laufzeit", [], [],
            "Aktuelles Benutzerkonto; keine zusätzliche Erhöhung", [TransportKind.Local, TransportKind.Simulated]),
        new("Keine freien Parameter oder Geheimnisse", marker ? "Eigene Marke muss fehlen" : "Lesbare Quelle",
            "Explizites Zielinventar und Backendautorisierung", "Adapter prüft Verbindung und eigene Marke",
            "Ziele, Vertragshash, erwarteter Istzustand und Wellen", marker ? "Eigene auftragsgebundene Datei anlegen" : "Vorhandene lokale Daten lesen",
            marker ? "Exakten Markeninhalt und Hash erneut lesen" : "Quelle und Zeitpunkt je Feld speichern",
            marker ? "Nur eigene unveränderte Marke entfernen" : "Keine Änderung, kein Rückweg erforderlich"),
        new("Kurze Einzelabfrage mit Zielzeitlimit", "Kein Neustart", "Abbruch stoppt weitere Arbeit",
            "Keine Netzwerkkonfiguration", marker ? "Nur eigene Testdatei" : "Nur lesend", "Keine Dienständerung", "Keine Schutzänderung"),
        new(marker ? 1 : 0, marker ? "CreateNew; erfolgreiche Ziele werden nicht wiederholt" : "Lesend wiederholbar",
            true, true, marker ? "Eigene unveränderte Testmarke" : "Nicht erforderlich", DataClassification.Sensitive),
        new("Quellenmetadaten und eigener Nachherhash", "Zielgetrennt, Exitcode und Verifikationsstatus",
            "Explizite Verbindungs-, Zugriffs-, Zeitlimit- und Konfliktfehler",
            "Begrenzte sensible lokale Aufträge; keine Geheimnisse", "Fehlende Quelle ist keine Diagnose",
            "docs/ADMIN-ACTION-CONTRACT.md"));

    public static void Authorize(PlatformPrincipal principal, AdminTarget target, AdminAction action)
    {
        if (string.IsNullOrWhiteSpace(principal.Id) || principal.Id.Length > 256 || !Enum.IsDefined(principal.Role) ||
            principal.Scope.IsDefault || !principal.Scope.Contains(target.Id))
            throw new UnauthorizedAccessException("Ziel ist nicht vom authentifizierten Hostkontext freigegeben.");
        if (action != Get(action.ActionId) || !action.Requirements.Targets.Contains(target.Kind))
            throw new UnauthorizedAccessException("Unveränderter qualifizierter Aktionsvertrag erforderlich.");
        if (action.Safety.Risk > 1 || action.Safety.Risk == 1 &&
            principal.Role is not (PlatformRole.Support or PlatformRole.ClientAdministrator or PlatformRole.PlatformAdministrator))
            throw new UnauthorizedAccessException("Rolle oder Risikoklasse erlaubt diese Aktion nicht.");
    }

    public static void Validate(ExecutionPolicy policy, int count, bool changes)
    {
        if (count is < 1 or > TargetInventory.MaximumTargets || policy.Workers is < 1 or > 8 ||
            policy.WaveSize is < 1 or > 100 || policy.CanaryCount < 0 || policy.CanaryCount > count ||
            changes && count > 1 && policy.CanaryCount == 0 || policy.MaximumFailures is < 0 or > 1000 ||
            policy.TargetTimeoutSeconds is < 1 or > 300 || policy.MaximumRuntimeSeconds is < 1 or > 3600 ||
            policy.WindowEnd is { } end &&
            (end <= DateTimeOffset.UtcNow || policy.NotBefore is { } start && start >= end))
            throw new ArgumentException("Ungültige Ausführungsgrenzen, Canary oder Wartungszeit.", nameof(policy));
    }

    public static ScriptPackage InspectPackage(ScriptPackage manifest)
    {
        if (manifest.ApiVersion != 1 || string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 128 ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64 ||
            string.IsNullOrWhiteSpace(manifest.Publisher) || manifest.Publisher.Length > 160 ||
            string.IsNullOrWhiteSpace(manifest.Origin) || manifest.Origin.Length > 512 ||
            manifest.Sha256 is null || manifest.Sha256.Length != 64 || !manifest.Sha256.All(char.IsAsciiHexDigit) ||
            manifest.Permissions.IsDefault || manifest.Permissions.Length > 32 ||
            manifest.Permissions.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 128))
            throw new InvalidDataException("Ungültiges Erweiterungsmanifest.");
        return manifest with { Trust = PackageTrust.Blocked, SignatureStatus = "Nicht unabhängig geprüft" };
    }

    public static ImmutableArray<TransportCapability> Capabilities => Enum.GetValues<TransportKind>().Select(kind =>
        new TransportCapability(kind, kind is TransportKind.Local or TransportKind.Simulated ? ConnectionState.Available : ConnectionState.NotConfigured,
            kind == TransportKind.Local ? "Nur eingebauter lokaler Schnitt, zielabhängig prüfen" :
            kind == TransportKind.Simulated ? "Testadapter, keine Realnetzverbindung" :
            "Vertragsmodell; im Plattformmotor nicht verbunden. Bestehende Verwaltungswege bleiben getrennt.")).ToImmutableArray();
}
