using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using AetherOperations.Intelligence;

namespace SystemCockpit.Services;

internal sealed class LocalOllamaRuntimeException(string reason, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Reason { get; } = reason;
}

internal sealed class LocalOllamaRuntime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? _ownedServer;
    private static Uri? _ownedEndpoint;
    private static string? _ownedStore;
    private static DateTimeOffset? _ownedStartedAt;
    private readonly Uri _endpoint;
    private readonly string _root;
    private readonly string _modelStore;
    private readonly Func<string?> _findExecutable;
    private readonly Func<string, CancellationToken, Task<(bool Valid, string? Subject)>> _readSignature;

    public LocalOllamaRuntime() : this(new Uri("http://" + AetherLocalAiStoreContract.Host + "/"),
        AetherLocalAiStoreContract.RootPath, AetherOllamaExecutable.Find, AetherOllamaExecutable.ReadSignatureAsync)
    { }

    internal LocalOllamaRuntime(Uri endpoint, string root, Func<string?> findExecutable,
        Func<string, CancellationToken, Task<(bool Valid, string? Subject)>> readSignature)
    {
        CompatibleInferenceProvider.ValidateEndpoint(endpoint, false);
        _endpoint = endpoint;
        _root = Path.GetFullPath(root);
        _modelStore = Path.Combine(_root, "models");
        _findExecutable = findExecutable;
        _readSignature = readSignature;
    }

    public async Task<Uri> EnsureAvailableAsync(CompatibleInferenceProvider provider, FabricModel model,
        FabricRunDiagnostics? diagnostics, CancellationToken token)
    {
        var endpoint = provider.Info.Endpoint;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (endpoint is null)
                throw new LocalOllamaRuntimeException("EndpointUnavailable", "Der Ollama-Endpunkt fehlt.");
            if (endpoint != _endpoint && endpoint != _ownedEndpoint)
            {
                try { CompatibleInferenceProvider.ValidateEndpoint(endpoint, false); }
                catch (ArgumentException ex)
                { throw new LocalOllamaRuntimeException("EndpointUnavailable", "Der AETHER-Ollama-Endpunkt ist kein sicherer Loopback-Endpunkt.", ex); }
            }
            try { ValidateOwnedStore(model); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                KeyNotFoundException or InvalidOperationException or ArgumentException)
            { throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Der AETHER-Store-Herkunftsnachweis ist ungültig oder nicht lesbar.", ex); }
            if (_ownedServer is { HasExited: true })
            {
                RemoveRuntimeLease(_ownedStore);
                _ownedServer.Dispose();
                _ownedServer = null;
                _ownedEndpoint = null;
                _ownedStore = null;
                _ownedStartedAt = null;
            }
            if (_ownedServer is { } existing)
            {
                if (!string.Equals(_ownedStore, _modelStore, StringComparison.OrdinalIgnoreCase) || _ownedEndpoint is null)
                    throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Eine AETHER-Runtime für einen anderen Modellstore ist bereits aktiv.");
                using var existingProvider = new CompatibleInferenceProvider(provider.Info.Id, FabricProviderKind.Ollama, _ownedEndpoint,
                    runtimePolicy: provider.RuntimePolicy);
                EnsureLoopbackOnly(_ownedEndpoint.Port);
                if (!IsListenerOwnedBy(_ownedEndpoint.Port, existing.Id) || !await ApiReachableAsync(existingProvider, token).ConfigureAwait(false))
                    throw new LocalOllamaRuntimeException("EndpointUnavailable", "Die AETHER-Runtime ist nicht erreichbar oder besitzt den Listener nicht mehr.");
                diagnostics?.Update(d => { d.EndpointReachable = true; d.RuntimeStartedByAether = true;
                    d.RuntimeStoreVerified = true; d.RuntimeProcessId = existing.Id; d.OllamaEndpoint = _ownedEndpoint.ToString();
                    d.RuntimeHost = _ownedEndpoint.Authority; d.RuntimeModelStorePath = _modelStore;
                    d.RuntimeStartedAt = _ownedStartedAt; });
                return _ownedEndpoint;
            }

            var selectedEndpoint = HasListener(endpoint.Port) ? FreeLoopbackEndpoint() : endpoint;
            using var selectedProvider = new CompatibleInferenceProvider(provider.Info.Id, FabricProviderKind.Ollama, selectedEndpoint,
                runtimePolicy: provider.RuntimePolicy);
            diagnostics?.Update(d => { d.EndpointReachable = false; d.OllamaEndpoint = selectedEndpoint.ToString(); });

            var executable = _findExecutable();
            if (executable is null)
                throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Ollama ist nicht installiert oder nicht auffindbar.");
            diagnostics?.Update(d => d.RuntimeExecutablePath = executable);
            (bool Valid, string? Subject) signature;
            try { signature = await _readSignature(executable, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                Win32Exception or JsonException)
            { throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Die Ollama-Signaturprüfung ist fehlgeschlagen: " + ex.Message, ex); }
            if (!signature.Valid)
                throw new LocalOllamaRuntimeException("RuntimeUnavailable", $"Die Ollama-Programmdatei besitzt keine gültige offizielle Signatur: {executable} ({signature.Subject ?? "kein Unterzeichner"}).");
            var start = new ProcessStartInfo
            {
                FileName = executable, Arguments = "serve", WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false, CreateNoWindow = true
            };
            start.Environment[AetherLocalAiStoreContract.EnvironmentVariable] = _modelStore;
            start.Environment[AetherLocalAiStoreContract.HostEnvironmentVariable] = selectedEndpoint.Authority;
            start.Environment["OLLAMA_NO_CLOUD"] = "1";
            start.Environment["OLLAMA_NOPRUNE"] = "1";
            Process process;
            try { process = Process.Start(start) ?? throw new Win32Exception("Ollama konnte nicht gestartet werden."); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            { throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Ollama konnte nicht gestartet werden.", ex); }
            var startedAt = DateTimeOffset.UtcNow;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (process.HasExited)
                        throw new LocalOllamaRuntimeException("RuntimeUnavailable", $"Ollama wurde mit Exitcode {process.ExitCode} beendet.");
                    if (IsListenerOwnedBy(selectedEndpoint.Port, process.Id) && await ApiReachableAsync(selectedProvider, timeout.Token).ConfigureAwait(false))
                    {
                        EnsureLoopbackOnly(selectedEndpoint.Port);
                        WriteRuntimeLease(process, selectedEndpoint);
                        _ownedServer = process;
                        _ownedEndpoint = selectedEndpoint;
                        _ownedStore = _modelStore;
                        _ownedStartedAt = startedAt;
                        diagnostics?.Update(d => { d.EndpointReachable = true; d.RuntimeStartedByAether = true;
                            d.RuntimeStoreVerified = true; d.RuntimeProcessId = process.Id; d.OllamaEndpoint = selectedEndpoint.ToString();
                            d.RuntimeHost = selectedEndpoint.Authority; d.RuntimeModelStorePath = _modelStore;
                            d.RuntimeStartedAt = startedAt; });
                        return selectedEndpoint;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new LocalOllamaRuntimeException("EndpointUnavailable", "Die gestartete Ollama-API wurde nicht rechtzeitig bereit."); }
            finally
            {
                if (!ReferenceEquals(_ownedServer, process))
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    process.Dispose();
                }
            }
        }
        finally { Gate.Release(); }
    }

    internal static async Task StopOwnedAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var process = _ownedServer;
            var storePath = _ownedStore;
            _ownedServer = null;
            _ownedEndpoint = null;
            _ownedStore = null;
            _ownedStartedAt = null;
            if (process is null) return;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            finally
            {
                process.Dispose();
                RemoveRuntimeLease(storePath);
            }
        }
        finally { Gate.Release(); }
    }

    private static async Task<bool> ApiReachableAsync(CompatibleInferenceProvider provider, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { _ = await provider.ReadOllamaVersionAsync(timeout.Token).ConfigureAwait(false); return true; }
        catch (HttpRequestException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (JsonException) { return false; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
    }

    private void WriteRuntimeLease(Process process, Uri endpoint)
    {
        var path = Path.Combine(_root, AetherLocalAiStoreContract.RuntimeLeaseFileName);
        Directory.CreateDirectory(_root);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, new
                {
                    schema = 1,
                    processId = process.Id,
                    processStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                    executablePath = process.MainModule?.FileName,
                    endpoint = endpoint.ToString(),
                    modelStorePath = _modelStore
                }, FabricJson.Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RemoveRuntimeLease(string? storePath)
    {
        if (storePath is null) return;
        var root = Path.GetDirectoryName(storePath) ??
            throw new InvalidDataException("AETHER-Laufzeit-Verzeichnis konnte nicht bestimmt werden.");
        var path = Path.Combine(root, AetherLocalAiStoreContract.RuntimeLeaseFileName);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("AETHER Ollama-Runtime-Lease konnte nicht entfernt werden: {0}", ex.Message);
        }
    }

    private static bool HasListener(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port);

    private static Uri FreeLoopbackEndpoint()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (port != AetherLocalAiStoreContract.AcceptanceTestPort)
                    return new Uri($"http://127.0.0.1:{port}/");
            }
            finally { listener.Stop(); }
        }
        throw new LocalOllamaRuntimeException("EndpointUnavailable", "Kein freier Ollama-Loopback-Port außerhalb des Acceptance-Ports verfügbar.");
    }

    private static void EnsureLoopbackOnly(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(e => e.Port == port).ToArray();
        if (listeners.Length == 0 || listeners.Any(e => !IPAddress.IsLoopback(e.Address)))
            throw new LocalOllamaRuntimeException("EndpointUnavailable", "Der Ollama-Listener ist nicht ausschließlich auf Loopback begrenzt.");
    }

    internal static bool IsListenerOwnedBy(int port, int processId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var size = 0;
            var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
            if (result != 122) throw new Win32Exception((int)result);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, 2, 5, 0);
                if (result == 122) continue;
                if (result != 0) throw new Win32Exception((int)result);
                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<TcpRow>();
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<TcpRow>(IntPtr.Add(buffer, 4 + index * rowSize));
                    if (row.State == 2 && (ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort) == port &&
                        row.ProcessId == processId)
                        return true;
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new Win32Exception(122, "Die TCP-Prozesszuordnung änderte sich während der Prüfung.");
    }

    private void ValidateOwnedStore(FabricModel model)
    {
        var expected = _modelStore;
        var file = Path.Combine(_root, AetherLocalAiStoreContract.OwnershipFileName);
        if (!Directory.Exists(expected) || !File.Exists(file))
            throw new LocalOllamaRuntimeException("ModelMissing", "Der freigegebene AETHER-Modellstore oder sein Herkunftsnachweis fehlt.");
        for (var item = new DirectoryInfo(expected); item is not null; item = item.Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Der AETHER-Modellstore enthält eine nicht freigegebene Umleitung.");
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Der Store-Herkunftsnachweis ist umgeleitet.");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 65536)
            throw new LocalOllamaRuntimeException("RuntimeUnavailable", "Der Store-Herkunftsnachweis überschreitet das Größenlimit.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
        var owner = document.RootElement;
        if (owner.GetProperty("schema").GetInt32() != 1 ||
            !string.Equals(Path.GetFullPath(owner.GetProperty("modelStorePath").GetString()!), expected, StringComparison.OrdinalIgnoreCase) ||
            !owner.GetProperty("models").EnumerateArray().Any(item =>
                item.GetProperty("modelId").GetString() == model.Id &&
                string.Equals(item.GetProperty("digest").GetString(), model.Digest, StringComparison.OrdinalIgnoreCase)))
            throw new LocalOllamaRuntimeException("ModelMissing", "Das Modell ist im AETHER-Store nicht mit dem erwarteten Digest freigegeben.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, ProcessId;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
}
