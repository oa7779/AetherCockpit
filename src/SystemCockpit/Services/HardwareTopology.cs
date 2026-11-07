using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SystemCockpit.Services;

public sealed record BoardDevice(string Id, string Name, string Class, string Status, string ParentId = "",
    string Usage = "", bool Preferred = false, bool? Connected = null, bool? InUse = null, string StorageBus = "");
public sealed record BoardSnapshot(BoardDevice[] Devices, string Board, string Internet, string[] Warnings);
public sealed record BoardGroup(string Key, string Title, string Accent, IReadOnlyList<BoardDevice> Devices);

public static class HardwareTopology
{
    public static IReadOnlyList<BoardGroup> Group(BoardSnapshot snapshot)
    {
        (string Key, string Title, string Color)[] groups =
        [
            ("cpu", "CPU / PROCESSOR", "#39FF68"), ("ram", "RAM / MEMORY BANKS", "#FFCE54"),
            ("gpu", "GPU / VIDEO", "#F56AFF"), ("monitor", "SCREENS / DESKTOP", "#66D9FF"),
            ("controller", "CONTROLLER / CHIPSET", "#32D5FF"),
            ("network", "NETZWERK / INTERNET", "#24FFE3"), ("usb", "USB / EXTERNAL I/O", "#FFB86C"),
            ("storage", "STORAGE / DRIVE BUS", "#FF576E"), ("sound", "SOUND / AUDIO", "#DCA4FF"),
            ("other", "PERIPHERIE / SYSTEM", "#82AFFF")
        ];
        return groups.Select(group => new BoardGroup(group.Key, group.Title, group.Color,
            snapshot.Devices.Where(device => Category(device) == group.Key)
                .OrderByDescending(device => device.InUse == true)
                .ThenByDescending(device => device.Preferred)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
    }

    private static string Category(BoardDevice device) => device.Class.ToUpperInvariant() switch
    {
        "PROCESSOR" or "CPU" => "cpu",
        "MEMORY" => "ram",
        "DISPLAY" => "gpu",
        "MONITOR" => "monitor",
        "NET" => "network",
        "USB" => "usb",
        "DISKDRIVE" or "SCSIADAPTER" or "HDC" or "CDROM" or "STORAGE" => "storage",
        "MEDIA" or "AUDIOENDPOINT" => "sound",
        "SYSTEM" when device.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                      device.Name.Contains("Chipset", StringComparison.OrdinalIgnoreCase) ||
                      device.Name.Contains("Bridge", StringComparison.OrdinalIgnoreCase) => "controller",
        _ => "other"
    };

    public static async Task<BoardSnapshot> ReadAsync(CancellationToken token)
    {
        var result = await AdminShell.RunAsync(Script, TimeSpan.FromSeconds(60), token);
        token.ThrowIfCancellationRequested();
        if (result.ExitCode != 0) throw new InvalidOperationException("Hardware-Schaubild: " + result.Output + result.Error);
        var snapshot = JsonSerializer.Deserialize<BoardSnapshot>(result.Output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Hardware-Erkennung lieferte keine Daten.");
        var warnings = snapshot.Warnings.ToList();
        var audioDevices = new List<BoardDevice>();
        try { audioDevices.AddRange(AudioTopology.Read()); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            warnings.Add("Aktive Audio-Endpunkte/Standardrollen nicht verfuegbar: " + ex.Message);
        }
        var inventory = MergeDevices(snapshot.Devices, audioDevices);
        try { inventory = MergeDevices(inventory, DisplayTopology.Read(inventory)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            warnings.Add("Aktive Desktop-Bildschirme nicht zuordenbar: " + ex.Message);
        }
        var parentErrors = 0;
        var devices = inventory.Select(device =>
        {
            if (!device.Id.StartsWith("RAM:", StringComparison.Ordinal) &&
                !device.Id.StartsWith("SCREEN:", StringComparison.Ordinal) && device.Connected != false)
            {
                var code = CM_Locate_DevNodeW(out var node, device.Id, 0);
                if (code != 0) { parentErrors++; return device; }
                code = CM_Get_Parent(out var parent, node, 0);
                if (code != 0) { parentErrors++; return device; }
                var id = new StringBuilder(512);
                code = CM_Get_Device_IDW(parent, id, id.Capacity, 0);
                if (code == 0) return device with { ParentId = id.ToString() };
                parentErrors++;
            }
            return device;
        }).ToArray();
        if (parentErrors > 0) warnings.Add($"Windows-Elternbeziehung fuer {parentErrors} Geraete nicht verfuegbar.");
        return snapshot with { Devices = devices, Warnings = warnings.ToArray() };
    }

    internal static BoardDevice[] MergeDevices(IEnumerable<BoardDevice> inventory, IEnumerable<BoardDevice> current) =>
        inventory.Concat(current).GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()).ToArray();

    internal static BoardSnapshot ScreensaverSnapshot(BoardSnapshot snapshot)
    {
        var devices = snapshot.Devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        bool InternalDisk(BoardDevice device)
        {
            if (!device.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase)) return false;
            if (device.StorageBus.ToUpperInvariant() is not ("ATA" or "ATAPI" or "SATA" or "SAS" or "SCSI" or
                "NVME" or "RAID" or "SPACES" or "STORAGE SPACES")) return false;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (BoardDevice? node = device; node is not null && visited.Add(node.Id);
                 node = devices.GetValueOrDefault(node.ParentId))
                if (node.Id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
                    node.Id.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) ||
                    node.Class.Equals("USB", StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
        var filtered = snapshot.Devices.Where(d =>
            !d.Class.Equals("CDROM", StringComparison.OrdinalIgnoreCase) &&
            (!d.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) || InternalDisk(d))).ToArray();
        return snapshot with { Devices = filtered };
    }

    internal static bool IsIllustratedPeripheral(BoardDevice device) => device.Connected != false &&
        (device.Class.ToUpperInvariant() is "MONITOR" or "AUDIOENDPOINT" or "KEYBOARD" or "MOUSE" or
            "CAMERA" or "IMAGE" or "PRINTER" ||
         device.Class.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase) &&
            (device.Id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
             device.Id.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) ||
             device.Id.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase)));

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint node, StringBuilder id, int length, uint flags);

    private const string Script = """
        $ErrorActionPreference='Stop'
        $warnings=[Collections.Generic.List[string]]::new()
        $devices=@()
        try {
            $devices=@(Get-PnpDevice -PresentOnly -ErrorAction Stop | ForEach-Object {
                $name=if([string]::IsNullOrWhiteSpace($_.FriendlyName)){[string]$_.InstanceId}else{[string]$_.FriendlyName}
                @{Id=[string]$_.InstanceId;Name=$name;Class=[string]$_.Class;Status=[string]$_.Status;ParentId=''}
            })
        } catch { $warnings.Add('PnP-Erkennung nicht verfuegbar: '+$_.Exception.Message) }
        try {
            $disks=@(Get-Disk -ErrorAction Stop)
            foreach($disk in @(Get-CimInstance Win32_DiskDrive -ErrorAction Stop)) {
                $match=@($disks | Where-Object { $_.Number -eq $disk.Index })
                if($match.Count -eq 1) {
                    foreach($device in @($devices | Where-Object { $_.Id -ieq $disk.PNPDeviceID })) {
                        $device.StorageBus=[string]$match[0].BusType
                    }
                }
            }
        } catch { $warnings.Add('Laufwerks-Bustyp nicht verfuegbar; unbekannte Laufwerke erscheinen nicht im Schoner: '+$_.Exception.Message) }
        try {
            $monitors=@(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID -ErrorAction Stop)
            foreach($monitor in $monitors) {
                $id=([string]$monitor.InstanceName) -replace '_\d+$',''
                $model=-join @($monitor.UserFriendlyName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ })
                $existing=@($devices | Where-Object { $_.Id -ieq $id })
                $name=if([string]::IsNullOrWhiteSpace($model)){if($existing.Count){$existing[0].Name}else{$id}}else{$model}
                $status=if($existing.Count){$existing[0].Status}else{'Unknown'}
                $usage=if($monitor.Active){'Monitorpfad aktiv (Windows WMI)'}else{'Monitorpfad nicht aktiv (Windows WMI)'}
                $devices=@($devices | Where-Object { $_.Id -ine $id })
                $devices+=@{Id=$id;Name=$name;Class='Monitor';Status=$status;ParentId='';Usage=$usage;Preferred=[bool]$monitor.Active}
            }
        } catch { $warnings.Add('Monitor-Modelle/aktive Monitorpfade nicht verfuegbar: '+$_.Exception.Message) }
        try {
            $devices+=@(Get-CimInstance Win32_PhysicalMemory | ForEach-Object {
                @{Id='RAM:'+[string]$_.Tag;Name=('{0} | {1} {2} | {3:N1} GB | {4} MT/s gemeldet' -f $_.DeviceLocator,$_.Manufacturer,$_.PartNumber,($_.Capacity/1GB),$_.ConfiguredClockSpeed);Class='Memory';Status=[string]$_.Status;ParentId=''}
            })
        } catch { $warnings.Add('RAM-Baenke nicht verfuegbar: '+$_.Exception.Message) }
        $board='Mainboard nicht verfuegbar'
        try { $board=(Get-CimInstance Win32_BaseBoard | ForEach-Object { "$($_.Manufacturer) $($_.Product)" }) -join ' + ' }
        catch { $warnings.Add('Mainboard nicht verfuegbar: '+$_.Exception.Message) }
        $internet='Windows-Internetstatus nicht verfuegbar'
        try {
            $nlm=[Activator]::CreateInstance([type]::GetTypeFromCLSID([guid]'DCB00C01-570F-4A9B-8D69-199FDBA5723B'))
            $flags=[int]$nlm.GetConnectivity()
            $internet=if(($flags -band 0x440) -ne 0){'Internet laut Windows verbunden (kein eigener Verbindungstest)'}else{'Windows meldet keine Internetverbindung'}
        } catch { $warnings.Add('Internetstatus nicht verfuegbar: '+$_.Exception.Message) }
        @{Devices=@($devices);Board=$board;Internet=$internet;Warnings=@($warnings.ToArray())} | ConvertTo-Json -Depth 5 -Compress
        """;
}
