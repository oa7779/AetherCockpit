using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;

namespace SystemCockpit.Services;

public sealed record AdminEntity(string Id, string Name, string Status, string Detail, string Token = "", string Auxiliary = "");
public sealed record AdminArea(string Id, string Name, string Description, string Query, IReadOnlyList<string> Actions)
{
    public override string ToString() => Name;
}
public sealed record AdminPlan(string Label, string Script, string? RestoreScript, string Warning);
public sealed record DnsConfiguration(string Family, string Servers);

public static class AdminManagement
{
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    private const string ItemFunction = """
        function Row($id,$name,$status,$detail,$token='',$aux='') {
            @{Id=[string]$id;Name=[string]$name;Status=[string]$status;Detail=[string]$detail;Token=[string]$token;Auxiliary=[string]$aux}
        }
        """;

    public static IReadOnlyList<AdminArea> Areas { get; } =
    [
        new("processes", "Prozesse / Taskmanager", "Exakte PID + Startzeit schuetzen vor PID-Wiederverwendung. Beenden ist nicht rueckgaengig.",
            "Get-Process | ForEach-Object { $p=$_; try { Row $p.Id $p.ProcessName $p.PriorityClass ('RAM MB: '+[math]::Round($p.WorkingSet64/1MB,1)+' / Threads: '+$p.Threads.Count+' / CPU gesamt s: '+$p.CPU) $p.StartTime.ToUniversalTime().Ticks } catch { Row $p.Id ('PID '+$p.Id) 'Nicht lesbar' $_.Exception.Message } }",
            ["Prozess beenden", "Prioritaet Normal", "Prioritaet BelowNormal", "Prioritaet AboveNormal"]),
        new("services", "Dienste", "Start/Stop/Neustart und Starttyp. Kein pauschales Deaktivieren wichtiger Dienste. Wiederherstellung umfasst Starttyp, nicht vergangene Dienstaktionen.",
            """
            Get-CimInstance Win32_Service | ForEach-Object {
                $delayed=[Microsoft.Win32.Registry]::GetValue(('HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'+$_.Name),'DelayedAutoStart',$null)
                Row $_.Name $_.DisplayName $_.State ('Start: '+$_.StartMode+' / Delayed: '+$delayed+' / Konto: '+$_.StartName+' / PID: '+$_.ProcessId) $_.StartMode $(if($null -eq $delayed){'absent'}else{[string]$delayed})
            }
            """,
            ["Starten", "Stoppen", "Neustarten", "Starttyp Automatisch", "Starttyp Manuell", "Starttyp Deaktiviert"]),
        new("tasks", "Geplante Aufgaben", "Ausfuehren, beenden, aktivieren/deaktivieren. Gestartete Aufgaben koennen bereits Systemaenderungen gemacht haben.",
            "Get-ScheduledTask | ForEach-Object { Row ($_.TaskPath+$_.TaskName) $_.TaskName $_.State $_.TaskPath $_.TaskPath ([string]$_.Settings.Enabled) }",
            ["Ausfuehren", "Beenden", "Aktivieren", "Deaktivieren"]),
        new("features", "Windows-Features", "Windows Optional Features eingebettet; kein automatischer Neustart. Zustandswechsel koennen Downloads und Neustart erfordern.",
            "Get-WindowsOptionalFeature -Online | ForEach-Object { Row $_.FeatureName $_.FeatureName $_.State 'Online Windows-Feature / Abhaengigkeiten beachten' }",
            ["Aktivieren", "Deaktivieren"]),
        new("packages", "Installierte Apps / WinGet", "GUI-Inventar aus Uninstall-Registry (nicht alle AppX-Pakete). WinGet-Ziel-ID im Parameterfeld explizit eingeben; keine Namensraten.",
            """
            foreach($path in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')) {
                foreach($p in Get-ItemProperty $path) { if($p.DisplayName) { Row $p.PSPath $p.DisplayName $p.DisplayVersion ('Herausgeber: '+$p.Publisher) } }
            }
            """, ["WinGet suchen", "WinGet Details", "WinGet installieren", "WinGet aktualisieren", "WinGet deinstallieren"]),
        new("updates", "Windows-Update-Historie", "Lokale Updatehistorie ueber Windows Update COM; kein versteckter Zwangsneustart. Reparatur und Windows-Featureverwaltung separat.",
            """
            $searcher=(New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher()
            $count=[Math]::Min(300,$searcher.GetTotalHistoryCount())
            if($count -gt 0) { $searcher.QueryHistory(0,$count) | ForEach-Object { Row $_.UpdateIdentity.UpdateID $_.Title $_.ResultCode ($_.Date.ToString('s')+' / Operation: '+$_.Operation) } }
            """, ["WinGet Updateangebot", "DISM Zustand"]),
        new("power", "Energieplaene", "Vorhandene Energieplaene aus Windows lesen/aktivieren. Keine ungeprueften Ultimate-Performance-Presets.",
            """
            $plans=& powercfg.exe /list
            if($LASTEXITCODE -ne 0){throw 'powercfg /list fehlgeschlagen'}
            foreach($line in $plans) {
                $m=[regex]::Match($line,'(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((?<name>.*)\)\s*(?<active>\*)?\s*$')
                if($m.Success){ Row $m.Groups['id'].Value $m.Groups['name'].Value $m.Groups['active'].Success 'Windows-Energieplan / powercfg' }
            }
            """, ["Aktivieren"]),
        new("adapters", "Netzwerkadapter / DNS", "Adapter, Link, DNS lesen; Deaktivierung/DNS-Wechsel kann Zugriff und Internet trennen. DNS-IP-Liste kommasepariert eingeben.",
            """
            $dnsConfigurations=@(Get-DnsClientServerAddress)
            Get-NetAdapter | ForEach-Object {
                $a=$_; $configuration=@($dnsConfigurations | Where-Object InterfaceIndex -eq $a.ifIndex)
                $dns=@($configuration | ForEach-Object ServerAddresses)
                $backup=@(foreach($family in $configuration) {
                    $protocol=if($family.AddressFamily -eq 2){'Tcpip'}elseif($family.AddressFamily -eq 23){'Tcpip6'}else{throw 'DNS-Adressfamilie unbekannt'}
                    $key='HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'+$protocol+'\Parameters\Interfaces\'+$a.InterfaceGuid
                    $servers=[Microsoft.Win32.Registry]::GetValue($key,'NameServer',$null)
                    if($null -ne $servers -and $servers -isnot [string]){throw 'DNS-Registrytyp unbekannt'}
                    @{Family=$(if($family.AddressFamily -eq 2){'IPv4'}else{'IPv6'});Servers=[string]$servers}
                })
                $dnsText=if($configuration.Count){$dns -join ', '}else{'DNS-Provider: keine Interface-Daten'}
                Row $a.ifIndex $a.Name $a.AdminStatus ('Linkstatus: '+$a.Status+' / Link: '+$a.LinkSpeed+' / MAC: '+$a.MacAddress+' / DNS: '+$dnsText) $a.InterfaceGuid $(if($configuration.Count){ConvertTo-Json -InputObject @($backup) -Compress}else{'unavailable'})
            }
            """, ["Aktivieren", "Deaktivieren", "DNS setzen", "DNS automatisch"]),
        new("proxy", "WinHTTP-Proxy", "WinHTTP-Systemproxy, nicht Browser-/Benutzerproxy. Parameter: host:port oder host:port|Bypass-Liste (z.B. proxy:8080|<local>;*.example).",
            """
            $detail=& netsh.exe winhttp show proxy
            if($LASTEXITCODE -ne 0){throw 'WinHTTP-Status fehlgeschlagen'}
            $dump=& netsh.exe winhttp dump
            if($LASTEXITCODE -ne 0){throw 'WinHTTP-Sicherung fehlgeschlagen'}
            Row 'winhttp' 'WinHTTP Systemproxy' 'Status siehe Detail' ($detail | Out-String) '' ($dump -join "`n")
            """,
            ["Proxy setzen", "Proxy direkt"]),
        new("firewall", "Firewallregeln", "Vorhandene Regeln gezielt aktivieren/deaktivieren, nicht Firewall global abschalten. GPO-Regeln koennen gesperrt sein.",
            "Get-NetFirewallRule | ForEach-Object { Row $_.Name $_.DisplayName $_.Enabled ($_.Direction.ToString()+' / '+$_.Action+' / '+$_.Profile+' / '+$_.PolicyStoreSourceType) }",
            ["Aktivieren", "Deaktivieren"]),
        new("users", "Lokale Benutzer", "Kontenstatus/Details; kein Passwort exportiert. Eigenes Admin-Konto nicht deaktivieren; kein Garant gegen Aussperren.",
            "Get-LocalUser | ForEach-Object { Row $_.SID.Value $_.Name $_.Enabled ('Letzte Anmeldung: '+$_.LastLogon+' / Passwort erforderlich: '+$_.PasswordRequired) }",
            ["Aktivieren", "Deaktivieren"]),
        new("groups", "Gruppen / Mitgliedschaften", "Gruppe auswaehlen, Mitglied als Konto/SID im Parameterfeld. Adminrechteaenderung kann neue Anmeldung erfordern.",
            """
            Get-LocalGroup | ForEach-Object {
                $group=$_
                try {
                    $members=@(Get-LocalGroupMember -SID $group.SID)
                    Row $group.SID.Value $group.Name 'Lokale Gruppe' ($group.Description+' / Mitglieder: '+(($members.Name) -join ', ')) '' (($members.SID.Value | Sort-Object) -join ',')
                } catch { Row $group.SID.Value $group.Name 'Mitgliedschaften nicht lesbar' $_.Exception.Message '' 'unavailable' }
            }
            """,
            ["Mitglieder lesen", "Mitglied hinzufuegen", "Mitglied entfernen"]),
        new("acl", "Dateiberechtigungen / ACL", "Absoluten vorhandenen Datei-/Ordnerpfad im Parameterfeld eingeben. Vererbung oder Rechte werden nicht rekursiv pauschal veraendert.",
            "Row 'acl' 'Datei-/Ordnerberechtigungen' 'Pfad erforderlich' 'Absoluten Pfad im Parameterfeld eingeben; ACL-Ausgabe und Vorschau in der eingebetteten Konsole.'",
            ["ACL lesen", "Vererbung deaktivieren / Rechte behalten", "Vererbung aktivieren"])
    ];

    public static async Task<List<AdminEntity>> ReadAsync(AdminArea area, CancellationToken token, string parameter = "")
    {
        if (!Areas.Contains(area)) throw new ArgumentException("Unbekannter Verwaltungsbereich.");
        var query = area.Query;
        if (area.Id == "acl" && !string.IsNullOrWhiteSpace(parameter))
        {
            if (!System.IO.Path.IsPathFullyQualified(parameter)) throw new ArgumentException("Absoluten ACL-Zielpfad eingeben.");
            query = "Import-Module \"$PSHOME\\Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1\"; " +
                $"$item=Get-Item -LiteralPath {Quote(parameter)} -Force; " +
                "if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'ACL-Ziel ist ReparsePoint; keine automatische Rechteaenderung'}; " +
                "$acl=Get-Acl -LiteralPath $item.FullName; Row $item.FullName $item.Name 'ACL / SDDL gelesen' $acl.AccessToString $item.CreationTimeUtc.Ticks $acl.Sddl";
        }
        var json = await CommandRunner.PowerShellAsync(ItemFunction + "\n$rows=@(& { " + query + " }); " +
            "@{Rows=@($rows)} | ConvertTo-Json -Depth 4 -Compress", token);
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.GetProperty("Rows").Deserialize<List<AdminEntity>>() ??
            throw new InvalidOperationException("Verwaltung lieferte kein Tabellenformat.");
        if (rows.Count > 10000 || rows.Any(r => r.Id is null || r.Name is null || r.Status is null || r.Detail is null))
            throw new InvalidOperationException("Ungueltige Verwaltungsdaten.");
        return rows;
    }

    public static AdminPlan Plan(AdminArea area, string action, AdminEntity? entity, string parameter)
    {
        if (!Areas.Contains(area) || !area.Actions.Contains(action)) throw new ArgumentException("Unbekannte Verwaltungsaktion.");
        var row = entity ?? throw new ArgumentException("Zuerst eine Tabellenzeile auswaehlen.");
        var id = Quote(row.Id);
        var name = Quote(row.Name);
        var script = "";
        string? restore = null;
        var changes = true;
        switch (area.Id)
        {
            case "processes":
                if (!int.TryParse(row.Id, out var pid) || pid <= 4 || !long.TryParse(row.Token, out var ticks))
                    throw new ArgumentException("Keine sichere PID/Startzeit vorhanden; geschuetzte Systemprozesse bleiben gesperrt.");
                script = $"$p=Get-Process -Id {pid}; if($p.StartTime.ToUniversalTime().Ticks -ne {ticks} -or $p.Id -eq $PID){{throw 'Prozess hat sich geaendert oder ist diese Shell'}}; ";
                if (action == "Prozess beenden")
                    script = "# Native Prozessaktion: PID/Erstellungszeit, Windows-Schutz, Eigenkomponenten, Sicherheitsdialog und verifiziertes Prozessende. Kein PowerShell-Kill.";
                else
                {
                    var priority = action["Prioritaet ".Length..];
                    if (row.Status is not ("Normal" or "BelowNormal" or "AboveNormal" or "High" or "Idle"))
                        throw new ArgumentException("Vorherige Prioritaet nicht sicher lesbar.");
                    restore = script + $"$p.PriorityClass={Quote(row.Status)}";
                    script += $"$p.PriorityClass={Quote(priority)}; $p.Refresh(); if([string]$p.PriorityClass -ne {Quote(priority)}){{throw 'Prioritaet nicht verifiziert'}}";
                }
                break;
            case "services":
                script = $"$s=Get-Service -Name {id}; ";
                if (action.StartsWith("Starttyp "))
                {
                    var type = action switch { "Starttyp Automatisch" => "Automatic", "Starttyp Manuell" => "Manual", _ => "Disabled" };
                    var old = row.Token switch { "Auto" => "Automatic", "Manual" => "Manual", "Disabled" => "Disabled", _ => throw new ArgumentException("Starttyp unbekannt.") };
                    var guard = $"if((Get-CimInstance Win32_Service | Where-Object Name -eq {id}).StartMode -ne {Quote(row.Token)}){{throw 'Starttyp extern geaendert'}}; ";
                    script += guard + $"Set-Service -Name {id} -StartupType {type}";
                    if (row.Auxiliary is not ("0" or "1" or "absent")) throw new ArgumentException("DelayedAutoStart nicht sicher lesbar.");
                    var servicePath = Quote(@"HKLM:\SYSTEM\CurrentControlSet\Services\" + row.Id);
                    restore = $"Set-Service -Name {id} -StartupType {old}; " +
                        (row.Auxiliary == "absent" ? $"if((Get-Item -LiteralPath {servicePath}).GetValueNames() -contains 'DelayedAutoStart'){{Remove-ItemProperty -LiteralPath {servicePath} -Name DelayedAutoStart}}" :
                        $"New-ItemProperty -LiteralPath {servicePath} -Name DelayedAutoStart -Value {row.Auxiliary} -PropertyType DWord -Force | Out-Null");
                }
                else script += action switch { "Starten" => $"Start-Service -Name {id}", "Stoppen" => $"Stop-Service -Name {id}", _ => $"Restart-Service -Name {id}" };
                break;
            case "tasks":
                var taskArgs = $"-TaskPath {Quote(row.Token)} -TaskName {name}";
                script = action switch
                {
                    "Ausfuehren" => $"Start-ScheduledTask {taskArgs}",
                    "Beenden" => $"Stop-ScheduledTask {taskArgs}",
                    "Aktivieren" => $"Enable-ScheduledTask {taskArgs} | Out-Null",
                    _ => $"Disable-ScheduledTask {taskArgs} | Out-Null"
                };
                if (action is "Aktivieren" or "Deaktivieren")
                {
                    if (!bool.TryParse(row.Auxiliary, out _)) throw new ArgumentException("Aufgabenaktivierung nicht lesbar.");
                    restore = $"{(row.Auxiliary.Equals("True", StringComparison.OrdinalIgnoreCase) ? "Enable" : "Disable")}-ScheduledTask {taskArgs} | Out-Null";
                }
                break;
            case "features":
                if (row.Status is not ("Enabled" or "Disabled")) throw new ArgumentException("Featurezustand ist pending/unbekannt. Erst Windows-Neustart/Status pruefen.");
                var enable = action == "Aktivieren";
                script = $"if([string](Get-WindowsOptionalFeature -Online -FeatureName {id}).State -ne {Quote(row.Status)}){{throw 'Featurezustand geaendert'}}; " +
                    (enable ? $"Enable-WindowsOptionalFeature -Online -FeatureName {id} -NoRestart" : $"Disable-WindowsOptionalFeature -Online -FeatureName {id} -NoRestart");
                restore = row.Status == "Enabled" ? $"Enable-WindowsOptionalFeature -Online -FeatureName {id} -NoRestart" : $"Disable-WindowsOptionalFeature -Online -FeatureName {id} -NoRestart";
                break;
            case "packages":
                ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
                var target = Quote(parameter.Trim());
                var verb = action switch { "WinGet suchen" => "search", "WinGet Details" => "show", "WinGet installieren" => "install", "WinGet aktualisieren" => "upgrade", _ => "uninstall" };
                changes = verb is not ("search" or "show");
                script = $"& winget.exe {verb} {(verb == "search" ? target : "--id " + target + " --exact")} --disable-interactivity; if($LASTEXITCODE -ne 0){{throw \"WinGet Exit $LASTEXITCODE\"}}";
                break;
            case "updates":
                changes = false;
                script = action == "DISM Zustand" ? "& dism.exe /Online /Cleanup-Image /CheckHealth; if($LASTEXITCODE -ne 0){throw \"DISM Exit $LASTEXITCODE\"}" :
                    "& winget.exe upgrade --disable-interactivity; if($LASTEXITCODE -ne 0){throw \"WinGet Exit $LASTEXITCODE\"}";
                break;
            case "power":
                if (!Guid.TryParse(row.Id, out _)) throw new ArgumentException("Ungueltige Energieplan-ID.");
                script = $"& powercfg.exe /setactive {id}; if($LASTEXITCODE -ne 0){{throw 'Energieplanwechsel fehlgeschlagen'}}";
                break;
            case "adapters":
                if (action.StartsWith("DNS") && row.Auxiliary == "unavailable")
                    throw new ArgumentException("Adapter hat keine DNS-Providerdaten; kein DNS-Wechsel moeglich.");
                if (!int.TryParse(row.Id, out var index) || index < 1 || !Guid.TryParse(row.Token, out _)) throw new ArgumentException("Adapterkennung nicht lesbar.");
                var guardAdapter = $"$a=Get-NetAdapter | Where-Object ifIndex -eq {index}; if($null -eq $a -or [string]$a.InterfaceGuid -ne {Quote(row.Token)}){{throw 'Adapterkennung geaendert'}}; ";
                script = guardAdapter;
                if (action is "Aktivieren" or "Deaktivieren")
                {
                    if (row.Status is not ("Up" or "Down")) throw new ArgumentException("Administrativer Adapterstatus unbekannt.");
                    script += $"$a | {(action == "Aktivieren" ? "Enable" : "Disable")}-NetAdapter -Confirm:$false";
                    restore = guardAdapter + $"$a | {(row.Status == "Down" ? "Disable" : "Enable")}-NetAdapter -Confirm:$false";
                }
                else
                {
                    if (action == "DNS automatisch") script += $"Set-DnsClientServerAddress -InterfaceIndex {index} -ResetServerAddresses";
                    else
                    {
                        var addresses = parameter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        if (addresses.Length is < 1 or > 8 || addresses.Any(a => !IPAddress.TryParse(a, out _))) throw new ArgumentException("1..8 gueltige DNS-IP-Adressen kommasepariert eingeben.");
                        script += $"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses @({string.Join(",", addresses.Select(Quote))})";
                    }
                    restore = guardAdapter + DnsRestore(index, row.Auxiliary);
                }
                break;
            case "proxy":
                if (action == "Proxy direkt") script = "& netsh.exe winhttp reset proxy";
                else
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
                    var proxyParts = parameter.Split('|', 2, StringSplitOptions.TrimEntries);
                    if (proxyParts[0].Length == 0) throw new ArgumentException("Proxy host:port eingeben.");
                    script = "& netsh.exe winhttp set proxy " + Quote("proxy-server=" + proxyParts[0]) +
                        (proxyParts.Length == 2 ? " " + Quote("bypass-list=" + proxyParts[1]) : "");
                }
                script += "; if($LASTEXITCODE -ne 0){throw \"WinHTTP Exit $LASTEXITCODE\"}";
                restore = ProxyRestore(row.Auxiliary);
                break;
            case "firewall":
                var enabled = action == "Aktivieren" ? "True" : "False";
                if (row.Status is not ("True" or "False")) throw new ArgumentException("Regelzustand unbekannt.");
                script = $"if([string](Get-NetFirewallRule -Name {id}).Enabled -ne {Quote(row.Status)}){{throw 'Firewallregel geaendert'}}; Set-NetFirewallRule -Name {id} -Enabled {enabled}";
                restore = $"Set-NetFirewallRule -Name {id} -Enabled {row.Status}";
                break;
            case "users":
                if (!row.Id.StartsWith("S-1-", StringComparison.Ordinal) || row.Status is not ("True" or "False")) throw new ArgumentException("Benutzer-SID/Status fehlt.");
                var userGuard = $"$u=Get-LocalUser -SID {id}; if($u.SID.Value -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value){{throw 'Eigenes Konto wird nicht veraendert'}}; ";
                script = userGuard + $"{(action == "Aktivieren" ? "Enable" : "Disable")}-LocalUser -SID {id}";
                restore = userGuard + $"{(row.Status == "True" ? "Enable" : "Disable")}-LocalUser -SID {id}";
                break;
            case "groups":
                if (!row.Id.StartsWith("S-1-", StringComparison.Ordinal)) throw new ArgumentException("Gruppen-SID fehlt.");
                changes = action != "Mitglieder lesen";
                if (!changes) script = $"Get-LocalGroupMember -SID {id} | Format-Table Name,SID,ObjectClass";
                else
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
                    if (row.Auxiliary == "unavailable") throw new ArgumentException("Mitgliedschaften nicht lesbar; keine sichere Aenderung.");
                    var member = Quote(parameter);
                    var adding = action == "Mitglied hinzufuegen";
                    var memberGuard = $"$group=Get-LocalGroup -SID {id}; $memberSid=if({member}.StartsWith('S-1-')){{[Security.Principal.SecurityIdentifier]::new({member})}}else{{([Security.Principal.NTAccount]::new({member})).Translate([Security.Principal.SecurityIdentifier])}}; ";
                    script = memberGuard + $"$members=@(Get-LocalGroupMember -SID {id}); " +
                        $"if((($members.SID.Value | Sort-Object) -join ',') -ne {Quote(row.Auxiliary)}){{throw 'Mitgliedschaften extern geaendert'}}; " +
                        $"if(($members.SID.Value -contains $memberSid.Value) -eq ${(adding ? "true" : "false")}){{throw 'Mitgliedzustand bereits wie angefordert'}}; " +
                        $"{(adding ? "Add" : "Remove")}-LocalGroupMember -SID {id} -Member $memberSid.Value";
                    restore = memberGuard + $"{(adding ? "Remove" : "Add")}-LocalGroupMember -SID {id} -Member $memberSid.Value";
                }

                break;
            case "acl":
                if (!System.IO.Path.IsPathFullyQualified(parameter) || !System.IO.Path.Exists(parameter)) throw new ArgumentException("Vorhandenen absoluten Datei-/Ordnerpfad eingeben.");
                var path = Quote(parameter);
                changes = action != "ACL lesen";
                var aclImport = "Import-Module \"$PSHOME\\Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1\"; ";
                if (!changes) script = aclImport + $"Get-Acl -LiteralPath {path} | Format-List Path,Owner,AreAccessRulesProtected,AccessToString,Sddl";
                else
                {
                    if (!row.Id.Equals(System.IO.Path.GetFullPath(parameter), StringComparison.OrdinalIgnoreCase) ||
                        !long.TryParse(row.Token, out var creation) || creation <= 0 || string.IsNullOrWhiteSpace(row.Auxiliary))
                        throw new ArgumentException("ACL-Ziel erst ueber 'Status lesen' laden: Original-SDDL/Dateiidentitaet erforderlich.");
                    var aclGuard = aclImport + $"$item=Get-Item -LiteralPath {path} -Force; if($item.CreationTimeUtc.Ticks -ne {creation} -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){{throw 'ACL-Zielidentitaet geaendert'}}; $acl=Get-Acl -LiteralPath {path}; ";
                    script = aclGuard + $"if($acl.Sddl -ne {Quote(row.Auxiliary)}){{throw 'ACL extern geaendert'}}; " +
                        $"$acl.SetAccessRuleProtection(${(action.StartsWith("Vererbung deaktivieren") ? "true" : "false")},$true); Set-Acl -LiteralPath {path} -AclObject $acl";
                    restore = aclGuard + $"$acl.SetSecurityDescriptorSddlForm({Quote(row.Auxiliary)},[Security.AccessControl.AccessControlSections]::Access); Set-Acl -LiteralPath {path} -AclObject $acl";
                }
                break;
            default: throw new ArgumentException("Unbekannter Bereich.");
        }
        return new($"{area.Name} / {action}", "$ErrorActionPreference='Stop'; " + script, restore is null ? null : "$ErrorActionPreference='Stop'; " + restore,
            $"{area.Description}\nZiel: {row.Name} ({row.Id})\n" +
            (changes ? "SYSTEMAENDERUNG. Vorher sichern; Teilaktionen bleiben bei Fehler/Abbruch bestehen." : "Abfrage; ggf. Netzwerkzugriff/sensible Informationen.") +
            "\n" + (restore is null ? "Keine automatische Ruecknahme fuer diese Aktion; bewusste manuelle Wiederherstellung erforderlich." :
                "Ruecknahme im Sitzungsverlauf verfuegbar. Vorherigen Zustand pruefen; keine Wiederherstellung vergangener Daten/Prozessaktionen."));
    }

    internal static string DnsRestore(int index, string json)
    {
        var configurations = JsonSerializer.Deserialize<List<DnsConfiguration>>(json) ??
            throw new ArgumentException("DNS-Sicherung fehlt.");
        if (configurations.Count is < 1 or > 2 || configurations.Any(c => c is null || c.Family is not ("IPv4" or "IPv6") || c.Servers is null) ||
            configurations.Select(c => c.Family).Distinct().Count() != configurations.Count)
            throw new ArgumentException("Ungueltige DNS-Konfigurationssicherung.");
        return string.Join("; ", configurations.Select(c =>
        {
            var addresses = Regex.Split(c.Servers.Trim(), @"[,\s;]+").Where(a => a.Length > 0).ToArray();
            if (addresses.Any(a => !IPAddress.TryParse(a, out var ip) ||
                (c.Family == "IPv4") != (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)))
                throw new ArgumentException("Originale DNS-Adressen nicht sicher lesbar.");
            return $"Get-DnsClientServerAddress -InterfaceIndex {index} -AddressFamily {c.Family} | Set-DnsClientServerAddress " +
                (addresses.Length == 0 ? "-ResetServerAddresses" : $"-ServerAddresses @({string.Join(",", addresses.Select(Quote))})");
        }));
    }

    internal static string? ProxyRestore(string dump)
    {
        var commands = new List<string>();
        foreach (var line in dump.Split('\n').Select(line => line.Trim()))
        {
            if (line.Length == 0 || line.StartsWith('#') || line is "pushd winhttp" or "popd") continue;
            if (line == "reset proxy") commands.Add("& netsh.exe winhttp reset proxy");
            else
            {
                var match = Regex.Match(line, "^set proxy proxy-server=\"([^\"]*)\" bypass-list=\"([^\"]*)\"$");
                if (!match.Success) return null;
                commands.Add($"& netsh.exe winhttp set proxy {Quote("proxy-server=" + match.Groups[1].Value)} {Quote("bypass-list=" + match.Groups[2].Value)}");
            }
        }
        return commands.Count == 0 ? null : string.Join("; ", commands.Select(c => c + "; if($LASTEXITCODE -ne 0){throw 'WinHTTP-Ruecknahme fehlgeschlagen'}"));
    }
}
