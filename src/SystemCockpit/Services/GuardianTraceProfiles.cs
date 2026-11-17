using System.Security.Principal;
using System.Runtime.InteropServices;

namespace SystemCockpit.Services;

internal sealed record GuardianTraceProvider(string Id, Guid Guid, ulong Keywords, byte Level, bool SystemLogger,
    string Expected, string Limitation, string Documentation, IReadOnlyList<Guid> EventProviders);

internal sealed record GuardianTraceProfile(string Id, string Name, string Description, IReadOnlyList<string> Components,
    int DefaultSeconds = 5, int MaximumSeconds = 30, int MaximumMiB = 8)
{
    public override string ToString() => Name;
    internal string Rights => "Bewusst administrativ gestarteter AETHER-Prozess; keine Selbsterhöhung. CPU/Systemlogger separat: SeSystemProfilePrivilege kann erforderlich sein; keine automatische Privilegaktivierung";
    internal string Privacy => "Hochsensibel: systemweite Ereignismetadaten, Prozess-/Dateipfade, mögliche Befehlszeilen, Adressen und Endpoints; keine Datei-/Paket-/Speicherinhalte, kein Upload. Dekodierte Fakten: Sensitive";
    internal string Filters => "Nur feste Provider-/Keyword-/Level-Allowlist; keine freie Eingabe; keine Stackwalks oder Payloadfilter";
    internal string Stop => "Zeit-/Größenlimit, Cancellation, Shutdown oder Fehler; nur eigene Session nach GUID-/Namensprüfung stoppen";
    internal string Analysis => "Eigene ETL mit Windows tracerpt dekodieren; Fakten, unsichere Identitäten und abgeleitete Kanten getrennt; keine Ursachenbehauptung";
    internal GuardianTraceQualification Qualification => GuardianTraceProfiles.Qualification(Id);
    internal bool Released => Qualification.ProductionReleased;
}

internal sealed record GuardianTraceQualification(bool Implemented, bool SyntheticallyValidated,
    bool RealLifecycleValidated, bool RealDecoderValidated, bool RealProfileValidated, bool ProductionReleased,
    string Evidence, string Limitation)
{
    public bool PrivilegeContractRealValidated { get; init; }
}
internal sealed record GuardianCustomTracePlan(IReadOnlyList<string> Selected, IReadOnlyList<string> Effective,
    int Seconds, int EtlMiB, long SoftStopBytes, int DecoderBytes, string Limitation)
{
    public string Privacy => GuardianTraceProfiles.Get("custom").Privacy;
}

internal static class GuardianTraceProfiles
{
    internal const string Matrix03Sha256 = "38311E98695C143524043B7A3F9A40D0F2741E329D7199CAFE254BAD5B900112";
    internal static GuardianTraceQualification Qualification(string id)
    {
        _ = Get(id);
        var validated = id is "process" or "io" or "crash" or "network" or "startup";
        return new(true, true, true, validated || id == "custom", validated, false,
            validated ? "guardian-etw-admin-matrix-03.json / SHA256 " + Matrix03Sha256 :
            id == "cpu" ? "guardian-etw-cpu-admin-01.json / SHA256 B15D85F7E2866F5219007265CF03A66DFC083A3D45C656D168293A3C1BB0F134; " +
                "guardian-etw-cpu-admin-04.json / SHA-256 871D3E404D77C619DDBFF5B6F8A02D79E4F5122C30B7AE3B112F15BD3B8FC460" :
            "guardian-etw-custom-admin-01.json / SHA256 E2E2505C544D3FD56947C9920DB887B659DE039F2D511E1ED128967400A120E2",
            validated ? "Kurzer eigener Workload; keine Vollständigkeits-, Langzeit- oder Produktfreigabe." :
            id == "cpu" ? "CPU-admin-04: Decoder-, Cleanup- und WarmPlateau-Prüfungen bestanden; NoMalformedFields scheiterte mit 163/190/168 Fällen. Die Ursache der ThreadId- und fehlenden Provider.Guid-Gruppen ist aus den begrenzten Metadaten nicht entscheidbar. CPU-Profil nicht abgenommen." :
            "Begrenzter Custom-Inhalt real bestanden, Ressourcen-Einzelmessung nicht bestanden; kalte/warme Wiederholung erforderlich.")
        { PrivilegeContractRealValidated = id == "cpu" };
    }
    internal static GuardianCustomTracePlan CustomPlan(IReadOnlyList<string> selected)
    {
        if (selected.Count is < 1 or > 5 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Count ||
            selected.Any(id => !Providers.Any(p => p.Id == id)))
            throw new ArgumentException("Custom benötigt eindeutige feste Allowlist-Bausteine.");
        if (selected.Count(id => id != "process") > 2)
            throw new ArgumentException("Custom erlaubt höchstens zwei schwere Quellen neben dem explizit angezeigten Prozesskontext.");
        var effective = new[] { "process" }.Concat(selected.Where(id => id != "process")).ToArray();
        return new(selected.ToArray(), effective, 1, 8, 524288, GuardianTraceDecoder.MaximumXmlBytes,
            "Prozesskontext immer enthalten. Höchstens eine Sekunde; kontrollierter Softstop ab512KiB/nach500ms. " +
            "ETL-Größe ist keine sichere EVTX-Prognose; 64MiB je Konverterdatei/30s insgesamt bleiben harte Abbruchgates. Keine Vollständigkeit.");
    }
    internal sealed record ProfilePrivilege(bool? Present, bool? Enabled, uint NativeCode);
    internal static ProfilePrivilege ReadProfilePrivilege()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!LookupPrivilegeValue(null, "SeSystemProfilePrivilege", out var luid))
            return new(null, null, (uint)Marshal.GetLastWin32Error());
        GetTokenInformation(identity.AccessToken, 3, IntPtr.Zero, 0, out var length);
        var error = Marshal.GetLastWin32Error();
        if (error != 122) return new(null, null, (uint)error);
        if (length is < 4 or > 65536) throw new InvalidOperationException("Ungültige TokenPrivileges-Länge.");
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(identity.AccessToken, 3, buffer, length, out _))
                return new(null, null, (uint)Marshal.GetLastWin32Error());
            var count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (length - 4) / 12) throw new InvalidOperationException("Ungültige TokenPrivileges-Anzahl.");
            for (var index = 0; index < count; index++)
            {
                var entry = buffer + 4 + index * 12;
                if (Marshal.ReadInt64(entry) == luid) return new(true, (Marshal.ReadInt32(entry, 8) & 2) != 0, 0);
            }
            return new(false, false, 0);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token, int informationClass,
        IntPtr information, int length, out int returnedLength);
    internal static readonly IReadOnlyList<GuardianTraceProvider> Providers = Array.AsReadOnly<GuardianTraceProvider>(
    [
        new("process", GuardianTrace.ProcessProvider, 0x70, 4, false,
            "Prozessstart/-ende, Threadstart/-ende, Imageload/-unload",
            "Nur beobachtetes Aufnahmeintervall; PID allein liefert keine dauerhafte Instanzidentität",
            "Lokales Manifest Microsoft-Windows-Kernel-Process; https://learn.microsoft.com/windows/win32/etw/about-event-tracing",
            [GuardianTrace.ProcessProvider]),
        new("cpu", new("bfeb0324-1cee-496f-a409-2ac2b48a6322"), 1, 5, true,
            "System Profile / SYSTEM_PROFILE_KW_GENERAL: statistische CPU-Samples",
            "Ab Windows Build 20348; SeSystemProfilePrivilege kann zusätzlich nötig sein. Kein vollständiger CPU-Zähler, keine Stack-/Symbolanalyse",
            "https://learn.microsoft.com/windows/win32/etw/system-providers ; https://learn.microsoft.com/windows/win32/etw/perfinfo",
            [new("bfeb0324-1cee-496f-a409-2ac2b48a6322"), new("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc")]),
        new("file", new("edd08927-9cc4-4e65-b970-c2560fb5c289"), 0x330, 4, false,
            "Datei-I/O Metadaten: Filename/FileIO/Read/Write; keine Dateiinhalte",
            "Logische Dateioperationen sind keine physische Geräteaktivität; fehlende Thread-/Prozessidentität bleibt unbekannt",
            "Lokales Manifest Microsoft-Windows-Kernel-File",
            [new("edd08927-9cc4-4e65-b970-c2560fb5c289")]),
        new("disk", new("c7bde69a-e1e0-4177-b6ef-283ad1525271"), 0x8000000000000000, 4, false,
            "Kernel-Disk I/O Ereignismetadaten",
            "Geräteaktivität ist nicht automatisch einer Datei oder Prozessinstanz zugeordnet",
            "Lokales Manifest Microsoft-Windows-Kernel-Disk",
            [new("c7bde69a-e1e0-4177-b6ef-283ad1525271")]),
        new("network", new("7dd42a49-5329-4832-8dfd-43d979153a88"), 0x8000000000000000, 4, false,
            "Kernel-Network Verbindungs-/Transfermetadaten, Endpoints und Byteangaben soweit geliefert",
            "Keine Paketinhalte; keine Adapterbyteverteilung. Header-PID ohne bestätigte Erstellungszeit bleibt unbestätigt",
            "Lokales Manifest Microsoft-Windows-Kernel-Network",
            [new("7dd42a49-5329-4832-8dfd-43d979153a88")])
    ]);

    internal static readonly IReadOnlyList<GuardianTraceProfile> All = Array.AsReadOnly<GuardianTraceProfile>(
    [
        new("process", "Prozess / Thread / Image", "Beobachtete Lebenszyklen mit providerbasierter Identität", ["process"]),
        new("cpu", "CPU / Performance", "Begrenzte statistische Samples und Prozess-/Threadkontext; keine Vollerfassung", ["process", "cpu"]),
        new("io", "Datei- und Datenträger-I/O", "Getrennte logische Dateioperationen und Datenträgerereignisse", ["process", "file", "disk"]),
        new("crash", "Hang / Crash — Kontext", "Manueller Trace für einen bestehenden Fall; Zeitnähe beweist keine Ursache", ["process"]),
        new("network", "Netzwerk — Ereignismetadaten", "Verbindungen und Transfers soweit der Provider sie liefert; kein Paketmitschnitt", ["process", "network"]),
        new("startup", "Startup — beobachtetes Intervall", "Nur Starts nach tatsächlichem Tracebeginn; kein vollständiger Boot-/Loginabschluss", ["process"], 10),
        new("custom", "Benutzerdefinierte geprüfte Bausteine", "Begrenzte feste Bausteine mit angezeigtem Prozesskontext; keine freie GUID-/Keywordeingabe", ["process"], 1, 1)
    ]);

    internal static GuardianTraceProfile Get(string id) => All.SingleOrDefault(p => p.Id == id) ??
        throw new ArgumentException("Unbekanntes Traceprofil.");
    internal static IReadOnlyList<GuardianTraceProvider> Resolve(GuardianTraceProfile profile, IReadOnlyList<string>? components = null)
    {
        if (!ReferenceEquals(profile, Get(profile.Id))) throw new ArgumentException("Nur unveränderte Katalogprofile sind erlaubt.");
        var ids = components ?? profile.Components;
        if (ids.Count is < 1 or > 5 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
            profile.Id != "custom" && !ids.SequenceEqual(profile.Components))
            throw new ArgumentException("Unzulässige Tracebausteine.");
        return ids.Select(id => Providers.SingleOrDefault(p => p.Id == id) ??
            throw new ArgumentException("Providerbaustein nicht in der Allowlist.")).ToArray();
    }
    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    internal static void RequireAdministrator()
    {
        if (!IsAdministrator()) throw new UnauthorizedAccessException(
            "Deep Trace benötigt einen bewusst als Administrator gestarteten Testhost/AETHER-Prozess. Keine Sitzung gestartet; AETHER erhöht sich nicht selbst.");
    }
}
