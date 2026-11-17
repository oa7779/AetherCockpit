using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;
using SystemCockpit.Services;

internal sealed class GuardianCpuPrivilege : IDisposable
{
    internal sealed record Entry(string Step, bool Succeeded, uint? NativeCode, string Detail, bool? NativeReturn = null,
        string? NativePreviousState = null);
    internal interface IToken : IDisposable
    {
        uint Adjust(bool enabled, out bool previousEnabled);
        GuardianTraceProfiles.ProfilePrivilege Read();
        bool? LastNativeReturn => null;
        string? LastPreviousState => null;
    }
    private readonly IToken _token;
    private readonly IList<Entry> _journal;
    private readonly bool _previous;
    private bool _restored;
    private GuardianCpuPrivilege(IToken token, IList<Entry> journal, bool previous)
    { _token = token; _journal = journal; _previous = previous; }
    internal static GuardianCpuPrivilege Enable(bool explicitlyRequested, IList<Entry> journal, IToken? fixture = null)
    {
        if (!explicitlyRequested) throw new InvalidOperationException("Explizite --enable-present-profile-privilege-Zustimmung fehlt.");
        if (fixture is null)
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.ImpersonationLevel != TokenImpersonationLevel.None)
                throw new UnauthorizedAccessException("CPU-Privilegtest akzeptiert keinen Impersonationskontext.");
            GuardianTraceProfiles.RequireAdministrator();
        }
        var token = fixture ?? new NativeToken();
        var changed = false; var previous = false;
        try
        {
            var before = token.Read();
            journal.Add(new("ReadBefore", before.NativeCode == 0, before.NativeCode, $"Present={before.Present}; Enabled={before.Enabled}; nur aktueller Prozess"));
            if (before.NativeCode != 0) throw new Win32Exception((int)before.NativeCode);
            if (before.Present != true) throw new UnauthorizedAccessException("SeSystemProfilePrivilege fehlt im Token; keine Rechte werden hinzugefügt.");
            var code = token.Adjust(true, out previous);
            changed = code == 0;
            journal.Add(new("AdjustTokenPrivileges(enable)", code == 0, code, code == 1300 ? "ERROR_NOT_ALL_ASSIGNED; keine Freigabe" : "Nur vorhandenes SeSystemProfilePrivilege",
                token.LastNativeReturn, token.LastPreviousState));
            if (code != 0) throw new Win32Exception((int)code);
            var after = token.Read();
            journal.Add(new("ReadEnabled", after.Enabled == true && after.NativeCode == 0, after.NativeCode, $"Enabled={after.Enabled}"));
            if (after.Enabled != true || after.NativeCode != 0) throw new InvalidOperationException("Privilegaktivierung nicht nachgelesen.");
            return new(token, journal, previous);
        }
        catch
        {
            try
            {
                if (changed)
                {
                    var restore = token.Adjust(previous, out _);
                    journal.Add(new("RestoreAfterFailedEnable", restore == 0, restore, $"PreviousEnabled={previous}", token.LastNativeReturn));
                    if (restore != 0) throw new Win32Exception((int)restore, "Privilegrücksetzung nach Fehler fehlgeschlagen.");
                    VerifyRestored(token, previous, journal);
                }
            }
            finally { Close(token, journal); }
            throw;
        }
    }
    public void Dispose()
    {
        if (_restored) return;
        _restored = true;
        try
        {
            var code = _token.Adjust(_previous, out _);
            _journal.Add(new("AdjustTokenPrivileges(restore)", code == 0, code, $"PreviousEnabled={_previous}", _token.LastNativeReturn));
            if (code != 0) throw new Win32Exception((int)code, "Privilegrücksetzung fehlgeschlagen; Testhost beenden.");
            VerifyRestored(_token, _previous, _journal);
        }
        finally { Close(_token, _journal); }
    }
    private static void VerifyRestored(IToken token, bool previous, IList<Entry> journal)
    {
        var state = token.Read();
        var verified = state.NativeCode == 0 && state.Present == true && state.Enabled == previous;
        journal.Add(new("ReadRestored", verified, state.NativeCode, $"Enabled={state.Enabled}; erwartet {previous}"));
        if (!verified) throw new InvalidOperationException("Vorheriger Privilegzustand nicht bestätigt; Testhost beenden.");
    }
    private static void Close(IToken token, IList<Entry> journal)
    {
        token.Dispose();
        journal.Add(new("TokenHandleClosed", true, null, "Eigenes Tokenhandle freigegeben; verwaltete SafeHandle-Prüfung, kein nativer Ergebniscode"));
    }
    internal static uint CheckAdjustment(bool returned, uint nativeCode)
    {
        if (!returned && nativeCode == 0)
            throw new InvalidOperationException("AdjustTokenPrivileges lieferte FALSE ohne nativen Fehlercode; kein Erfolg angenommen.");
        return nativeCode;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { internal uint Low; internal int High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Privileges { internal uint Count; internal Luid Id; internal uint Attributes; }
    private sealed class NativeToken : IToken
    {
        public bool? LastNativeReturn { get; private set; }
        public string? LastPreviousState { get; private set; }
        private readonly SafeAccessTokenHandle _handle;
        private readonly Luid _id;
        internal NativeToken()
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out _handle))
            {
                var error = Marshal.GetLastWin32Error();
                _handle.Dispose();
                throw new Win32Exception(error);
            }
            try
            {
                if (!LookupPrivilegeValue(null, "SeSystemProfilePrivilege", out _id))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch { _handle.Dispose(); throw; }
        }
        public GuardianTraceProfiles.ProfilePrivilege Read() => GuardianTraceProfiles.ReadProfilePrivilege();
        public uint Adjust(bool enabled, out bool previousEnabled)
        {
            var value = new Privileges { Count = 1, Id = _id, Attributes = enabled ? 2u : 0 };
            Marshal.SetLastPInvokeError(0);
            var ok = AdjustTokenPrivileges(_handle, false, ref value, (uint)Marshal.SizeOf<Privileges>(), out var previous, out _);
            var code = (uint)Marshal.GetLastWin32Error();
            LastNativeReturn = ok;
            LastPreviousState = $"Count={previous.Count}; LUID={previous.Id.High}:{previous.Id.Low}; Attributes=0x{previous.Attributes:X8}; requested Enabled={enabled}";
            previousEnabled = previous.Count == 0 ? enabled : (previous.Attributes & 2) != 0;
            return CheckAdjustment(ok, code);
        }
        public void Dispose()
        {
            _handle.Dispose();
            if (!_handle.IsClosed) throw new InvalidOperationException("Eigenes Tokenhandle nicht geschlossen.");
        }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref Privileges state, uint length, out Privileges previous, out uint returnedLength);
}
