using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AetherOperations;
using SystemCockpit.Services;

namespace SystemCockpit;

public partial class ControlCenterWindow
{
    internal sealed record ReviewedUpdate(Guid UpdateId, int Revision, string Title, string EulaText,
        string EulaHash, int Type, string? DriverManufacturer)
    {
        public string Display => $"{Title} | {(Type == 2 ? "TREIBER" : "SOFTWARE")} | {UpdateId:D}:{Revision}";
    }

    private ReviewedUpdate[] _reviewedUpdates = [];
    private DateTimeOffset? _updateSearchAt;
    private DispatcherTimer? _updatePlanExpiry;

    internal static ReviewedUpdate[] ParseReviewedUpdates(ManagedActionResult result)
    {
        try { return ParseCompleteReviewedUpdates(result); }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException or InvalidOperationException or JsonException)
        {
            throw new InvalidDataException("Ungültiges oder unvollständiges Updatesuch-JSON; erneut suchen.", ex);
        }
    }

    private static ReviewedUpdate[] ParseCompleteReviewedUpdates(ManagedActionResult result)
    {
        if (result.ExitCode != 0 || result.Truncated)
            throw new InvalidDataException("Nur vollständige erfolgreiche Updatesuchen erlauben eine Auswahl.");
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        if (root.GetProperty("resultCode").GetInt32() != 2 || root.GetProperty("serverSelection").GetInt32() != 0)
            throw new InvalidDataException("Updatesuche ist nicht vollständig oder nutzt nicht die konfigurierte Quelle.");
        var offers = root.GetProperty("updates").EnumerateArray().Select(item =>
        {
            var id = item.GetProperty("updateId").GetGuid();
            var revision = item.GetProperty("revision").GetInt32();
            var text = item.GetProperty("eulaText").GetString() ?? "";
            var hash = item.GetProperty("eulaHash").GetString() ?? "";
            if (id == Guid.Empty || revision < 0 ||
                hash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))))
                throw new InvalidDataException("Updateidentität oder exakter EULA-Hash ungültig.");
            var type = item.GetProperty("type").GetInt32();
            if (type is not (1 or 2)) throw new InvalidDataException("Unbekannter Updateangebotstyp.");
            return new ReviewedUpdate(id, revision, item.GetProperty("title").GetString() ?? "",
                text, hash, type,
                item.TryGetProperty("driverManufacturer", out var manufacturer) ? manufacturer.GetString() : null);
        }).ToArray();
        if (offers.Select(o => (o.UpdateId, o.Revision)).Distinct().Count() != offers.Length)
            throw new InvalidDataException("Mehrdeutige Updateidentitäten; erneut suchen.");
        return offers;
    }

    internal static bool UpdatePlanFresh(DateTimeOffset? searchedAt, DateTimeOffset now) =>
        searchedAt is { } time && now >= time && now - time < TimeSpan.FromHours(1);

    private void ClearUpdatePlan(string reason)
    {
        _reviewedUpdates = [];
        _updateSearchAt = null;
        _updatePlanExpiry?.Stop();
        ReviewedUpdates.ItemsSource = _reviewedUpdates;
        UpdateEulaAcceptance.IsChecked = false;
        UpdateOfferDetails.Text = "";
        UpdatePlanStatus.Text = reason;
    }

    private void SaveUpdatePlan(ManagedActionResult result)
    {
        _reviewedUpdates = ParseReviewedUpdates(result);
        _updateSearchAt = DateTimeOffset.UtcNow;
        ReviewedUpdates.ItemsSource = _reviewedUpdates;
        ReviewedUpdates.SelectedIndex = _reviewedUpdates.Length > 0 ? 0 : -1;
        UpdatePlanStatus.Text = $"{_reviewedUpdates.Length} vollständige Angebote / konfigurierte Windows-Update-/WSUS-Quelle. Auswahl gültig bis {_updateSearchAt.Value.AddHours(1).ToLocalTime():HH:mm:ss}.";
        if (_updatePlanExpiry is null)
        {
            _updatePlanExpiry = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _updatePlanExpiry.Tick += (_, _) => ClearUpdatePlan("Auswahl nach einer Stunde abgelaufen; erneut suchen.");
            Closed += (_, _) => _updatePlanExpiry.Stop();
        }
        _updatePlanExpiry.Start();
    }

    private void ReviewedUpdateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (UpdateEulaAcceptance is null) return;
        UpdateEulaAcceptance.IsChecked = false;
        if (!UpdatePlanFresh(_updateSearchAt, DateTimeOffset.UtcNow) && _updateSearchAt is not null)
        {
            ClearUpdatePlan("Auswahl abgelaufen; erneut suchen.");
            return;
        }
        if (ReviewedUpdates.SelectedItem is not ReviewedUpdate offer) { UpdateOfferDetails.Text = ""; return; }
        UpdateOfferDetails.Text = $"{offer.Title}\nGUID: {offer.UpdateId:D}\nRevision: {offer.Revision}\nTyp: {(offer.Type == 2 ? "Treiber" : "Software")}" +
            $"\nHersteller: {offer.DriverManufacturer ?? "nicht angegeben"}\nEULA SHA256: {offer.EulaHash}\n\nEXAKTER LIZENZTEXT:\n{offer.EulaText}";
    }

    private async void TypedUpdateInstallClicked(object sender, RoutedEventArgs e)
    {
        if (!_model.Settings.ExpertMode)
        {
            Status.Text = "Nicht gestartet: Updateinstallation verlangt Expertenmodus.";
            return;
        }
        if (!UpdatePlanFresh(_updateSearchAt, DateTimeOffset.UtcNow))
        {
            ClearUpdatePlan("Keine aktuelle vollständige Suche; zuerst Angebote suchen.");
            Status.Text = UpdatePlanStatus.Text;
            return;
        }
        if (ReviewedUpdates.SelectedItem is not ReviewedUpdate offer ||
            !_reviewedUpdates.Contains(offer) || UpdateEulaAcceptance.IsChecked != true)
        {
            Status.Text = "Genau ein Angebot auswählen und dessen angezeigten Lizenztext ausdrücklich akzeptieren.";
            return;
        }
        var selection = new WindowsUpdateSelection(offer.UpdateId, offer.Revision, offer.EulaHash, true);
        await RunAsync("Ein geprüftes Windows-/Treiberupdate installieren", async token =>
        {
            if (!_model.Settings.ExpertMode || !UpdatePlanFresh(_updateSearchAt, DateTimeOffset.UtcNow))
                throw new InvalidOperationException("Auswahl/Expertenmodus nicht mehr gültig; erneut prüfen.");
            ClearUpdatePlan("Freigabe verbraucht; für weitere Updates erneut suchen.");
            var result = await ManagedActionRunner.RunUpdateAsync(selection, token);
            if (result.ExitCode != 0 || result.Truncated)
            {
                AppendTerminal(result.Output);
                throw new InvalidOperationException($"Updateinstallation nicht vollständig erfolgreich (Exit {result.ExitCode}, gekürzt={result.Truncated}); Teilaktionen können bestehen bleiben.");
            }
            return result.Output;
        }, $"ÄNDERND: genau ein Update; Systemdateien/Treiber und EULA-Zustand ändern sich. Vorher sichern, laufende Apps schließen. " +
            $"Abbruch ist kein Rollback; Windows kann einen Neustart benötigen, AETHER startet nicht neu.\n\n" +
            $"QUELLE: konfigurierte Windows Update/WSUS\n{offer.Title}\nGUID: {offer.UpdateId:D}\nREVISION: {offer.Revision}\n" +
            $"EULA SHA256: {offer.EulaHash}\n\nAkzeptierter exakter Lizenztext:\n{offer.EulaText}",
            admin: true, detailedConfirmation: true);
    }

    private async void TypedUpdateSearchClicked(object sender, RoutedEventArgs e) =>
        await SearchTypedUpdatesAsync();

    private Task SearchTypedUpdatesAsync() => RunAsync("Windows-/Treiberupdates suchen", async token =>
    {
        ClearUpdatePlan("Neue Suche gestartet; vorherige Auswahl verworfen.");
        var result = await ManagedActionRunner.RunAsync(ManagedActionCatalog.Get("windows-update-search"), token);
        SaveUpdatePlan(result);
        return result.Output;
    }, "NETZWERK: vollständige WUA-Suche bei konfigurierter Windows-Update-/WSUS-Quelle. Keine Installation/Lizenzannahme. " +
        "Vorherige Auswahl wird verworfen; gekürzte/fehlgeschlagene Ergebnisse erlauben keine Installation.", admin: true);

    private async void ManagedActionClicked(object sender, RoutedEventArgs e)
    {
        if (ManagedActions.SelectedItem is not ManagedActionTool tool) return;
        var action = tool.Action;
        if (action.Id == "windows-update-search") { await SearchTypedUpdatesAsync(); return; }
        if (action.Id == "windows-update-install")
        {
            if (!UpdatePlanFresh(_updateSearchAt, DateTimeOffset.UtcNow))
                await SearchTypedUpdatesAsync();
            Status.Text = "Installation nur unten: genau ein Angebot und EULA prüfen, Expertenmodus aktivieren, einzeln freigeben.";
            ReviewedUpdates.BringIntoView();
            return;
        }
        await RunAsync(action.Name, async token =>
        {
            var result = await ManagedActionRunner.RunAsync(action, token,
                action.Id == "inventory" ? ManagedActionRunner.MaximumOutputCharacters : ManagedActionRunner.DefaultOutputCharacters);
            var text = result.Output + (result.Truncated ? "\n[AUSGABE GEKÜRZT – kein vollständiger Befund/JSON]" : "");
            if (result.ExitCode != 0)
            {
                AppendTerminal(text);
                throw new InvalidOperationException($"Windows-Aktion ExitCode {result.ExitCode}; Ausgabe beachten.");
            }
            return text;
        }, warning: tool.Risk + "\n\nVoraussetzungen: " + string.Join(", ", action.RequiredCommands) +
            "\n\nFester Befehl:\n" + action.Script,
            admin: true, detailedConfirmation: action.ChangesSystem);
    }

    private async void UpdateSettingsClicked(object sender, RoutedEventArgs e) =>
        await RunAsync("Windows Update / manuelle Auswahl", _ =>
        {
            Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
            return Task.FromResult("Windows Update geöffnet. Software-/Treiberangebote und Identitäten dort prüfen und einzeln auswählen. " +
                "AETHER installiert nichts, nimmt keine EULA an und startet Windows nicht neu. " +
                "Für geprüfte exakte Updatepläne bleibt der vorhandene Operations-Wartungsplan verfügbar.");
        }, "Lokale Einstellungen öffnen. Jede dort genehmigte Installation kann Windows und Treiber ändern; Windows kann einen Neustart verlangen. " +
            "Keine remote-/Session-0-Aktion.", detailedConfirmation: true);

    private async void OptionalToolAvailabilityClicked(object sender, RoutedEventArgs e)
    {
        if (OptionalTools.SelectedItem is not OptionalMicrosoftTool tool) return;
        await RunAsync(tool.Name + " / Verfügbarkeit", _ =>
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.SystemDirectory, tool.Executable),
                Path.Combine(AppContext.BaseDirectory, "tools", tool.Executable)
            };
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                if (Path.IsPathFullyQualified(folder.Trim('"')))
                    candidates.Add(Path.Combine(folder.Trim('"'), tool.Executable));
            var installed = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
            return Task.FromResult((installed.Length == 0 ? "Nicht in System/tools/PATH gefunden (keine vollständige Installationssuche)." :
                "Datei gefunden (nicht ausgeführt; Signatur und Version separat prüfen):\n" + string.Join("\n", installed)) +
                "\nRISIKO: " + tool.Risk + "\nOFFIZIELL: " + tool.DownloadUrl +
                "\nKeine Installation, kein Toolstart und keine automatische Lizenzannahme.");
        });
    }

    private async void OptionalToolDownloadClicked(object sender, RoutedEventArgs e)
    {
        if (OptionalTools.SelectedItem is not OptionalMicrosoftTool tool) return;
        await RunAsync(tool.Name + " / offizielle Microsoft-Seite", _ =>
        {
            Process.Start(new ProcessStartInfo(tool.DownloadUrl) { UseShellExecute = true });
            return Task.FromResult("Nur offizielle Informations-/Downloadseite geöffnet; Download, Installation, EULA und Toolstart bleiben manuell.");
        }, "Browserkontakt zu Microsoft:\n" + tool.DownloadUrl + "\n" + tool.Risk +
            "\nAETHER lädt nichts herunter und akzeptiert keine Lizenz.");
    }
}
