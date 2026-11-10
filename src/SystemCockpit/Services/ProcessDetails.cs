using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AetherOperations;

namespace SystemCockpit.Services;

internal sealed record ProcessDetails(ProcessIdentity Identity, string? Path, IntPtr Window, IReadOnlyList<HardwareFact> Facts,
    System.Windows.Media.Imaging.BitmapSource? Icon);
internal static class ProcessInspector
{
    internal static ProcessDetails Read(ProcessIdentity identity, CancellationToken token)
    {
        using var handle = ProcessNative.Open(identity);
        token.ThrowIfCancellationRequested();
        if (ProcessNative.Exited(handle)) throw new InvalidOperationException("Prozess wurde bereits beendet.");
        var now = DateTimeOffset.UtcNow;
        var facts = new List<HardwareFact>();
        void Fact(string key, string label, Func<string> read, DataClassification privacy = DataClassification.Normal)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                facts.Add(new(key, label, SourceObservation<string>.Present(read(), "",
                    new("Windows-Prozess-/Datei-API", SourceType.Direct, now, DataConfidence.ProviderReported,
                        DataAvailability.Available, classification: privacy))));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException or NotSupportedException)
            {
                Trace.TraceWarning("Prozessdetail {0}: {1}", key, ex);
                facts.Add(HardwareFact.Missing(key, label, "Windows-Prozess-/Datei-API", now,
                    ex is Win32Exception w ? $"Win32 {w.NativeErrorCode}" : ex.GetType().Name,
                    ex is Win32Exception { NativeErrorCode: 5 } or UnauthorizedAccessException ? DataAvailability.AccessDenied : DataAvailability.Unavailable));
            }
        }
        string? path = null;
        Fact("path", "Ausführbare Datei / vollständiger Pfad", () => path = ProcessNative.Path(handle), DataClassification.Sensitive);
        Fact("architecture", "Architektur", () => ProcessNative.Architecture(handle));
        Fact("protection", "Windows-Prozessschutz", () => ProcessNative.Protection(handle) is { Length: > 0 } value ? value : "Kein gemeldeter Critical-/Protection-Level");
        Fact("token", "Benutzer-SID / Elevation / Integrität", () =>
        {
            var value = ProcessNative.Token(handle);
            return $"{value.User} / Elevated: {value.Elevated} / Integrity-RID: {value.Integrity}";
        }, DataClassification.Sensitive);
        using var process = Process.GetProcessById(identity.Pid);
        Fact("priority", "Prioritätsklasse", () => process.PriorityClass.ToString());
        Fact("session", "Windows-Sitzung", () => process.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture), DataClassification.Sensitive);
        Fact("start", "Startzeit UTC", () => new DateTime(identity.StartTicks, DateTimeKind.Utc).ToString("O"), DataClassification.Sensitive);
        Fact("runtime", "Laufzeit zum Abfragezeitpunkt", () => (now.UtcDateTime - new DateTime(identity.StartTicks, DateTimeKind.Utc)).ToString());
        if (path is not null)
        {
            Fact("description", "Dateibeschreibung (unbestätigte Dateimetadaten)", () => { ProcessFileInfo.RequireLocal(path); return FileVersionInfo.GetVersionInfo(path).FileDescription ?? "Keine Dateibeschreibung"; }, DataClassification.Sensitive);
            Fact("publisher", "Firma (unbestätigte Dateimetadaten, keine Signatur)", () => { ProcessFileInfo.RequireLocal(path); return FileVersionInfo.GetVersionInfo(path).CompanyName ?? "Keine Firmenangabe"; }, DataClassification.Sensitive);
            Fact("file", "Datei vorhanden", () => { File.GetAttributes(path); return "Zum Abfragezeitpunkt vorhanden"; }, DataClassification.Sensitive);
            Fact("signature", "Offline-Signaturvertrauen", () => ProcessFileInfo.Trust(path));
        }
        var window = Window(identity.Pid);
        Fact("window", "Sichtbares Fenster / Vordergrund / Reaktion", () => window == IntPtr.Zero
            ? "Kein sichtbares Top-Level-Fenster beobachtet; keine Aussage über Hintergrundarbeit."
            : $"Sichtbar / Vordergrund: {GetForegroundWindow() == window} / Windows meldet hängend: {IsHungAppWindow(window)}");
        facts.Add(HardwareFact.Missing("network-rate", "Netzwerk-Datenrate je Prozess", "IP Helper Owner-PID-Tabellen", now,
            "Socketbesitz enthält keine Bytezähler. Adapterrate wird nicht auf Prozesse verteilt.", DataAvailability.Unsupported));
        facts.Add(HardwareFact.Missing("hardware-effect", "Hardware-/Sensorwirkung", "Vorhandene Hardware-/Sensorbeobachtungen", now,
            "Zeitgleiche Last und Sensoränderung beweisen keine Prozessursache; keine geratene Zuordnung.", DataAvailability.Unsupported));
        if (ProcessNative.Exited(handle)) throw new InvalidOperationException("Prozess während der Detailabfrage beendet; Details nicht als aktuell übernommen.");
        System.Windows.Media.Imaging.BitmapSource? icon = null;
        if (path is not null)
            Fact("icon", "Programmsymbol", () => { icon = ProcessFileInfo.Icon(path); return icon is null ? "Kein Dateisymbol" : "Lokales Dateisymbol"; });
        return new(identity, path, window, facts, icon);
    }

    internal static IntPtr Window(int pid)
    {
        var found = IntPtr.Zero;
        EnumCallback callback = (hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == pid && IsWindowVisible(hwnd)) { found = hwnd; return false; }
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        return found;
    }

    internal static async Task<ProcessActionResult> CloseAsync(ProcessIdentity identity, IntPtr window, CancellationToken token)
    {
        var actions = ProcessActions.Create();
        await actions.PrepareAsync(identity, false, token).ConfigureAwait(false);
        using var handle = ProcessNative.Open(identity);
        GetWindowThreadProcessId(window, out var pid);
        if (window == IntPtr.Zero || pid != identity.Pid || !IsWindowVisible(window))
            return new(identity, ProcessActionState.Blocked, "Fenster gehört nicht mehr eindeutig zum Zielprozess.");
        token.ThrowIfCancellationRequested();
        ActionJournal.Append("process-window-close-intent", new { identity });
        if (!PostMessage(window, 0x10, UIntPtr.Zero, IntPtr.Zero)) throw ProcessNative.Error();
        var watch = Stopwatch.StartNew();
        while (!ProcessNative.Exited(handle) && watch.Elapsed < TimeSpan.FromSeconds(2))
            await Task.Delay(50, token).ConfigureAwait(false);
        var result = new ProcessActionResult(identity, ProcessNative.Exited(handle) ? ProcessActionState.Exited : ProcessActionState.CloseRequested,
            ProcessNative.Exited(handle) ? "Reguläres Prozessende bestätigt." : "Schließen angefordert. Anwendung kann nachfragen oder weiterlaufen; keine harte Beendigung.");
        ActionJournal.Append("process-window-close-result", new { result });
        return result;
    }
    private delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wparam, IntPtr lparam);
}
