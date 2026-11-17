using System.IO;
using System.Text.Json;

namespace SystemCockpit.Services;

internal sealed class GuardianActions(string? backupDirectory = null) : IGuardianAction
{
    internal GuardianConfigurationAction? Configuration { get; set; }
    private readonly StartupManager _startup = new(backupDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherCockpit", "Backups"));
    internal static GuardianPlan MakePlan(GuardianTarget target, string action)
    {
        target.Validate();
        var expected = (target.Kind, action, target.State) switch
        {
            ("service", "start", "Stopped") => "Running", ("service", "stop", "Running") => "Stopped",
            ("service", "restart", "Running") => "Running",
            ("task" or "startup", "enable", "Disabled") => "Enabled",
            ("task" or "startup", "disable", "Enabled") => "Disabled",
            _ => throw new InvalidOperationException("Aktion passt nicht zum aktuellen Zielzustand.")
        };
        var rollback = action == "restart" ? RollbackCapability.NotAvailable : target.Kind == "service" ? RollbackCapability.Partial : RollbackCapability.Available;
        return new(action, target, expected, target.Kind == "startup" && target.Fields.GetValueOrDefault("hive") == "HKCU" ?
            "Aktueller Benutzer / Registry64" : "Windows-Zielrechte; kein automatisches Umgehen oder Erhöhen",
            target.Fields.GetValueOrDefault("dependencies") ?? target.Fields.GetValueOrDefault("xml") ?? "Exakter Run-Eintrag, keine vollständige Abhängigkeitsanalyse",
            $"Nur {target.Kind} {target.Id}: {target.State} → {expected}",
            target.Kind == "service" ? "Unterbrechung laufender Arbeit, Datenverlust und externe Dienstwirkungen möglich. Keine kaskadierenden Stopps." :
                "Ändert zukünftigen Start. Bereits laufende Programme oder Aufgaben werden nicht beendet.",
            "Kein Neustart ausgelöst; weiterer Neustartbedarf nicht universell bewertbar",
            "Exakter Ausgangszustand im lokalen sensiblen Journal; Autostart zusätzlich über vorhandene Sicherung",
            "Identität und tatsächlichen Zustand nach dem Eingriff erneut lesen", rollback,
            rollback == RollbackCapability.Available ? "Vorherigen Aktivierungszustand konfliktgeprüft wiederherstellen." :
                "Vergangene Laufzeit, Dienstwirkungen und verlorene Daten können nicht zurückgeholt werden.");
    }
    internal async Task<GuardianTarget> InspectAsync(string kind, string id, CancellationToken token)
    {
        if (kind == "service") return await Task.Run(() => GuardianService.Read(id), token).ConfigureAwait(false);
        if (kind == "startup")
        {
            var split = id.IndexOf('\\');
            if (split < 1 || id[..split] is not ("HKCU" or "HKLM")) throw new ArgumentException("Autostartziel als HKCU\\Name oder HKLM\\Name angeben.");
            var entry = await Task.Run(() => _startup.Read().SingleOrDefault(e => e.Hive == id[..split] &&
                e.Name.Equals(id[(split + 1)..], StringComparison.OrdinalIgnoreCase)), token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Autostartziel fehlt oder ist nicht eindeutig.");
            if (entry.Command.Contains("Aether", StringComparison.OrdinalIgnoreCase) || entry.Command.Contains("SystemCockpit", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AETHER-eigene Autostarts sind hier geschützt.");
            return new("startup", id, EvidenceFile.Hash(entry.Hive + "\n" + entry.Name + "\n" + entry.Command),
                entry.Enabled ? "Enabled" : "Disabled", new Dictionary<string, string>
                { ["hive"] = entry.Hive, ["name"] = entry.Name, ["command"] = entry.Command, ["source"] = "Registry64 Run / bestehende StartupManager-Sicherung" });
        }
        if (kind != "task") throw new ArgumentException("Kein erlaubter Guardian-Aktionstyp.");
        var response = await AdminShell.RunAsync(TaskScript(id), TimeSpan.FromSeconds(25), token).ConfigureAwait(false);
        return ParseTask(response, id);
    }
    public async Task<GuardianTarget> ReadAsync(GuardianTarget target, CancellationToken token)
    {
        if (target.Kind == "configuration") return await (Configuration ??
            throw new InvalidOperationException("Konfigurationsadapter fehlt.")).ReadAsync(target, token).ConfigureAwait(false);
        if (target.Kind == "invocation") return target with { State = "SystemStateUnverified" };
        if (target.Kind == "process")
        {
            var identities = JsonSerializer.Deserialize<ProcessIdentity[]>(target.Fields["targets"]) ?? throw new JsonException("Prozessidentitäten fehlen.");
            if (identities.Length is < 1 or > 8192 || identities.Any(i => !i.IsKnown)) throw new JsonException("Prozessidentitäten ungültig.");
            var current = await new NativeProcessActionSource().ReadAsync(token).ConfigureAwait(false);
            return target with { State = identities.All(i => current.Any(p => p.Identity == i)) ? "ObservedAlive" :
                identities.Any(i => current.Any(p => p.Id == i.Pid && !p.Identity.IsKnown)) ? "IdentityUnknown" : "OriginalIdentityNotObserved" };
        }
        return await InspectAsync(target.Kind, target.Id, token).ConfigureAwait(false);
    }
    public async Task<string> ExecuteAsync(GuardianPlan plan, CancellationToken token)
    {
        if (plan.Before.Kind == "configuration")
            return await (Configuration ?? throw new InvalidOperationException("Konfigurationsadapter fehlt.")).ExecuteAsync(plan, token).ConfigureAwait(false);
        MakePlan(plan.Before, plan.Action);
        token.ThrowIfCancellationRequested();
        if (plan.Before.Kind == "service") return await GuardianService.ExecuteAsync(plan, token).ConfigureAwait(false);
        if (plan.Before.Kind == "startup")
        {
            var current = await ReadAsync(plan.Before, token).ConfigureAwait(false);
            if (!GuardianCore.Matches(plan.Before, current)) throw new InvalidOperationException("Autostart wurde extern geändert.");
            var f = current.Fields;
            await Task.Run(() => _startup.SetEnabled(new(f["hive"], f["name"], f["command"], current.State == "Enabled"),
                plan.ExpectedState == "Enabled"), token).ConfigureAwait(false);
            return "Bestehender konfliktgeprüfter StartupManager ausgeführt.";
        }
        var result = await AdminShell.RunAsync(TaskScript(plan.Before.Id, plan), TimeSpan.FromSeconds(25), token).ConfigureAwait(false);
        ParseTask(result, plan.Before.Id);
        return "Task-Scheduler-Aktivierung gesetzt; Nachherprüfung folgt.";
    }
    private static GuardianTarget ParseTask(CommandResult result, string id)
    {
        if (result.ExitCode != 0 || result.OutputTruncated) throw new InvalidOperationException("Task-Scheduler: " + result.Error);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(result.Output) ?? throw new JsonException("Taskzustand fehlt.");
        if (fields.Count != 6 || !fields.TryGetValue("xml", out var xml) || !fields.TryGetValue("enabled", out var enabled) ||
            !fields.TryGetValue("source", out _) || !fields.ContainsKey("lastRun") || !fields.ContainsKey("nextRun") ||
            !fields.ContainsKey("lastResult") || enabled is not ("True" or "False") || xml.Length > 32768)
            throw new JsonException("Ungültiger Taskzustand.");
        return new("task", id, EvidenceFile.Hash(xml), enabled == "True" ? "Enabled" : "Disabled", fields);
    }
    internal static string TaskScript(string id, GuardianPlan? plan = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || !id.StartsWith('\\') || id.EndsWith('\\') ||
            id.Contains("..", StringComparison.Ordinal) || id.Contains('*') || id.Contains('?') ||
            id.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) || id.Contains("Aether", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("SystemCockpit", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Nicht unterstützte, geschützte oder ungenaue Taskidentität.");
        var slash = id.LastIndexOf('\\');
        var prefix = "$ErrorActionPreference='Stop'; $scheduler=New-Object -ComObject 'Schedule.Service'; $scheduler.Connect(); " +
            $"$folder=$scheduler.GetFolder({AdminManagement.Quote(slash == 0 ? "\\" : id[..slash])}); " +
            $"$task=$folder.GetTask({AdminManagement.Quote(id[(slash + 1)..])}); " +
            "$enabled=[string][bool]$task.Enabled; [xml]$xml=$task.Xml; " +
            "$ns=[Xml.XmlNamespaceManager]::new($xml.NameTable); $ns.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task'); " +
            "$node=$xml.SelectSingleNode('/t:Task/t:Settings/t:Enabled',$ns); if($null -ne $node){[void]$node.ParentNode.RemoveChild($node)}; " +
            "$identity=$xml.OuterXml; ";
        if (plan is not null)
        {
            if (plan.Action is not ("enable" or "disable")) throw new ArgumentException("Nicht erlaubte Taskaktion.");
            prefix += $"if($identity -cne {AdminManagement.Quote(plan.Before.Fields["xml"])} -or " +
                $"$enabled -ne '{(plan.Before.State == "Enabled" ? "True" : "False")}'){{throw 'Taskidentität oder Zustand geändert'}}; " +
                $"$task.Enabled=${(plan.ExpectedState == "Enabled" ? "true" : "false")}; $enabled=[string][bool]$task.Enabled; ";
        }
        return prefix + "$last=if($task.LastRunTime.Year -ge 1900){$task.LastRunTime.ToUniversalTime().ToString('O')}else{'Nicht gemeldet'}; " +
            "$next=if($task.NextRunTime.Year -ge 1900){$task.NextRunTime.ToUniversalTime().ToString('O')}else{'Nicht geplant / nicht gemeldet'}; " +
            "@{xml=$identity;enabled=$enabled;lastRun=$last;nextRun=$next;lastResult=[string]$task.LastTaskResult;" +
            "source='Task Scheduler IRegisteredTask / exaktes XML ohne Enabled'} | ConvertTo-Json -Compress";
    }
}
