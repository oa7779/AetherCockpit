using System.ComponentModel;
using System.Net.Http;
using System.Security;

namespace AetherOperations;

public enum OperationState { Success, Partial, Failed, Cancelled, TimedOut, Unavailable, AccessDenied, Unsupported, Unknown }
public enum FailureKind { None, Process, MissingTool, Access, Provider, Network, Timeout, Cancellation, Unknown }

public sealed record OperationOutcome(OperationState State, string Source, string Code,
    FailureKind Failure = FailureKind.None, int? ExitCode = null, int? HResult = null)
{
    public static OperationOutcome FromProcess(string source, int exitCode, bool truncated) =>
        new(exitCode != 0 ? OperationState.Failed : truncated ? OperationState.Partial : OperationState.Success,
            source, exitCode != 0 ? "process-exit" : truncated ? "output-truncated" : "completed",
            exitCode != 0 ? FailureKind.Process : FailureKind.None, exitCode);

    public static OperationOutcome FromException(Exception exception, string source, CancellationToken callerToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var (state, kind, code) = exception switch
        {
            OperationCanceledException when callerToken.IsCancellationRequested =>
                (OperationState.Cancelled, FailureKind.Cancellation, "caller-cancelled"),
            TimeoutException => (OperationState.TimedOut, FailureKind.Timeout, "deadline-expired"),
            UnauthorizedAccessException or SecurityException =>
                (OperationState.AccessDenied, FailureKind.Access, "access-denied"),
            Win32Exception { NativeErrorCode: 5 } =>
                (OperationState.AccessDenied, FailureKind.Access, "access-denied"),
            FileNotFoundException => (OperationState.Unavailable, FailureKind.MissingTool, "file-unavailable"),
            PlatformNotSupportedException or NotSupportedException =>
                (OperationState.Unsupported, FailureKind.Provider, "unsupported"),
            HttpRequestException => (OperationState.Failed, FailureKind.Network, "network-failed"),
            OperationCanceledException => (OperationState.Unknown, FailureKind.Unknown, "cancellation-origin-unknown"),
            _ => (OperationState.Failed, FailureKind.Provider, "operation-failed")
        };
        return new(state, source, code, kind, HResult: exception.HResult);
    }
}
