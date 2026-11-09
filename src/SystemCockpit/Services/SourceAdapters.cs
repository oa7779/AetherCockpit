using AetherOperations;

namespace SystemCockpit.Services;

public static class SourceAdapters
{
    public static SourceObservation<double> Observe(this SensorReading sensor, SourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(sensor);
        ArgumentNullException.ThrowIfNull(metadata);
        return sensor.Value is { } value && double.IsFinite(value)
            ? SourceObservation<double>.Present(value, sensor.Unit, metadata)
            : SourceObservation<double>.Missing(sensor.Unit, metadata);
    }

    public static OperationOutcome Outcome(this CommandResult result) =>
        OperationOutcome.FromProcess("CommandRunner", result.ExitCode, result.OutputTruncated);

    public static OperationOutcome Outcome(this ManagedActionResult result) =>
        OperationOutcome.FromProcess("ManagedActionRunner", result.ExitCode, result.Truncated);
}
