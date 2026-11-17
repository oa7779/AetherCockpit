using System.Text;
using System.Text.RegularExpressions;
using SystemCockpit.Services;

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
        report.AppendLine(DesktopUiText.Format("Export.ReportTitle", DateTimeOffset.Now));
        report.AppendLine(DesktopUiText.Format("Export.Identity", Environment.MachineName, Environment.UserName));
        report.AppendLine(DesktopUiText.Get("Export.ReadOnly"));
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
            if (!any) report.AppendLine(DesktopUiText.Get("Export.NoEntries"));
        }
        Section(DesktopUiText.Get("Export.WindowsChecks"), model.Checks);
        Section("STORAGE HEALTH", model.Disks);
        Section("VOLUMES", model.Volumes);
        Section(DesktopUiText.Get("Export.Diagnostics"), model.Diagnostics);
        Section(DesktopUiText.Get("Export.Alerts"), model.Alerts);
        report.AppendLine();
        report.AppendLine(DesktopUiText.Get("Export.SensorsHeading"));
        if (model.Sensors.Count == 0) report.AppendLine(DesktopUiText.Get("Export.NoSensors"));
        foreach (var sensor in model.Sensors)
            report.AppendLine($"{sensor.Hardware} | {sensor.Name} | {sensor.Type}: {sensor.Display}");
        report.AppendLine();
        report.AppendLine(DesktopUiText.Get("Export.SensorNotice"));
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
