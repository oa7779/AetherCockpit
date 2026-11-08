namespace SystemCockpit.Services;

public sealed record WindowsTroubleshootingAssistant(string Id, string Name, string Scope, string Script, string NextSteps);

public static class WindowsTroubleshootingAssistants
{
    public static IReadOnlyList<WindowsTroubleshootingAssistant> All =>
    [
        new("system", DesktopText.Get("Assistant.System.Name"), DesktopText.Get("Assistant.System.Scope"),
            DesktopScriptText.Preamble("Assistant.Script.Dism", "Assistant.Script.Sfc", "Assistant.Script.SystemAdvice") +
            """
            $ErrorActionPreference='Stop'
            "=== WINDOWS ==="
            Get-CimInstance Win32_OperatingSystem | Format-List Caption,Version,BuildNumber,LastBootUpTime
            $aetherDesktopText['Assistant.Script.Dism']
            & "$env:WINDIR\System32\dism.exe" /Online /Cleanup-Image /CheckHealth
            if($LASTEXITCODE -ne 0){throw "DISM CheckHealth Exit $LASTEXITCODE"}
            $aetherDesktopText['Assistant.Script.Sfc']
            & "$env:WINDIR\System32\sfc.exe" /verifyonly
            if($LASTEXITCODE -ne 0){throw "SFC VerifyOnly Exit $LASTEXITCODE"}
            $aetherDesktopText['Assistant.Script.SystemAdvice']
            """,
            DesktopText.Get("Assistant.System.NextSteps")),
        new("update", DesktopText.Get("Assistant.Update.Name"), DesktopText.Get("Assistant.Update.Scope"),
            DesktopScriptText.Preamble("Assistant.Script.UpdateServices", "Assistant.Script.RestartIndicators",
                "Assistant.Script.UpdateEvents", "Assistant.Script.EventCount") +
            """
            $ErrorActionPreference='Stop'
            $aetherDesktopText['Assistant.Script.UpdateServices']
            Get-Service wuauserv,bits,cryptsvc,TrustedInstaller | Format-Table Name,Status,StartType
            $aetherDesktopText['Assistant.Script.RestartIndicators']
            [pscustomobject]@{
                ComponentServicing=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending')
                WindowsUpdate=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
                PendingFileRename=(Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations')
            } | Format-List
            $aetherDesktopText['Assistant.Script.UpdateEvents']
            $errors=@()
            $events=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WindowsUpdateClient/Operational';StartTime=(Get-Date).AddDays(-7);Level=1,2,3} -MaxEvents 100 -ErrorAction SilentlyContinue -ErrorVariable errors)
            $unexpected=@($errors | Where-Object FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*')
            if($unexpected.Count){throw ($unexpected | Out-String)}
            $events | Format-List TimeCreated,Id,LevelDisplayName,Message
            ($aetherDesktopText['Assistant.Script.EventCount'] -f $events.Count)
            """,
            DesktopText.Get("Assistant.Update.NextSteps")),
        new("network", DesktopText.Get("Assistant.Network.Name"), DesktopText.Get("Assistant.Network.Scope"),
            DesktopScriptText.Preamble("Assistant.Script.Adapters", "Assistant.Script.IpDns",
                "Assistant.Script.Gateway", "Assistant.Script.DnsQuery", "Assistant.Script.DnsError",
                "Assistant.Script.NoIpAdapters", "Assistant.Script.NetworkAdvice") +
            """
            $ErrorActionPreference='Stop'
            $aetherDesktopText['Assistant.Script.Adapters']
            Get-NetAdapter -IncludeHidden | Format-Table Name,InterfaceDescription,Status,LinkSpeed
            $aetherDesktopText['Assistant.Script.IpDns']
            $configs=@(Get-NetIPConfiguration -ErrorAction Stop | Where-Object {$_.NetAdapter.Status -eq 'Up'})
            $configs | Format-List InterfaceAlias,IPv4Address,IPv6Address,IPv4DefaultGateway,DNSServer
            foreach($config in $configs){
                foreach($gateway in @($config.IPv4DefaultGateway | Where-Object {$_.NextHop})){
                    ($aetherDesktopText['Assistant.Script.Gateway'] -f $config.InterfaceAlias,$gateway.NextHop)
                    Test-Connection -ComputerName $gateway.NextHop -Count 1 -Quiet -TimeoutSeconds 3
                }
                $dns=@($config.DNSServer.ServerAddresses | Where-Object {$_})
                if($dns.Count){
                    ($aetherDesktopText['Assistant.Script.DnsQuery'] -f $config.InterfaceAlias,$dns[0])
                    try { Resolve-DnsName -Name 'www.microsoft.com' -Server $dns[0] -DnsOnly -QuickTimeout -ErrorAction Stop | Select-Object -First 4 Name,Type,IPAddress | Format-Table }
                    catch { ($aetherDesktopText['Assistant.Script.DnsError'] -f $_.Exception.Message) }
                }
            }
            if(!$configs.Count){$aetherDesktopText['Assistant.Script.NoIpAdapters']}
            $aetherDesktopText['Assistant.Script.NetworkAdvice']
            """,
            DesktopText.Get("Assistant.Network.NextSteps")),
        new("devices", DesktopText.Get("Assistant.Devices.Name"), DesktopText.Get("Assistant.Devices.Scope"),
            DesktopScriptText.Preamble("Assistant.Script.DeviceProblems", "Assistant.Script.NoDeviceProblems",
                "Assistant.Script.DriverInventory", "Assistant.Script.DriverAdvice") +
            """
            $ErrorActionPreference='Stop'
            $aetherDesktopText['Assistant.Script.DeviceProblems']
            $problems=@(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object {$_.Problem -gt 0 -or $_.Status -eq 'Error'})
            if($problems.Count){
                $problems | Select-Object FriendlyName,Class,Status,Problem,InstanceId | Format-List
            }else{$aetherDesktopText['Assistant.Script.NoDeviceProblems']}
            $aetherDesktopText['Assistant.Script.DriverInventory']
            Get-CimInstance Win32_PnPSignedDriver | Sort-Object DeviceName | Select-Object -First 30 DeviceName,DriverVersion,DriverDate,IsSigned,InfName | Format-Table
            $aetherDesktopText['Assistant.Script.DriverAdvice']
            """,
            DesktopText.Get("Assistant.Devices.NextSteps"))
    ];

    public static WindowsTroubleshootingAssistant Get(string id) =>
        All.SingleOrDefault(assistant => assistant.Id == id)
        ?? throw new ArgumentException(DesktopUiText.Get("Assistant.Unknown"), nameof(id));
}
