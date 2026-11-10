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
        var npu = HardwareIntelligenceReader.NpuClassification(JsonSerializer.SerializeToElement(new
            { PNPClass = "ComputeAccelerator", ClassGuid = "{f01a9d53-3ff6-48d2-9f97-c8a7004be10c}", Name = "NPU AI" }), time);
        Check(npu.Provenance == "Unconfirmed" && npu.Observation.Metadata.Confidence == DataConfidence.Unconfirmed, "class is not independent NPU evidence");
        Check(npu.Display.Contains("ComputeAccelerator-Klasse bestätigt"), "documented class GUID is checked");
        Check(!HardwareIntelligenceReader.NpuClassification(JsonSerializer.SerializeToElement(new { PNPClass = "GPU", Name = "NPU" }), time)
            .Display.Contains("Klasse bestätigt"), "name cannot establish NPU class");
        Check(HardwareCpuFrequency.ValidSample(0, 0) && HardwareCpuFrequency.ValidSample(1, 200), "PDH valid/new samples");
        Check(!HardwareCpuFrequency.ValidSample(2, 3000) && !HardwareCpuFrequency.ValidSample(0, double.NaN) &&
            !HardwareCpuFrequency.ValidSample(0, -1), "PDH invalid samples never become frequencies");
        var module = new SpdModule("fixture/module", new byte[512],
            HardwareIntelligenceReader.Metadata("fixture", time, classification: DataClassification.Sensitive), "fixture-license", new("https://example.org/spd"));
        Check(HardwareSpdBoundary.Valid(module), "bounded explicit SPD provider contract");
        Check(!HardwareSpdBoundary.Valid(module with { StableDeviceKey = "" }), "SPD requires exact device identity");
        Check(!HardwareSpdBoundary.Valid(module with { Bytes = new byte[32] }), "SPD invalid buffer refused");
        Check(!HardwareSpdBoundary.Valid(module with { Metadata = HardwareIntelligenceReader.Metadata("fixture", time) }), "SPD identifiers require privacy classification");
        Check(!HardwareSpdBoundary.Valid(module with { License = "" }), "SPD source license required");
        Check(HardwareDirect3D.ShaderName(0x69) == "6.9" && HardwareDirect3D.ShaderName(0x51) == "5.1", "shader model hex encoding");
        Check(HardwareDirect3D.ShaderName(0x70).Contains("Code"), "future shader models remain raw");
        Check(HardwareGpuDriver.Wddm(1105) == "WDDM 1.1" && HardwareGpuDriver.Wddm(1102).Contains("Prerelease"), "nondecimal WDDM 1.1 codes");
        Check(HardwareGpuDriver.Wddm(3200) == "WDDM 3.2" && HardwareGpuDriver.Wddm(2900) == "WDDM 2.9", "documented modern WDDM codes");
        Check(HardwareGpuDriver.Wddm(3250).Contains("3250") && HardwareGpuDriver.Wddm(1400).Contains("1400"), "unknown driver code not rounded into a WDDM claim");
        var trim = new byte[12];
        BitConverter.GetBytes(12u).CopyTo(trim, 4);
        trim[8] = 1;
        Check(HardwareStorageProperties.ParseTrim(trim, time)[0].Display == "True", "TRIM driver flag");
        trim[8] = 0;
        Check(HardwareStorageProperties.ParseTrim(trim, time)[0].Display == "False", "TRIM false is available");
        trim[8] = 2;
        try { HardwareStorageProperties.ParseTrim(trim, time); Check(false, "bad TRIM boolean accepted"); }
        catch (InvalidOperationException) { Check(true, "bad TRIM boolean refused"); }
        var storage = new byte[36];
        BitConverter.GetBytes(36u).CopyTo(storage, 4);
        BitConverter.GetBytes(17u).CopyTo(storage, 28);
        storage[10] = 1;
        var properties = HardwareStorageProperties.ParseDevice(storage, time);
        Check(properties[0].Display == "True" && properties[1].Display == "17", "removable and bus code remain separate");
        try { HardwareStorageProperties.ParseDevice(storage[..8], time); Check(false, "truncated descriptor accepted"); }
        catch (InvalidOperationException) { Check(true, "truncated descriptor refused"); }
        var storageRows = new Dictionary<string, object[]>
        {
            ["Disk"] = [new { _Identity = "disk", Number = 0 }],
            ["DiskDevice"] = [new { _Identity = "pnpdisk", Index = 0, ControllerIds = new[] { @"PCI\CONTROLLER" } }],
            ["ScsiController"] = [new { _Identity = "controller", PNPDeviceID = @"PCI\CONTROLLER" }]
        };
        IReadOnlyList<HardwareComponent> ParseStorage() => HardwareIntelligenceReader.Parse(HardwareClass.Storage,
            HardwareCimCatalog.Queries.Where(q => q.Class == HardwareClass.Storage).Select(q =>
                new HardwareCimResult(q.Key, time, DataAvailability.Available, null,
                    storageRows.GetValueOrDefault(q.Key, []).Select(o => JsonSerializer.SerializeToElement(o)).ToArray())).ToArray());
        var mapped = ParseStorage();
        Check(mapped.Single(c => c.Id == HardwareIntelligenceReader.Identity("DiskDevice", "pnpdisk")).ParentId ==
            HardwareIntelligenceReader.Identity("ScsiController", "controller"), "exact CIM controller association");
        Check(mapped.Single(c => c.Id == HardwareIntelligenceReader.Identity("Disk", "disk")).Facts.Single(f => f.Key == "ControllerIds")
            .Observation.Metadata is { SourceType: SourceType.Derived, Classification: DataClassification.Sensitive }, "chained disk controller provenance");
        storageRows["ScsiController"] = [storageRows["ScsiController"][0], new { _Identity = "duplicate", PNPDeviceID = @"PCI\CONTROLLER" }];
        Check(ParseStorage().Single(c => c.Id == HardwareIntelligenceReader.Identity("DiskDevice", "pnpdisk")).ParentId is null,
            "ambiguous controller identity refused");
        storageRows["Disk"] = [storageRows["Disk"][0], new { _Identity = "disk-other", Number = 0 }];
        Check(!ParseStorage().Single(c => c.Id == HardwareIntelligenceReader.Identity("Disk", "disk"))
            .Facts.Single(f => f.Key == "ControllerIds").Observation.HasValue, "duplicate disk numbers cannot borrow controller relation");
        Check(HardwareRuntimeReader.AudioFormatSupport(0, false, time).Display == "Exakt unterstützt", "shared exact format");
        Check(HardwareRuntimeReader.AudioFormatSupport(1, false, time).Display.Contains("Näherungsformat"), "shared closest format is not exact");
        Check(HardwareRuntimeReader.AudioFormatSupport(unchecked((int)0x88890008), true, time).Display == "Nicht unterstützt", "exclusive unsupported format is an actual result");
        Check(HardwareRuntimeReader.AudioFormatSupport(unchecked((int)0x88890004), true, time).State == "Unavailable", "invalidated endpoint is unavailable");
        Check(HardwareRuntimeReader.AudioFormatSupport(unchecked((int)0x80070005), false, time).State == "AccessDenied", "audio denied preserved");
        Check(HardwareRuntimeReader.AudioFormatSupport(1, true, time).State == "ProviderError", "unexpected exclusive result explicit");
        Check(HardwareDisplayConfig.ColorMode(2) == "HDR" && HardwareDisplayConfig.ColorMode(1) == "WCG", "distinct Windows HDR and WCG modes");
        Check(HardwareDisplayConfig.ColorMode(99).Contains("99"), "future color mode remains raw");
        foreach (var mode in new[] { "uint-valid", "bool-valid", "checksum-invalid", "bool-failed" })
        {
            var valid = mode is "uint-valid" or "bool-valid";
            var stub = """
                function Get-CimInstance {
                    [CmdletBinding()] param($Namespace,$ClassName,$OperationTimeoutSec,$Filter)
                    if($ClassName -eq 'WmiMonitorDescriptorMethods'){
                        $instance=[Microsoft.Management.Infrastructure.CimInstance]::new('WmiMonitorDescriptorMethods')
                        $instance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create(
                            'InstanceName','fixture-monitor',[Microsoft.Management.Infrastructure.CimFlags]::None))
                        $instance
                    }
                }
                function Invoke-CimMethod {
                    [CmdletBinding()] param($InputObject,$MethodName,$Arguments,$OperationTimeoutSec)
                    [byte[]]$bytes=New-Object byte[] 128
                    for($i=1;$i -le 6;$i++){$bytes[$i]=255}
                    $bytes[127]=6
                    @{ReturnValue=0;BlockContent=$bytes}
                }
                """;
            if (mode == "checksum-invalid") stub = stub.Replace("$bytes[127]=6", "$bytes[127]=7", StringComparison.Ordinal);
            if (mode.StartsWith("bool", StringComparison.Ordinal))
                stub = stub.Replace("ReturnValue=0", mode == "bool-valid" ? "ReturnValue=$true" : "ReturnValue=$false", StringComparison.Ordinal);
            var script = stub + "\n" + HardwareIntelligenceReader.Script(
                HardwareCimCatalog.Queries.Where(q => q.Class == HardwareClass.Display).ToArray(), HardwareClass.Display);
            var executed = AdminShell.RunAsync(script, TimeSpan.FromSeconds(15), CancellationToken.None).GetAwaiter().GetResult();
            Check(executed.ExitCode == 0 && !executed.OutputTruncated, "isolated EDID fixture script executes: " + executed.Error);
            var batches = JsonSerializer.Deserialize<HardwareCimResult[]>(executed.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var edid = HardwareIntelligenceReader.Parse(HardwareClass.Display, batches)
                .Single(c => c.Id.StartsWith("DisplayEdid:", StringComparison.Ordinal)).Facts;
            Check(valid ? edid.Single(f => f.Key == "EdidReadBlocks").Display == "1" :
                edid.Single(f => f.Key == "EdidBlocks").State == "ProviderError", "EDID valid block/checksum execution is not hidden in another provider branch");
            Check(!HardwareReport.Export(new(time, HardwareIntelligenceReader.Parse(HardwareClass.Display, batches)), "json", false)
                .Contains("00FFFFFFFFFFFF00", StringComparison.Ordinal), "raw EDID redacted in fixture export");
        }
        return _assertions;
    }
}
