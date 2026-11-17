using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SystemCockpit.Services;

internal static class GuardianTraceLog
{
    // Windows SDK evntrace.h, x64 EVENT_TRACE_LOGFILEW (448 bytes), TRACE_LOGFILE_HEADER at 120.
    [StructLayout(LayoutKind.Explicit, Size = 448)]
    private struct LogFile
    {
        [FieldOffset(0)] internal IntPtr Name;
        [FieldOffset(168)] internal uint EventsLost;
        [FieldOffset(396)] internal uint BuffersLost;
    }
    internal static (uint Events, uint Buffers) ReadLossCounters(string file, Action<string, uint>? step)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("ETL-Headervertrag benötigt den vorhandenen x64-Host.");
        var name = Marshal.StringToHGlobalUni(file);
        try
        {
            var log = new LogFile { Name = name };
            var handle = OpenTrace(ref log);
            var error = handle == ulong.MaxValue ? (uint)Marshal.GetLastWin32Error() : 0;
            step?.Invoke("OpenTraceHeader", error);
            if (error != 0) throw new Win32Exception((int)error, "OpenTraceHeader: " + new Win32Exception((int)error).Message);
            try { return (log.EventsLost, log.BuffersLost); }
            finally
            {
                var close = CloseTrace(handle);
                step?.Invoke("CloseTraceHeader", close);
                if (close != 0) throw new Win32Exception((int)close, "CloseTraceHeader: " + new Win32Exception((int)close).Message);
            }
        }
        finally { Marshal.FreeHGlobal(name); }
    }
    [DllImport("advapi32.dll", EntryPoint = "OpenTraceW", SetLastError = true)]
    private static extern ulong OpenTrace(ref LogFile log);
    [DllImport("advapi32.dll")]
    private static extern uint CloseTrace(ulong handle);
}
