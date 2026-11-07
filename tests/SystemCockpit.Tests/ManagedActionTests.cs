using AetherOperations;
using SystemCockpit.Services;

internal static class ManagedActionTests
{
    private static string BuildUpdateScript(WindowsUpdateSelection selection) =>
        (string)typeof(ManagedActionRunner).GetMethod("BuildUpdateScript",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [selection])!;

    public static string WindowsUpdateFixtureScript => "$installCode=" + AdminManagement.Quote(
        BuildUpdateScript(new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 7,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("Reviewed EULA ä"))), true))
            .Replace("exit 1", "throw 'fixture-result-failure'")) + "\n$searchCode=" +
        AdminManagement.Quote(ManagedActionCatalog.Get("windows-update-search").Script) + "\n" + """
        $ErrorActionPreference='Stop'
        function New-Object {
            param($ComObject)
            if($ComObject -eq 'Microsoft.Update.Session'){return $global:session}
            if($ComObject -eq 'Microsoft.Update.UpdateColl'){return ,([Collections.ArrayList]::new())}
            throw 'Unexpected fixture COM object'
        }
        foreach($case in @('success','driver','changed','missing','revision','searchPartial','downloadPartial','downloadIndividual','downloadHResult','installPartial','installIndividual','installHResult','interactive','pendingReboot')){
            $global:accepted=0; $global:downloads=0; $global:installs=0
            $global:update=[pscustomobject]@{Identity=[pscustomobject]@{UpdateID='11111111-1111-1111-1111-111111111111';RevisionNumber=$(if($case -eq 'revision'){8}else{7})};Title='Fixture offer';Type=$(if($case -eq 'driver'){2}else{1});EulaText=$(if($case -eq 'changed'){'Changed EULA'}else{'Reviewed EULA ä'});EulaAccepted=$false;IsDownloaded=$false;InstallationBehavior=[pscustomobject]@{CanRequestUserInput=($case -eq 'interactive');RebootBehavior=1};DriverManufacturer='Fixture manufacturer';DriverModel='Fixture model';DriverVerDate='2026-01-01';DriverHardwareID='PCI\FIXTURE'}
            $global:update | Add-Member ScriptMethod AcceptEula { $global:accepted++ }
            $global:search=[pscustomobject]@{ResultCode=$(if($case -eq 'searchPartial'){3}else{2});Updates=$(if($case -eq 'missing'){@()}else{@($global:update)})}
            $global:searcher=[pscustomobject]@{ServerSelection=-1}
            $global:searcher | Add-Member ScriptMethod Search {param($query) $global:lastQuery=$query; return $global:search }
            $global:downloadItem=[pscustomobject]@{ResultCode=$(if($case -eq 'downloadIndividual'){3}else{2});HResult=$(if($case -eq 'downloadHResult'){-2147467259}else{0})}
            $global:download=[pscustomobject]@{ResultCode=$(if($case -eq 'downloadPartial'){3}else{2});HResult=0}
            $global:download | Add-Member ScriptMethod GetUpdateResult {param($i) if($i -ne 0){throw 'Wrong update index'};return $global:downloadItem}
            $global:downloader=[pscustomobject]@{Updates=$null}
            $global:downloader | Add-Member ScriptMethod Download {if($this.Updates.Count -ne 1){throw 'Blanket download'};$global:downloads++;return $global:download}
            $global:installItem=[pscustomobject]@{ResultCode=$(if($case -eq 'installIndividual'){3}else{2});HResult=$(if($case -eq 'installHResult'){-2147467259}else{0});RebootRequired=$true}
            $global:installed=[pscustomobject]@{ResultCode=$(if($case -eq 'installPartial'){3}else{2});HResult=0;RebootRequired=$true}
            $global:installed | Add-Member ScriptMethod GetUpdateResult {param($i) if($i -ne 0){throw 'Wrong install index'};return $global:installItem}
            $global:installer=[pscustomobject]@{Updates=$null;AllowSourcePrompts=$true;ForceQuiet=$false;RebootRequiredBeforeInstallation=($case -eq 'pendingReboot')}
            $global:installer | Add-Member ScriptMethod Install {if($this.Updates.Count -ne 1 -or $this.AllowSourcePrompts -or !$this.ForceQuiet){throw 'Unsafe install'};$global:installs++;return $global:installed}
            $global:session=[pscustomobject]@{ClientApplicationID=''}
            $global:session | Add-Member ScriptMethod CreateUpdateSearcher {return $global:searcher}
            $global:session | Add-Member ScriptMethod CreateUpdateDownloader {return $global:downloader}
            $global:session | Add-Member ScriptMethod CreateUpdateInstaller {return $global:installer}
            if($case -eq 'driver'){
                $offers=(& ([scriptblock]::Create($searchCode))) | ConvertFrom-Json
                if($offers.updates.Count -ne 1 -or $offers.updates[0].type -ne 2 -or $offers.updates[0].driverManufacturer -ne 'Fixture manufacturer' -or $offers.updates[0].eulaText -cne 'Reviewed EULA ä'){throw 'Driver search metadata lost'}
                if($offers.updates[0].eulaHash -cne '4D8C01E41DB94FFF9310A25F2F062875909D05C37E75D85431272E77D30F06C7'){throw 'Exact UTF8 EULA hash lost'}
                if($global:accepted -or $global:downloads -or $global:installs){throw 'Search mutated update'}
            }
            $failed=$false
            try {$report=(& ([scriptblock]::Create($installCode))) | ConvertFrom-Json}catch{$failed=$true}
            $shouldSucceed=$case -in @('success','driver')
            if($failed -eq $shouldSucceed){throw ('Unexpected update result: '+$case)}
            if($shouldSucceed -and (!$report.rebootRequired -or $report.rebootRequested -or $report.installUpdateResultCode -ne 2)){throw 'Install result/reboot evidence lost'}
            if($case -in @('changed','missing','revision','searchPartial','interactive','pendingReboot') -and ($global:accepted -or $global:downloads -or $global:installs)){throw ('Mutation before validation: '+$case)}
            if($case -in @('downloadPartial','downloadIndividual','downloadHResult') -and $global:installs){throw 'Installed after failed download'}
            if($global:searcher.ServerSelection -ne 0 -or $global:lastQuery -notlike '*UpdateID=*RevisionNumber=7*'){throw 'Exact configured-source query lost'}
        }
        'Windows Update fixtures OK: 14 cases; no real WUA calls'
        """;

    public static string DnsFixtureScript => "$code=" +
        AdminManagement.Quote(ManagedActionCatalog.Get("dns-health").Script) + "\n" + """
        $ErrorActionPreference='Stop'
        $fixture={
            param($code,$defaultOk,$directOk,$connected)
            function Get-NetIPConfiguration {
                [pscustomobject]@{NetAdapter=[pscustomobject]@{Status=$(if($connected){'Up'}else{'Disconnected'})};IPv4DefaultGateway=[pscustomobject]@{NextHop='192.0.2.1'};IPv6DefaultGateway=$null;DNSServer=[pscustomobject]@{ServerAddresses=@('192.0.2.53')}}
            }
            function Resolve-DnsName {
                param($Name,$Server,[switch]$DnsOnly,[switch]$QuickTimeout,$ErrorAction)
                if(($Server -and $directOk) -or (!$Server -and $defaultOk)){[pscustomobject]@{IPAddress='192.0.2.10'}}else{throw 'fixture lookup failed'}
            }
            & ([scriptblock]::Create($code)) | ConvertFrom-Json
        }
        $cases=@(@($true,$true,$true,$false),@($false,$true,$true,$true),@($false,$false,$true,$false),@($false,$true,$false,$false))
        foreach($case in $cases){
            $result=& $fixture $code $case[0] $case[1] $case[2]
            if($result.needsRepair -ne $case[3]){throw 'DNS fixture policy failure'}
        }
        'DNS fixtures OK'
        """;

    public static int Run()
    {
        var count = 0;
        void Check(bool valid, string message) { count++; if (!valid) throw new InvalidOperationException(message); }
        var reviewedText = "Fixture EULA ä";
        var reviewedOffer = new
        {
            updateId = Guid.Parse("11111111-1111-1111-1111-111111111111"), revision = 7, title = "Fixture driver",
            eulaText = reviewedText,
            eulaHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(reviewedText))),
            type = 2, driverManufacturer = "Fixture manufacturer"
        };
        var reviewedJson = System.Text.Json.JsonSerializer.Serialize(new { resultCode = 2, serverSelection = 0, updates = new[] { reviewedOffer } });
        var parsedOffers = SystemCockpit.ControlCenterWindow.ParseReviewedUpdates(new(0, reviewedJson, false));
        Check(parsedOffers.Length == 1 && parsedOffers[0].UpdateId == reviewedOffer.updateId &&
            parsedOffers[0].Revision == 7 && parsedOffers[0].EulaText == reviewedText && parsedOffers[0].Type == 2,
            "Local typed selection preserves exact reviewed driver identity, revision and Unicode EULA");
        foreach(var invalidResult in new[] { new ManagedActionResult(1, reviewedJson, false),
            new(0, reviewedJson, true), new(0, "{}", false),
            new(0, reviewedJson.Replace("\"resultCode\":2", "\"resultCode\":3"), false),
            new(0, reviewedJson.Replace("\"serverSelection\":0", "\"serverSelection\":2"), false),
            new(0, reviewedJson.Replace(reviewedOffer.eulaHash, new string('A', 64)), false),
            new(0, System.Text.Json.JsonSerializer.Serialize(new { resultCode = 2, serverSelection = 0, updates = new[] { reviewedOffer, reviewedOffer } }), false) })
        {
            try { SystemCockpit.ControlCenterWindow.ParseReviewedUpdates(invalidResult); throw new InvalidOperationException("Incomplete local update approval accepted"); }
            catch(System.IO.InvalidDataException) { count++; }
        }
        var now = DateTimeOffset.UtcNow;
        Check(SystemCockpit.ControlCenterWindow.UpdatePlanFresh(now.AddMinutes(-59), now) &&
            !SystemCockpit.ControlCenterWindow.UpdatePlanFresh(now.AddHours(-1), now) &&
            !SystemCockpit.ControlCenterWindow.UpdatePlanFresh(null, now) &&
            !SystemCockpit.ControlCenterWindow.UpdatePlanFresh(now.AddMinutes(1), now),
            "Local update plans expire exactly after one hour and reject missing/future timestamps");
        var actions = ManagedActionCatalog.All;
        Check(actions.Count == 25 && actions.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() == actions.Count,
            "Managed action IDs are complete and unique");
        Check(actions.Where(a => a.CanAutoRepair).Select(a => a.Id).SequenceEqual(["dns-cache-clear"]) &&
            ManagedActionCatalog.Get("dns-cache-clear").ChangesSystem, "Only DNS cache clear can be approved for auto repair");
        Check(ManagedActionCatalog.Get("dns-cache-clear").RequiredCommands.SequenceEqual(["Clear-DnsClientCache"]),
            "DNS repair only requires the narrow cache-clear cmdlet");
        Check(actions.All(a => a.RequiredCommands.Length > 0 && a.Script.Length > 0 &&
            a.RequiredCommands.All(c => !c.Contains('\'') && !c.Contains(';'))), "Fixed scripts have explicit command requirements");
        Check(actions.All(a => !a.Script.Contains("Start-Process") && !a.Script.Contains("ms-settings:") &&
            !a.Script.Contains("/accepteula", StringComparison.OrdinalIgnoreCase)), "Remote catalog has no GUI launch or license auto acceptance");
        var dns = ManagedActionCatalog.Get("dns-health");
        Check(!dns.ChangesSystem && dns.Script.Contains("needsRepair=(!$defaultSucceeded -and $directSucceeded)") &&
            dns.Script.Contains("defaultResolverFailed") && dns.Script.Contains("configuredResolverSucceeded") &&
            dns.Script.Contains("evidence="), "DNS repair predicate requires differential resolver evidence");
        Check(dns.Script.Contains("NetAdapter.Status -eq 'Up'") && dns.Script.Contains("IPv4DefaultGateway") &&
            dns.Script.Contains("www.microsoft.com"), "Disconnected/no-route adapters do not imply a DNS cache fault");
        var update = ManagedActionCatalog.Get("windows-update-search");
        Check(!update.ChangesSystem && update.Script.Contains("Microsoft.Update.Session") &&
            update.Script.Contains("UpdateID") && update.Script.Contains("RevisionNumber") &&
            !update.Script.Contains("CreateUpdateInstaller") && !update.Script.Contains("AcceptEula") &&
            !update.Script.Contains(".Download("), "Update search reveals exact offers but never installs, downloads or accepts licenses");
        Check(new[] { "updates=", "updateId=", "eulaText=", "eulaHash=", "driverManufacturer=", "SHA256", "UTF8", "ServerSelection=0" }
            .All(update.Script.Contains), "Update search exposes exact EULA hashes and driver offers from configured source");
        var install = ManagedActionCatalog.Get("windows-update-install");
        Check(install.ChangesSystem && !install.CanAutoRepair, "Typed update installation requires approval and cannot auto repair");
        try { ManagedActionRunner.RunAsync(install, CancellationToken.None).GetAwaiter().GetResult(); throw new InvalidOperationException("Untyped install accepted"); }
        catch (ArgumentException) { count++; }
        var selection = new WindowsUpdateSelection(Guid.Parse("11111111-1111-1111-1111-111111111111"), 7, new string('A', 64), true);
        var installScript = BuildUpdateScript(selection);
        Check(installScript.Contains("RevisionNumber=$approvedRevision") && installScript.Contains("$matches.Count -ne 1") &&
            installScript.IndexOf("$actualHash -cne $approvedHash", StringComparison.Ordinal) < installScript.IndexOf("$update.AcceptEula()", StringComparison.Ordinal) &&
            !installScript.Contains("Restart-Computer") && !installScript.Contains("shutdown"),
            "Only exact identity/revision and unchanged approved EULA precede single update install");
        Check(installScript.Contains("$downloadItem.ResultCode -ne 2") && installScript.Contains("$item.ResultCode -ne 2") &&
            installScript.Contains("downloadUpdateHResult") && installScript.Contains("installUpdateHResult"),
            "Partial and individual WUA failures cannot report full success");
        foreach(var invalid in new[] {selection with { UpdateId = Guid.Empty }, selection with { Revision = -1 },
            selection with { AcceptEula = false }, selection with { EulaHash = "injection';bad" },
            selection with { EulaHash = new string('G', 64) }, selection with { EulaHash = null! }})
        {
            try { ManagedActionRunner.RunUpdateAsync(invalid, CancellationToken.None).GetAwaiter().GetResult(); throw new InvalidOperationException("Invalid typed selection accepted"); }
            catch(ArgumentException) { count++; }
        }
        var inventory = ManagedActionCatalog.Get("inventory");
        Check(!inventory.ChangesSystem &&
            new[] { "os=", "build=", "pnp=", "network=", "drivers=", "apps=", "events=", "processes=", "services=", "reboot=", "update=", "errors=", "ConvertTo-Json" }
                .All(inventory.Script.Contains), "Inventory is structured and has all requested bounded sections");
        Check(!inventory.Script.Contains("Win32_Product") && !inventory.Script.Contains(".Search("),
            "Inventory avoids MSI repair-triggering enumeration and active update searches");
        Check(actions.Where(a => !a.ChangesSystem).All(a => !a.Script.Contains("/RestoreHealth") &&
            !a.Script.Contains("/scannow") && !a.Script.Contains("Clear-DnsClientCache")),
            "Read-only actions never invoke repair scripts");
        Check(ManagedActionCatalog.Get("sfc-verify").Script.Contains("[Text.Encoding]::Unicode"),
            "SFC redirected UTF-16 is decoded rather than displayed as corrupt output");
        Check(ManagedActionTools.All.Count == actions.Count && ManagedActionTools.All.All(t => t.Risk.Length > 30),
            "Every cockpit action has an explicit risk description");
        Check(ManagedActionTools.OptionalTools.Count >= 10 && ManagedActionTools.OptionalTools.All(t =>
            new Uri(t.DownloadUrl).Host == "learn.microsoft.com" && t.DownloadUrl.StartsWith("https://")),
            "Optional tools link to official Microsoft HTTPS documentation only");
        Check(ManagedActionRunner.DefaultOutputCharacters == 65536 &&
            ManagedActionRunner.MaximumOutputCharacters == 8 * 1024 * 1024 &&
            ManagedActionRunner.Timeout > TimeSpan.Zero, "Runner bounds default jobs and larger inventory output");
        var clone = ManagedActionCatalog.Get("dns-cache-clear");
        clone.RequiredCommands[0] = "Injected";
        Check(ManagedActionCatalog.Get("dns-cache-clear").RequiredCommands[0] == "Clear-DnsClientCache",
            "Caller cannot mutate trusted catalog requirements");
        try { ManagedActionCatalog.Get("unknown"); throw new InvalidOperationException("Unknown action accepted"); }
        catch (ArgumentException) { count++; }
        foreach (var forged in new[] { inventory with { Script = "'injected'" }, inventory with { ChangesSystem = true },
                     inventory with { RequiredCommands = ["Fake-Command"] }, inventory with { RequiredCommands = null! } })
        {
            try { ManagedActionRunner.RunAsync(forged, CancellationToken.None).GetAwaiter().GetResult(); throw new InvalidOperationException("Forged action accepted"); }
            catch (ArgumentException) { count++; }
        }
        foreach (var limit in new[] { 0, -1, ManagedActionRunner.MaximumOutputCharacters + 1 })
        {
            try { ManagedActionRunner.RunAsync(inventory, CancellationToken.None, limit).GetAwaiter().GetResult(); throw new InvalidOperationException("Unbounded output accepted"); }
            catch (ArgumentOutOfRangeException) { count++; }
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { ManagedActionRunner.RunAsync(inventory, canceled.Token).GetAwaiter().GetResult(); throw new InvalidOperationException("Canceled action started"); }
        catch (OperationCanceledException) { count++; }
        try { ManagedActionRunner.RunUpdateAsync(selection, canceled.Token).GetAwaiter().GetResult(); throw new InvalidOperationException("Canceled typed selection started"); }
        catch (OperationCanceledException) { count++; }
        return count;
    }
}
