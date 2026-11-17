using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using AetherOperations;

namespace SystemCockpit.Services;

internal static class HardwareStorageProperties
{
    private const string Source = "Windows IOCTL_STORAGE_QUERY_PROPERTY / geöffneter MSFT_Disk.Path";
    internal static IReadOnlyList<HardwareComponent> Read(IReadOnlyList<HardwareComponent> components, CancellationToken token) =>
        components.Select(disk =>
        {
            token.ThrowIfCancellationRequested();
            if (!disk.Id.StartsWith("Disk:", StringComparison.Ordinal)) return disk;
            var time = DateTimeOffset.UtcNow;
            var path = Field(disk, "Path");
            var number = Field(disk, "Number");
            IReadOnlyList<HardwareFact> facts;
            if (path is null || !uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                components.Count(c => c.Id.StartsWith("Disk:", StringComparison.Ordinal) &&
                    (Field(c, "Number") == number || string.Equals(Field(c, "Path"), path, StringComparison.OrdinalIgnoreCase))) != 1)
                facts = [HardwareFact.Missing("trim", "Windows TRIM-Freigabe", Source, time, "Kein eindeutiger verfügbarer Diskpfad mit Nummer.")];
            else
            {
                try
                {
                    using var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var identity = Ioctl(handle, 0x2d1080, [], 12);
                    if (identity.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(identity) != 7 ||
                        BinaryPrimitives.ReadUInt32LittleEndian(identity.AsSpan(4)) != index)
                        throw new InvalidOperationException("Geöffneter Storagepfad gehört nicht zur angefragten Windows-Disknummer.");
                    token.ThrowIfCancellationRequested();
                    var values = new List<HardwareFact>();
                    foreach (var property in new uint[] { 8, 0 })
                    {
                        token.ThrowIfCancellationRequested();
                        try { values.AddRange(property == 8 ? ParseTrim(Query(handle, property), time) : ParseDevice(Query(handle, property), time)); }
                        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                        { values.Add(Failure(property == 8 ? "trim" : "device", ex, time)); }
                    }
                    token.ThrowIfCancellationRequested();
                    facts = values;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    facts = [Failure("storage-properties", ex, time)];
                }
            }
            return disk with { Facts = disk.Facts.Concat(facts).ToArray() };
        }).ToArray();

    private static string? Field(HardwareComponent disk, string key) =>
        disk.Facts.FirstOrDefault(f => f.Key == key)?.Observation is { HasValue: true, Metadata.Availability: DataAvailability.Available } field
            ? field.Value : null;

    private static HardwareFact Failure(string key, Exception ex, DateTimeOffset time)
    {
        System.Diagnostics.Trace.TraceError("Hardware Storage-Eigenschaften {0}: {1}", key, ex);
        var code = ex is Win32Exception native ? native.NativeErrorCode : ex.HResult;
        return HardwareFact.Missing(key, "Windows Storage-Eigenschaften / " + key, Source, time,
            $"Lesende Treiberabfrage fehlgeschlagen (Code 0x{code:X8}).",
            code == 5 ? DataAvailability.AccessDenied : code is 1 or 50 or 87 ? DataAvailability.Unsupported :
            code is 21 or 1167 ? DataAvailability.Unavailable : DataAvailability.ProviderError);
    }

    internal static IReadOnlyList<HardwareFact> ParseTrim(byte[] trim, DateTimeOffset time)
    {
        Validate(trim, 9);
        if (trim[8] > 1) throw new InvalidOperationException("Ungültige Windows-TRIM-Booleanangabe.");
        return [Fact("trim", "Windows-Treiber meldet TrimEnabled (kein Ausführungsnachweis)", (trim[8] == 1).ToString(), time)];
    }

    internal static IReadOnlyList<HardwareFact> ParseDevice(byte[] device, DateTimeOffset time)
    {
        Validate(device, 36);
        if (device[10] > 1) throw new InvalidOperationException("Ungültige Windows-Storage-Booleanangabe.");
        return
        [
            Fact("removable", "Windows-Treiber meldet Wechselmedium", (device[10] == 1).ToString(), time),
            Fact("transport", "Windows Storage BusType (Code)", BinaryPrimitives.ReadUInt32LittleEndian(device.AsSpan(28)).ToString(CultureInfo.InvariantCulture), time)
        ];
    }

    private static HardwareFact Fact(string key, string name, string value, DateTimeOffset time) =>
        new(key, name, SourceObservation<string>.Present(value, "", HardwareIntelligenceReader.Metadata(Source, time)), HardwareCadence.Dynamic);

    private static void Validate(byte[] bytes, int minimum)
    {
        if (bytes.Length < minimum || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) < minimum ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) > bytes.Length)
            throw new InvalidOperationException("Unvollständiger Windows-Storage-Deskriptor.");
    }

    private static byte[] Query(SafeFileHandle handle, uint property)
    {
        var input = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(input, property);
        var header = Ioctl(handle, 0x2d1400, input, 8);
        if (header.Length < 8) throw new InvalidOperationException("Windows-Storage-Header fehlt.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (size is < 8 or > 65536) throw new InvalidOperationException("Windows-Storage-Deskriptor überschreitet die Abfragegrenze.");
        return Ioctl(handle, 0x2d1400, input, checked((int)size));
    }

    private static byte[] Ioctl(SafeFileHandle handle, uint code, byte[] input, int size)
    {
        var output = new byte[size];
        if (!DeviceIoControl(handle, code, input, input.Length, output, output.Length, out var bytes, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (bytes > output.Length) throw new InvalidOperationException("Ungültige Windows-Storage-Antwortlänge.");
        return output.AsSpan(0, checked((int)bytes)).ToArray();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        byte[] output, int outputSize, out uint bytes, IntPtr overlapped);
}
