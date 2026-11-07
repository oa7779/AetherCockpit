using System.IO;
using System.Text.Json;
using SystemCockpit;
using SystemCockpit.Services;

internal static class SwtTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }

        // ---- Risikoanzeige: jede Katalogzeile traegt eine abgeleitete, konsistente Stufe ----
        Check(AdminCommandCatalog.All.All(c => c.RiskLevel is >= 0 and <= 2 && c.RiskBadge.StartsWith("RISIKO:")),
            "Jeder Befehl hat eine Risikostufe mit Badge");
        Check(AdminCommandCatalog.All.Single(c => c.Command == "sfc /scannow").RiskLevel == 2,
            "Reparaturbefehle sind als AENDERND eingestuft");
        Check(AdminCommandCatalog.All.Single(c => c.Command == "sfc /verifyonly").RiskLevel == 0,
            "Pruefbefehle sind als LESEND eingestuft");
        Check(AdminCommandCatalog.All.Single(c => c.Command == "Resolve-DnsName 'HOSTNAME'").RiskLevel == 1,
            "Befehle mit externem Kontakt sind als NETZWERK eingestuft");
        Check(AdminCommandCatalog.All.Where(c => c.RiskLevel == 0).All(c => c.Impact.StartsWith("LESEND")),
            "Risikostufe folgt exakt der Impact-Kennzeichnung");

        // ---- Kategorie-Leitfaden: jede real genutzte Kategorie hat gepflegte Hinweise ----
        var fallback = AdminCommandCatalog.Guidance("UNBEKANNTE-KATEGORIE");
        foreach (var category in AdminCommandCatalog.All.Select(c => c.Category).Distinct())
        {
            var guidance = AdminCommandCatalog.Guidance(category);
            Check(guidance != fallback && guidance.WhenUseful.Length > 20 && guidance.Caution.Length > 20,
                $"Kategorie '{category}' hat einen eigenen Sinnvoll-/Vorsicht-Leitfaden");
        }

        // ---- Einsteiger-/Expertenmodus: Filter und Persistenzformat ----
        var beginner = ControlCenterWindow.FilterCatalog("", expert: false);
        Check(beginner.Count > 20 && beginner.All(c => c.RiskLevel == 0),
            "Einsteiger sieht eine nutzbare, rein lesende Kernliste");
        Check(ControlCenterWindow.FilterCatalog("", expert: true).Count == AdminCommandCatalog.All.Count,
            "Experte sieht den vollen Katalog");
        Check(ControlCenterWindow.FilterCatalog("Stop-Process", expert: false).Count == 0 &&
            ControlCenterWindow.FilterCatalog("Stop-Process", expert: true).Count >= 1,
            "Suche hebt die Einsteiger-Sperre nicht auf");
        var roundtrip = JsonSerializer.Deserialize<CockpitSettings>(
            JsonSerializer.Serialize(new CockpitSettings
            {
                ExpertMode = false,
                OverlayMonitorDeviceName = @"\\.\DISPLAY2",
                OverlayDockSide = "left",
                OverlayVerticalOffset = 0.65,
                SidebarMonitorDeviceName = @"\\.\DISPLAY3",
                SidebarDockSide = "left",
                SidebarPinned = false,
                SidebarAutoHide = true,
                SidebarHorizontalOffset = 0.32,
                SidebarShortcuts = [new("Test-App", Path.GetFullPath(Environment.ProcessPath!), "App")]
            }))!;
        Check(!roundtrip.ExpertMode && JsonSerializer.Deserialize<CockpitSettings>("{}")!.ExpertMode,
            "ExpertMode wird gespeichert; Altbestand ohne Feld bleibt Experte");
        Check(roundtrip.OverlayMonitorDeviceName == @"\\.\DISPLAY2" && roundtrip.OverlayDockSide == "left" &&
            roundtrip.OverlayVerticalOffset == 0.65 &&
            JsonSerializer.Deserialize<CockpitSettings>("{}")!.OverlayDockSide == "right",
            "Overlay-Monitor, Dockseite und Hoehe werden gespeichert; Altbestand startet rechts");
        Check(roundtrip.SidebarMonitorDeviceName == @"\\.\DISPLAY3" && roundtrip.SidebarDockSide == "left" &&
            !roundtrip.SidebarPinned && roundtrip.SidebarAutoHide == true &&
            roundtrip.SidebarHorizontalOffset == 0.32 && roundtrip.SidebarShortcuts.Single().Name == "Test-App",
            "xBar-Monitor, Dock, Float/Auto-hide und eigene Verknuepfungen werden serialisiert");
        var oldSidebarSettings = JsonSerializer.Deserialize<CockpitSettings>("{}")!;
        Check(oldSidebarSettings.SidebarPinned && oldSidebarSettings.SidebarWidgetCpu &&
            oldSidebarSettings.SidebarShortcuts.Count == 0,
            "Altbestand erhaelt angeheftete xBar, Standardwidgets und leere Shortcuts");

        // ---- Skins: Katalog, Validierung und vollstaendige Brush-Sets ----
        Check(SkinCatalog.All.Count == 4 && SkinCatalog.All[0].Id == "retro-neon" &&
            SkinCatalog.All.Select(s => s.Id).Distinct().Count() == 4,
            "Vier Skins mit Retro-Neon als Original an erster Stelle");
        Check(SkinCatalog.Valid("vista-aero") && !SkinCatalog.Valid("hellgruen"),
            "Skin-Validierung kennt nur die definierten Skins");
        try { new CockpitSettings { Skin = "gibtesnicht" }.Validate(); throw new InvalidOperationException("Skin-Validierung fehlt"); }
        catch (ArgumentException) { count++; }
        Check(JsonSerializer.Deserialize<CockpitSettings>("{}")!.Skin == "retro-neon",
            "Altbestand ohne Skin-Feld bleibt Retro-Neon");
        foreach (var skin in SkinCatalog.All.Where(s => s.File is not null))
        {
            var dictionary = new System.Windows.ResourceDictionary { Source = SkinCatalog.Source(skin.Id) };
            foreach (var key in SkinCatalog.RequiredKeys)
                Check(dictionary[key] is System.Windows.Media.Brush, $"Skin '{skin.Id}' definiert Brush '{key}'");
        }
        foreach (var key in SkinCatalog.RequiredKeys)
            Check(System.Windows.Application.Current?.Resources[key] is System.Windows.Media.Brush,
                $"Retro-Neon stellt '{key}' als App-Ressource bereit");

        // ---- Live-Skinwechsel: sofortige Wirkung, kein Stapeln, Original wiederherstellbar ----
        var application = System.Windows.Application.Current!;
        var retroBackdrop = application.Resources["WindowBackdrop"];
        SkinCatalog.Apply(application, "vista-aero");
        Check(!ReferenceEquals(application.Resources["WindowBackdrop"], retroBackdrop),
            "Skin ueberlagert die Theme-Brushes sofort");
        SkinCatalog.Apply(application, "candy-gloss");
        Check(application.Resources.MergedDictionaries.Count(d =>
                d.Source?.OriginalString.Contains("/Skins/", StringComparison.OrdinalIgnoreCase) == true) == 1,
            "Skinwechsel ersetzt die vorherige Ueberlagerung statt zu stapeln");
        var skinHost = new System.Windows.Window { Width = 120, Height = 80, ShowInTaskbar = false };
        try
        {
            var probe = new System.Windows.Controls.Button();
            skinHost.Content = probe;
            probe.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, "GlossButton");
            var candyBrush = probe.Background;
            SkinCatalog.Apply(application, "retro-neon");
            Check(!ReferenceEquals(probe.Background, candyBrush) &&
                ReferenceEquals(application.Resources["WindowBackdrop"], retroBackdrop),
                "Zurueck zu Retro-Neon wirkt sofort auf offene Fenster und stellt das Original wieder her");
        }
        finally
        {
            skinHost.Close();
            SkinCatalog.Apply(application, "retro-neon");
        }

        // ---- Hilfe-Center: Abdeckung, Suche, Qualitaet ----
        Check(HelpCatalog.All.Count >= 18 && HelpCatalog.All.Select(t => t.Title).Distinct().Count() == HelpCatalog.All.Count,
            "Hilfe hat ausreichend eindeutige Themen");
        foreach (var module in new[] { "Cockpit", "Taskmanager", "Sensoren", "Laufwerke", "Messauswertung", "Diagnose",
                     "Sicherheit", "Cleaner", "Ereignisse", "Superadmin", "Operations", "Pakete", "Netzwerk", "Spuren",
                     "Schluessel", "Konsole" })
            Check(HelpCatalog.All.Any(t => t.Module == module), $"Hilfethema fuer Modul '{module}' vorhanden");
        Check(HelpCatalog.All.All(t => t.Body.Length > 100 && t.Keywords.Length > 10),
            "Jedes Hilfethema hat ausfuehrlichen Inhalt und Suchbegriffe");
        Check(HelpCatalog.Search("unigetui") is { Count: > 0 } packageTopics && packageTopics.All(t => t.Module == "Pakete"),
            "Suche findet Paketthemen ueber Stichwoerter");
        Check(HelpCatalog.Search("GAMING").Count >= 2 && HelpCatalog.Search("qqqqq-nichts").Count == 0,
            "Suche ist case-insensitiv und erfindet keine Treffer");

        // ---- Diagnose-Export: Bericht und Schwaerzung ----
        var model = new CockpitModel();
        model.ApplyProbe(new("Test-CPU", "Test-GPU", "Board", "RAM", "Windows 11",
            [new("Secure Boot", "On", "ok", $"Host {Environment.MachineName}")],
            [new("Disk", "OK", "ok", "WD Blue")]));
        var report = DiagnosticExport.Build(model);
        Check(report.Contains("== WINDOWS-PRUEFUNGEN ==") && report.Contains("Secure Boot") &&
            report.Contains("== SENSOREN") && report.Contains(Environment.MachineName),
            "Bericht enthaelt Abschnitte und echte Modellwerte");
        var sample = $"PC {Environment.MachineName} Nutzer {Environment.UserName} " +
            $@"Pfad C:\Users\{Environment.UserName}\Desktop IP 192.168.178.42 MAC AA:BB:CC:DD:EE:FF Ende";
        var redacted = DiagnosticExport.Redact(sample);
        Check(!redacted.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
            !redacted.Contains(@"\Users\" + Environment.UserName, StringComparison.OrdinalIgnoreCase) &&
            !redacted.Contains("192.168.178.42") && !redacted.Contains("AA:BB:CC:DD:EE:FF"),
            "Schwaerzung entfernt Rechner, Benutzer, Pfad, IPv4 und MAC");
        Check(redacted.Contains("[RECHNER]") && redacted.Contains("[BENUTZER]") &&
            redacted.Contains("[IPV4]") && redacted.Contains("[MAC]") && redacted.EndsWith("Ende"),
            "Platzhalter ersetzen die Angaben, uebriger Text bleibt erhalten");
        Check(DiagnosticExport.Redact(DiagnosticExport.Build(model))
                .Contains("Rechner: [RECHNER] / Benutzer: [BENUTZER]"),
            "Geschwaerzter Bericht traegt keine Identitaet im Kopf");

        // ---- Navigationsverlauf: Browser-Semantik ohne UI ----
        var trail = new NavigationTrail();
        Check(!trail.CanBack && !trail.CanForward && trail.Current is null, "Leerer Verlauf hat keine Richtungen");
        trail.Visit("Cockpit"); trail.Visit("Superadmin"); trail.Visit("Superadmin"); trail.Visit("Cleaner");
        Check(trail.Current == "Cleaner" && trail.CanBack && !trail.CanForward, "Doppelbesuche werden nicht doppelt gezaehlt");
        Check(trail.Back() == "Superadmin" && trail.Back() == "Cockpit" && !trail.CanBack && trail.CanForward,
            "Zurueck laeuft bis zur Hauptebene");
        Check(trail.Forward() == "Superadmin", "Vor wiederholt den Weg");
        trail.Visit("Hilfe");
        Check(!trail.CanForward && trail.Current == "Hilfe" && trail.Back() == "Superadmin",
            "Neuer Besuch nach Zurueck verwirft den Vorwaerts-Zweig");
        var bounded = new NavigationTrail();
        for (var i = 0; i < 150; i++) bounded.Visit("Seite" + i);
        var steps = 0;
        while (bounded.Back() is not null) steps++;
        Check(steps == NavigationTrail.MaxEntries - 1, "Verlauf ist auf 100 Eintraege begrenzt");

        // ---- Export-Vorschau: Schwaerzung ist Standard und live umschaltbar ----
        var preview = new ExportPreviewWindow("Rechner " + Environment.MachineName + " / 10.0.0.1");
        try
        {
            Check(preview.RedactToggle.IsChecked == true && preview.Preview.Text.Contains("[RECHNER]") &&
                preview.Preview.Text.Contains("[IPV4]"), "Vorschau startet geschwaerzt");
            preview.RedactToggle.IsChecked = false;
            Check(preview.Preview.Text.Contains(Environment.MachineName) && preview.Preview.Text.Contains("10.0.0.1"),
                "Abschalten zeigt den Rohtext fuer die eigene Pruefung");
        }
        finally { preview.Close(); }
        return count;
    }
}
