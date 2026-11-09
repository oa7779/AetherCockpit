using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AetherOperations;
using SystemCockpit.Services;

internal static class CommandLifecycleTests
{
    public static int Fixture(string mode)
    {
        if (mode == "wait") { Thread.Sleep(TimeSpan.FromMinutes(1)); return 0; }
        var length = int.Parse(mode, System.Globalization.CultureInfo.InvariantCulture);
        Console.Write(new string('x', length));
        return 0;
    }

    public static int Run() => Task.Run(RunAsync).GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException(message);
        }
        var assembly = Assembly.GetExecutingAssembly().Location;
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host missing.");
        string[] Arguments(string mode) => Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [assembly, "--command-fixture", mode] : ["--command-fixture", mode];
        foreach (var length in new[] { CommandRunner.MaximumCapturedCharacters, CommandRunner.MaximumCapturedCharacters + 1 })
        {
            var result = await CommandRunner.RunAsync(executable, Arguments(length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                TimeSpan.FromSeconds(30), CancellationToken.None);
            Check(result.ExitCode == 0, "Output fixture exits successfully.");
            Check(result.OutputTruncated == (length > CommandRunner.MaximumCapturedCharacters), "Exact capture boundary is marked.");
            Check(result.Outcome().State == (result.OutputTruncated ? OperationState.Partial : OperationState.Success),
                "Partial output cannot masquerade as complete success.");
            Check(result.Output.Count(c => c == 'x') == CommandRunner.MaximumCapturedCharacters, "Capture retains exactly the permitted prefix.");
            Check(!JsonSerializer.Serialize(result).Contains("OutputTruncated", StringComparison.Ordinal), "Legacy command JSON unchanged.");
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await CommandRunner.RunAsync("nonexistent-fixture-executable", [], TimeSpan.FromSeconds(1), cancelled.Token);
            throw new InvalidOperationException("Pre-cancelled command started.");
        }
        catch (OperationCanceledException ex) { Check(ex.CancellationToken == cancelled.Token, "Pre-cancelled command never starts."); }
        try
        {
            await CommandRunner.RunAsync("nonexistent-fixture-executable", [], TimeSpan.FromSeconds(-2), CancellationToken.None);
            throw new InvalidOperationException("Invalid timeout accepted.");
        }
        catch (ArgumentOutOfRangeException) { count++; }
        var progress = new ProcessProgress();
        try
        {
            await CommandRunner.RunAsync(executable, Arguments("wait"), TimeSpan.FromSeconds(2), CancellationToken.None, progress);
            throw new InvalidOperationException("Expected process timeout.");
        }
        catch (TimeoutException) { count++; }
        Check(progress.ProcessId.HasValue, "Timeout fixture actually started.");
        Check(HasExited(progress.ProcessId!.Value), "Timeout waits for termination of its own process.");
        var failing = new ProcessProgress { Fail = true };
        try
        {
            await CommandRunner.RunAsync(executable, Arguments("wait"), TimeSpan.FromSeconds(30), CancellationToken.None, failing);
            throw new InvalidOperationException("Expected progress failure.");
        }
        catch (IOException) { count++; }
        Check(failing.ProcessId.HasValue && HasExited(failing.ProcessId.Value), "Progress failure cannot leak an already started process.");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var cancellationProgress = new ProcessProgress();
        try
        {
            await CommandRunner.RunAsync(executable, Arguments("wait"), Timeout.InfiniteTimeSpan, stop.Token, cancellationProgress);
            throw new InvalidOperationException("Expected caller cancellation.");
        }
        catch (OperationCanceledException ex) { Check(ex.CancellationToken == stop.Token, "Caller cancellation remains cancellation."); }
        Check(cancellationProgress.ProcessId.HasValue && HasExited(cancellationProgress.ProcessId.Value),
            "Cancellation waits for its own process, without promising rollback.");
        return count;
    }

    private static bool HasExited(int id)
    {
        try { using var process = Process.GetProcessById(id); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    private sealed class ProcessProgress : IProgress<string>
    {
        public int? ProcessId { get; private set; }
        public bool Fail { get; init; }
        public void Report(string value)
        {
            const string prefix = "[PROCESS] PID ";
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) return;
            ProcessId = int.Parse(value[prefix.Length..].Split(' ', 2)[0], System.Globalization.CultureInfo.InvariantCulture);
            if (Fail) throw new IOException("Synthetic progress failure.");
        }
    }
}
