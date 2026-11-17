using AetherOperations;

namespace SystemCockpit.Services;

internal sealed record SpdModule(string StableDeviceKey, ReadOnlyMemory<byte> Bytes, SourceMetadata Metadata,
    string License, Uri Documentation);

internal interface IHardwareSpdProvider
{
    Task<IReadOnlyList<SpdModule>> ReadAsync(CancellationToken token);
}

internal static class HardwareSpdBoundary
{
    internal static HardwareComponent Describe(DateTimeOffset time) => new("spd:boundary", HardwareClass.Memory,
        "SPD / JEDEC / XMP / EXPO — Zugriffsgrenze",
        [HardwareFact.Missing("spd", "Rohe SPD-Daten / Profile", "Windows SMBIOS / vorhandene LibreHardwareMonitor-Abhängigkeit", time,
            "Win32_PhysicalMemory und LibreHardwareMonitor SMBios liefern Firmwareangaben, keinen rohen SPD-Zugriff. " +
            "Kein dokumentierter allgemeiner Windows-Benutzerkontextpfad für DIMM-SPD integriert. " +
            "Optionaler Providervertrag vorbereitet; nicht aktiviert. Kein SMBus-/Kernelzugriff oder Treiberinstallation.", DataAvailability.Unsupported)]);

    internal static bool Valid(SpdModule module) =>
        !string.IsNullOrWhiteSpace(module.StableDeviceKey) && module.Bytes.Length is >= 128 and <= 4096 &&
        module.Metadata.SourceType == SourceType.Direct && module.Metadata.Availability == DataAvailability.Available &&
        module.Metadata.Timestamp != default && module.Metadata.Classification == DataClassification.Sensitive &&
        !string.IsNullOrWhiteSpace(module.License) && module.Documentation.IsAbsoluteUri &&
        module.Documentation.Scheme == Uri.UriSchemeHttps;
}
