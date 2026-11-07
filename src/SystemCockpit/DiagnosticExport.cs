using System.Text;
using System.Text.RegularExpressions;

namespace SystemCockpit;

/// <summary>
/// Baut einen lokalen Text-Diagnosebericht aus den bereits gelesenen Modellwerten
/// und schwaerzt auf Wunsch persoenliche Angaben (Rechnername, Benutzername,
/// Benutzerpfade, IPv4- und MAC-Adressen) vor dem Speichern.
/// Die Schwaerzung ist Mustererkennung, keine Garantie: Vorschau selbst pruefen.
/// </summary>
public static partial class DiagnosticExport
{
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex IPv4();
    [GeneratedRegex(@"\b[0-9A-Fa-f]{2}(?:[:-][0-9A-Fa-f]{2}){5}\b")]
    private static partial Regex Mac();

    public static string Build(CockpitModel model)
    {
        var report = new StringBuilder();
        report.AppendLine($"AETHER DIAGNOSEBERICHT / {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Rechner: {Environment.MachineName} / Benutzer: {Environment.UserName}");
        report.AppendLine("Lesender Bericht aus den zuletzt angezeigten Cockpit-Werten; keine neue Messung.");
        report.AppendLine();
        report.AppendLine("== SYSTEM ==");
        report.AppendLine(model.SystemIdentity);
        report.AppendLine(model.LiveStatus + " | " + model.SensorStatus);
        void Section(string title, IEnumerable<Check> checks)
        {
            report.AppendLine();
            report.AppendLine($"== {title} ==");
            var any = false;
            foreach (var check in checks)
            {
                report.AppendLine($"[{check.State.ToUpperInvariant()}] {check.Name}: {check.Value} | {check.Detail}");
                any = true;
            }
            if (!any) report.AppendLine("(keine Eintraege)");
        }
        Section("WINDOWS-PRUEFUNGEN", model.Checks);
        Section("STORAGE HEALTH", model.Disks);
        Section("VOLUMES", model.Volumes);
        Section("ERWEITERTE DIAGNOSE", model.Diagnostics);
        Section("AKTUELLE HINWEISE / SYSTEMPULS", model.Alerts);
        report.AppendLine();
        report.AppendLine("== SENSOREN (AKTUELLE ANZEIGE) ==");
        if (model.Sensors.Count == 0) report.AppendLine("(keine Sensorwerte in der aktuellen Anzeige)");
        foreach (var sensor in model.Sensors)
            report.AppendLine($"{sensor.Hardware} | {sensor.Name} | {sensor.Type}: {sensor.Display}");
        report.AppendLine();
        report.AppendLine("Hinweis: 'Nicht verfuegbar' bedeutet kein Messwert, nicht 0. " +
            "Grenzwertkanaele sind gekennzeichnet und keine Ist-Temperaturen.");
        return report.ToString();
    }

    public static string Redact(string text)
    {
        var result = text;
        // Laengere Treffer zuerst ersetzen, damit Teilstrings nicht uebrig bleiben.
        foreach (var value in new[] { Environment.MachineName, Environment.UserName }
                     .Where(v => v.Length >= 2).OrderByDescending(v => v.Length))
            result = Regex.Replace(result, Regex.Escape(value),
                value == Environment.MachineName ? "[RECHNER]" : "[BENUTZER]", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"(?i)(\\Users\\)[^\\\r\n]+", "$1[BENUTZER]");
        result = IPv4().Replace(result, "[IPV4]");
        result = Mac().Replace(result, "[MAC]");
        return result;
    }
}
