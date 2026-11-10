using System.Text.Json;
using AetherOperations;
using SystemCockpit;
using SystemCockpit.Services;

internal static class HardwareCompletionTests
{
    private static int _assertions;
    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException("Hardware completion: " + message);
    }

    internal static int Run()
    {
        _assertions = 0;
        var time = DateTimeOffset.UtcNow;
        var reading = new SensorReading("sensor/1", "same model", "Temperature", "Temperature", 20, "°C")
            { ProviderDeviceId = "provider/device0", PnpDeviceId = @"PCI\EXACT\0" };
        var target = new HardwareComponent("gpu:fixture", HardwareClass.Gpu, "same model",
            [new("PNPDeviceID", "ID", SourceObservation<string>.Present(@"pci\exact\0", "",
                HardwareIntelligenceReader.Metadata("fixture", time, classification: DataClassification.Sensitive)))]);
        var sensors = HardwareSensorInventory.Read([reading], time, true, null);
        var correlated = HardwareSensorCorrelation.Apply(sensors, [reading], new(time, [target]), time);
        var device = correlated.Single(c => c.Id.StartsWith("sensor-device:", StringComparison.Ordinal));
        Check(device.ParentId == target.Id, "complete PnP ID matches case-insensitively");
        Check(correlated[0].ParentId == device.Id, "sensor uses actual provider device");
        Check(device.Facts.Single(f => f.Key == "correlation").Provenance == "Derived", "join provenance");
        Check(device.Facts.Single(f => f.Key == "provider-device").Privacy == "Sensitive", "provider identifier is sensitive");
        var report = HardwareReport.Export(new(time, [target, .. correlated]), "json", false);
        Check(!report.Contains("provider/device0") && !report.Contains(@"EXACT"), "join does not leak identifiers");
        var duplicate = HardwareSensorCorrelation.Apply(sensors, [reading], new(time, [target, target with { Id = "other" }]), time);
        Check(duplicate.Single(c => c.Id == device.Id).ParentId is null, "ambiguous target refused");
        var unmatched = HardwareSensorCorrelation.Apply(sensors, [reading with { PnpDeviceId = "different" }], new(time, [target]), time);
        Check(unmatched.Single(c => c.Id == device.Id).ParentId is null, "same name is not a match");
        var noIdentity = reading with { ProviderDeviceId = null, PnpDeviceId = null };
        Check(HardwareSensorCorrelation.Apply(sensors, [noIdentity], new(time, [target]), time).Count == sensors.Count,
            "legacy/imported sensor without device identity remains standalone");
        Check(!JsonSerializer.Serialize(reading).Contains("ProviderDeviceId"), "local provenance does not change feed wire format");
        var stats = new SessionStatistics();
        stats.AddSensors(time, [reading], false);
        var changed = reading with { ProviderDeviceId = "provider/device1", Value = 50 };
        stats.AddSensors(time.AddSeconds(1), [changed], false);
        Check(stats.ForSensor(reading) is null && stats.ForSensor(changed)?.Value.Count == 1, "device change resets statistics identity");
        return _assertions;
    }
}
