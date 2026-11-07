using System.IO;
using LibreHardwareMonitor.Hardware;

namespace SystemCockpit.Services;

public sealed class HardwareSensors : IDisposable
{
    private Computer? _computer;
    // Einmalige lesende Pruefung: erklaert fehlenden CPU-Kernelzugriff (HVCI vs. PawnIO) ohne Messung.
    private static readonly Lazy<string> KernelAccess = new(DescribeKernelAccess);
    public static string KernelAccessHint => KernelAccess.Value;

    private static string DescribeKernelAccess()
    {
        try
        {
            using var pawnService = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            var pawnIo = pawnService is not null || File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll"));
            using var hvciKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            var hvci = hvciKey?.GetValue("Enabled") is int enabled && enabled == 1;
            if (pawnIo)
                return "Kernelzugriff: PawnIO-Treiber ist installiert; LibreHardwareMonitor kann ihn auch bei aktiver Kernisolierung nutzen.";
            return hvci
                ? "Kernelzugriff: Kernisolierung/Speicherintegritaet (HVCI) ist aktiv und der PawnIO-Treiber fehlt. " +
                  "Der klassische WinRing0-Treiber steht auf der Microsoft-Blockliste verwundbarer Treiber und kann nicht laden; " +
                  "CPU-Temperatur und CPU-Package-Leistung bleiben dann 'Nicht verfuegbar'. " +
                  "Loesung ohne Schutzabschaltung: den signierten PawnIO-Treiber installieren (https://pawnio.eu), danach AETHER neu starten. " +
                  "Kernisolierung nicht abschalten."
                : "Kernelzugriff: PawnIO-Treiber fehlt; es wird der klassische Treiberweg versucht. " +
                  "Fehlen CPU-Werte dauerhaft, den signierten PawnIO-Treiber installieren (https://pawnio.eu).";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return "Kernelzugriff: Status nicht lesbar: " + ex.Message;
        }
    }

    public void Open()
    {
        if (_computer is not null) return;
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true
        };
        try
        {
            computer.Open();
            _computer = computer;
        }
        catch
        {
            computer.Close();
            throw;
        }
    }

    public List<SensorReading> Read()
    {
        var result = new List<SensorReading>();
        if (_computer is null) return result;
        foreach (var hardware in _computer.Hardware) ReadHardware(hardware, result);
        return result;
    }

    public string Report() => "[KERNELZUGRIFF] " + KernelAccessHint + Environment.NewLine + Environment.NewLine +
        (_computer?.GetReport() ?? "Hardwareprovider ist nicht geoeffnet. Sensorzugriff im Cockpit aktivieren.");

    private static void ReadHardware(IHardware hardware, List<SensorReading> readings)
    {
        hardware.Update();
        var cpuClockReadable = hardware.HardwareType != HardwareType.Cpu ||
            hardware.Sensors.Any(s => s.SensorType == SensorType.Clock && s.Value > 0);
        foreach (var sensor in hardware.Sensors)
            readings.Add(SensorSemantics.Classify(new(sensor.Identifier.ToString(), hardware.Name, sensor.Name,
                sensor.SensorType.ToString(),
                NormalizeValue(sensor.Value, hardware.HardwareType == HardwareType.Cpu,
                    sensor.SensorType == SensorType.Power, cpuClockReadable), Unit(sensor.SensorType))));
        foreach (var child in hardware.SubHardware) ReadHardware(child, readings);
    }

    public static double? NormalizeValue(float? value, bool cpu, bool power, bool cpuClockReadable)
    {
        if (value is not { } number || !float.IsFinite(number)) return null;
        // Unreadable CPU clocks accompany uninitialized zero RAPL readings on this system.
        if (cpu && power && number == 0 && !cpuClockReadable) return null;
        return number;
    }

    private static string Unit(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => "%",
        SensorType.Clock => "MHz",
        SensorType.Fan => "RPM",
        SensorType.Voltage => "V",
        SensorType.Power => "W",
        SensorType.Current => "A",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.Flow => "L/h",
        SensorType.TimeSpan => "s",
        SensorType.Energy => "mWh",
        _ => ""
    };

    public void Dispose()
    {
        _computer?.Close();
        _computer = null;
    }
}
