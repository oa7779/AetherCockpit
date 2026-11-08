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
        public string Display => $"{Title} | {(Type == 2 ? DesktopUiText.Get("Update.Driver") : "SOFTWARE")} | {UpdateId:D}:{Revision}";
    }

    private ReviewedUpdate[] _reviewedUpdates = [];
    private DateTimeOffset? _updateSearchAt;
    private DispatcherTimer? _updatePlanExpiry;

    internal static ReviewedUpdate[] ParseReviewedUpdates(ManagedActionResult result)
    {
        try { return ParseCompleteReviewedUpdates(result); }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException or InvalidOperationException or JsonException)
        {
            throw new InvalidDataException(DesktopUiText.Get("Update.InvalidJson"), ex);
        }
    }

    private static ReviewedUpdate[] ParseCompleteReviewedUpdates(ManagedActionResult result)
    {
        if (result.ExitCode != 0 || result.Truncated)
            throw new InvalidDataException(DesktopUiText.Get("Update.Incomplete"));
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        if (root.GetProperty("resultCode").GetInt32() != 2 || root.GetProperty("serverSelection").GetInt32() != 0)
            throw new InvalidDataException(DesktopUiText.Get("Update.InvalidSource"));
        var offers = root.GetProperty("updates").EnumerateArray().Select(item =>
        {
            var id = item.GetProperty("updateId").GetGuid();
            var revision = item.GetProperty("revision").GetInt32();
            var text = item.GetProperty("eulaText").GetString() ?? "";
            var hash = item.GetProperty("eulaHash").GetString() ?? "";
            if (id == Guid.Empty || revision < 0 ||
                hash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))))
                throw new InvalidDataException(DesktopUiText.Get("Update.InvalidIdentity"));
            var type = item.GetProperty("type").GetInt32();
            if (type is not (1 or 2)) throw new InvalidDataException(DesktopUiText.Get("Update.InvalidType"));
            return new ReviewedUpdate(id, revision, item.GetProperty("title").GetString() ?? "",
                text, hash, type,
                item.TryGetProperty("driverManufacturer", out var manufacturer) ? manufacturer.GetString() : null);
        }).ToArray();
        if (offers.Select(o => (o.UpdateId, o.Revision)).Distinct().Count() != offers.Length)
            throw new InvalidDataException(DesktopUiText.Get("Update.Ambiguous"));
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
        UpdatePlanStatus.Text = DesktopUiText.Format("Update.PlanStatus", _reviewedUpdates.Length, _updateSearchAt.Value.AddHours(1).ToLocalTime());
        if (_updatePlanExpiry is null)
        {
            _updatePlanExpiry = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _updatePlanExpiry.Tick += (_, _) => ClearUpdatePlan(DesktopUiText.Get("Update.ExpiredHour"));
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
            ClearUpdatePlan(DesktopUiText.Get("Update.Expired"));
            return;
        }
        if (ReviewedUpdates.SelectedItem is not ReviewedUpdate offer) { UpdateOfferDetails.Text = ""; return; }
        UpdateOfferDetails.Text = DesktopUiText.Format("Update.OfferDetail", offer.Title, offer.UpdateId, offer.Revision,
            offer.Type == 2 ? DesktopUiText.Get("Update.Driver") : "Software",
            offer.DriverManufacturer ?? DesktopUiText.Get("Update.NoManufacturer"), offer.EulaHash, offer.EulaText);
    }

    private async void TypedUpdateInstallClicked(object sender, RoutedEventArgs e)
    {
        if (!_model.Settings.ExpertMode)
        {
            Status.Text = DesktopUiText.Get("Update.ExpertRequired");
            return;
        }
        if (!UpdatePlanFresh(_updateSearchAt, DateTimeOffset.UtcNow))
        {
            ClearUpdatePlan(DesktopUiText.Get("Update.SearchFirst"));
            Status.Text = UpdatePlanStatus.Text;
            return;
        }
        if (ReviewedUpdates.SelectedItem is not ReviewedUpdate offer ||
            !_reviewedUpdates.Contains(offer) || UpdateEulaAcceptance.IsChecked != true)
        {
            Status.Text = DesktopUiText.Get("Update.AcceptExact");
            return;
        }
        var selection = new WindowsUpdateSelection(offer.UpdateId, offer.Revision, offer.EulaHash, true);
        await RunAsync(DesktopUiText.Get("Update.InstallTitle"), async token =>
        {
            if (!_model.Settings.ExpertMode || !UpdatePlanFresh(_updateSearchAt, DateTimeOffset.UtcNow))
                throw new InvalidOperationException(DesktopUiText.Get("Update.PlanInvalid"));
            ClearUpdatePlan(DesktopUiText.Get("Update.PlanConsumed"));
            var result = await ManagedActionRunner.RunUpdateAsync(selection, token);
            if (result.ExitCode != 0 || result.Truncated)
            {
                AppendTerminal(result.Output);
                throw new InvalidOperationException(DesktopUiText.Format("Update.InstallIncomplete", result.ExitCode, result.Truncated));
            }
            return result.Output;
        }, DesktopUiText.Format("Update.InstallConsent", offer.Title, offer.UpdateId, offer.Revision, offer.EulaHash, offer.EulaText),
            admin: true, detailedConfirmation: true);
    }

    private async void TypedUpdateSearchClicked(object sender, RoutedEventArgs e) =>
        await SearchTypedUpdatesAsync();

    private Task SearchTypedUpdatesAsync() => RunAsync(DesktopUiText.Get("Update.SearchTitle"), async token =>
    {
        ClearUpdatePlan(DesktopUiText.Get("Update.SearchStarted"));
        var result = await ManagedActionRunner.RunAsync(ManagedActionCatalog.Get("windows-update-search"), token);
        SaveUpdatePlan(result);
        return result.Output;
    }, DesktopUiText.Get("Update.SearchConsent"), admin: true);

    private async void ManagedActionClicked(object sender, RoutedEventArgs e)
    {
        if (ManagedActions.SelectedItem is not ManagedActionTool tool) return;
        var action = tool.Action;
        if (action.Id == "windows-update-search") { await SearchTypedUpdatesAsync(); return; }
        if (action.Id == "windows-update-install")
        {
            if (!UpdatePlanFresh(_updateSearchAt, DateTimeOffset.UtcNow))
                await SearchTypedUpdatesAsync();
            Status.Text = DesktopUiText.Get("Update.InstallHere");
            ReviewedUpdates.BringIntoView();
            return;
        }
        await RunAsync(action.Name, async token =>
        {
            var result = await ManagedActionRunner.RunAsync(action, token,
                action.Id == "inventory" ? ManagedActionRunner.MaximumOutputCharacters : ManagedActionRunner.DefaultOutputCharacters);
            var text = result.Output + (result.Truncated ? DesktopUiText.Get("Managed.Truncated") : "");
            if (result.ExitCode != 0)
            {
                AppendTerminal(text);
                throw new InvalidOperationException(DesktopUiText.Format("Managed.ExecutionFailed", result.ExitCode));
            }
            return text;
        }, warning: tool.Risk + DesktopUiText.Format("Managed.Prerequisites", string.Join(", ", action.RequiredCommands), action.Script),
            admin: true, detailedConfirmation: action.ChangesSystem);
    }

    private async void UpdateSettingsClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(DesktopUiText.Get("Update.SettingsTitle"), _ =>
        {
            Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
            return Task.FromResult(DesktopUiText.Get("Update.SettingsOpened"));
        }, DesktopUiText.Get("Update.SettingsConsent"), detailedConfirmation: true);

    private async void OptionalToolAvailabilityClicked(object sender, RoutedEventArgs e)
    {
        if (OptionalTools.SelectedItem is not OptionalMicrosoftTool tool) return;
        await RunAsync(DesktopUiText.Format("OptionalTool.AvailabilityTitle", tool.Name), _ =>
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
            return Task.FromResult((installed.Length == 0 ? DesktopUiText.Get("OptionalTool.NotFound") :
                DesktopUiText.Format("OptionalTool.Found", string.Join("\n", installed))) +
                DesktopUiText.Format("OptionalTool.AvailabilityNotice", tool.Risk, tool.DownloadUrl));
        });
    }

    private async void OptionalToolDownloadClicked(object sender, RoutedEventArgs e)
    {
        if (OptionalTools.SelectedItem is not OptionalMicrosoftTool tool) return;
        await RunAsync(DesktopUiText.Format("OptionalTool.WebTitle", tool.Name), _ =>
        {
            Process.Start(new ProcessStartInfo(tool.DownloadUrl) { UseShellExecute = true });
            return Task.FromResult(DesktopUiText.Get("OptionalTool.WebOpened"));
        }, DesktopUiText.Format("OptionalTool.WebConsent", tool.DownloadUrl, tool.Risk));
    }
}
