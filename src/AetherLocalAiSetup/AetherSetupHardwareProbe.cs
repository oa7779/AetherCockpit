using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using AetherOperations.Intelligence;

namespace AetherLocalAiSetup;

internal sealed class AetherSetupHardwareProbe
{
    private const int MaximumInventoryBytes = 65536;
    private const string InventoryScript =
        "$ErrorActionPreference='Stop';" +
        "$cpus=@(Get-CimInstance -ClassName Win32_Processor);" +
        "$cpuName=($cpus | ForEach-Object {[string]$_.Name} | Sort-Object -Unique) -join ' + ';" +
        "$cpuCores=($cpus | Measure-Object -Property NumberOfCores -Sum).Sum;" +
        "$logical=($cpus | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum;" +
        "$gpus=@(Get-CimInstance -ClassName Win32_VideoController | ForEach-Object {" +
        "[pscustomobject]@{Name=[string]$_.Name;DriverVersion=[string]$_.DriverVersion}});" +
        "[pscustomobject]@{CpuName=$cpuName;CpuCores=$cpuCores;" +
        "LogicalProcessors=$logical;Gpus=$gpus} | ConvertTo-Json -Compress -Depth 4";

    public async Task<AetherSetupHardware> ReadAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Die Setup-Systemprüfung ist nur unter Windows verfügbar.");

        var (cpuName, cores, logicalProcessors, inventoryGpus) = await ReadCimInventoryAsync(token).ConfigureAwait(false);
        var gpus = await ReadNvidiaGpuMeasurementsAsync(inventoryGpus, token).ConfigureAwait(false);
        var windowsVersion = ReadWindowsVersion();
        var memory = ReadMemory();
        var storeRoot = AetherLocalAiStoreContract.RootPath;
        var driveRoot = Path.GetPathRoot(storeRoot) ?? throw new IOException("Laufwerk des AETHER-Modellstores konnte nicht bestimmt werden.");
        var freeBytes = new DriveInfo(driveRoot).AvailableFreeSpace;
        return new(windowsVersion, Environment.Is64BitOperatingSystem, cpuName, cores, logicalProcessors,
            memory.InstalledBytes, memory.AvailableBytes, gpus, freeBytes);
    }

    private static async Task<(string CpuName, int? Cores, int? LogicalProcessors, System.Collections.Immutable.ImmutableArray<AetherSetupGpu> Gpus)>
        ReadCimInventoryAsync(CancellationToken token)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Windows PowerShell für die lesende CIM-Systeminventur wurde nicht gefunden.", executable);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(InventoryScript);
        if (!process.Start())
            throw new Win32Exception("Windows PowerShell konnte für die Systeminventur nicht gestartet werden.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("Die Windows-CIM-Systeminventur hat das Zeitlimit überschritten.");
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (output.Length > MaximumInventoryBytes || process.ExitCode != 0)
            throw new InvalidDataException("Die Windows-CIM-Systeminventur ist fehlgeschlagen: " +
                (error.Length > 1024 ? error[..1024] : error));

        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        var cpuName = ReadString(root, "CpuName");
        if (string.IsNullOrWhiteSpace(cpuName))
            throw new InvalidDataException("Windows-CIM hat keinen CPU-Namen gemeldet.");
        var cores = ReadPositiveInt(root, "CpuCores");
        var logicalProcessors = ReadPositiveInt(root, "LogicalProcessors");
        if (logicalProcessors is null)
            throw new InvalidDataException("Windows-CIM hat keine positive Anzahl logischer Prozessoren gemeldet.");

        var gpus = System.Collections.Immutable.ImmutableArray.CreateBuilder<AetherSetupGpu>();
        if (root.TryGetProperty("Gpus", out var gpuValues))
        {
            var elements = gpuValues.ValueKind switch
            {
                JsonValueKind.Array => gpuValues.EnumerateArray(),
                JsonValueKind.Object => new[] { gpuValues }.AsEnumerable(),
                JsonValueKind.Null => [],
                _ => throw new InvalidDataException("Windows-CIM hat ein ungültiges GPU-Inventar geliefert.")
            };
            foreach (var gpu in elements)
            {
                var name = ReadString(gpu, "Name");
                if (!string.IsNullOrWhiteSpace(name))
                    gpus.Add(new(name, null, null, ReadString(gpu, "DriverVersion")));
            }
        }
        return (cpuName, cores, logicalProcessors, gpus.ToImmutable());
    }

    private static async Task<System.Collections.Immutable.ImmutableArray<AetherSetupGpu>> ReadNvidiaGpuMeasurementsAsync(
        System.Collections.Immutable.ImmutableArray<AetherSetupGpu> gpus, CancellationToken token)
    {
        if (!gpus.Any(gpu => gpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)))
            return gpus;

        var executable = FindNvidiaSmi();
        if (executable is null)
            return gpus;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--query-gpu=name,memory.total,memory.free,driver_version");
        process.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");
        if (!process.Start())
            throw new Win32Exception("NVIDIA-SMI konnte für die VRAM-Messung nicht gestartet werden.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("Die NVIDIA-SMI-VRAM-Messung hat das Zeitlimit überschritten.");
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (output.Length > 8192 || process.ExitCode != 0)
            throw new InvalidDataException("Die NVIDIA-SMI-VRAM-Messung ist fehlgeschlagen: " +
                (error.Length > 1024 ? error[..1024] : error));
        return AetherSetupGpuMeasurements.ApplyNvidiaSmi(gpus, output);
    }

    private static string? FindNvidiaSmi()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvidia-smi.exe")
        };
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            candidates.Add(Path.Combine(folder, "nvidia-smi.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static Version? ReadWindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
        if (key is null) return null;
        if (!int.TryParse(key.GetValue("CurrentMajorVersionNumber")?.ToString(), out var major) ||
            !int.TryParse(key.GetValue("CurrentMinorVersionNumber")?.ToString(), out var minor))
            return null;
        var buildText = key.GetValue("CurrentBuildNumber") as string;
        if (!int.TryParse(buildText, out var build) || build < 1) return null;
        return new Version(major, minor, build);
    }

    private static (long? InstalledBytes, long? AvailableBytes) ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Installierter und verfügbarer Arbeitsspeicher konnten nicht gemessen werden.");
        long? installedBytes = GetPhysicallyInstalledSystemMemory(out var installedKilobytes)
            ? checked((long)installedKilobytes * 1024)
            : null;
        return (installedBytes, checked((long)status.AvailablePhysical));
    }

    private static string? ReadString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadPositiveInt(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) && result > 0
            ? result
            : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
