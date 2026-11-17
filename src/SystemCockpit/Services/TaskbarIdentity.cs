using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace SystemCockpit.Services;

internal static class TaskbarIdentity
{
    internal const string AppUserModelId = "AETHER.SystemControl";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out nint appId);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, in Guid iid, out nint store);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern nint SetClassLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CopyImage(nint image, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);
        [PreserveSig] int Commit();
    }

    private static readonly Guid PropertyStoreId = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    private static readonly PropertyKey AppIdKey = new(new Guid("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), 5);
    // Class icons must remain valid while any window of the class exists.
    private static readonly List<nint> ClassIcons = [];
    internal sealed record WindowDiagnostic(DateTimeOffset Timestamp, int ProcessId, string? ProcessPath,
        string Hwnd, string? ProcessAppId, string? WindowAppId, string BigIcon, string SmallIcon,
        string SmallIcon2, string ClassBigIcon, string ClassSmallIcon, string? IconPath, bool ShowInTaskbar)
    {
        public string? Stage { get; init; }
    }

    internal static int SetForCurrentProcess() => SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

    internal static string? GetForCurrentProcess()
    {
        var result = GetCurrentProcessExplicitAppUserModelID(out var appId);
        if (result < 0)
        {
            if (appId != 0) Marshal.FreeCoTaskMem(appId);
            Marshal.ThrowExceptionForHR(result);
        }
        try { return Marshal.PtrToStringUni(appId); }
        finally { if (appId != 0) Marshal.FreeCoTaskMem(appId); }
    }

    private static string? WindowAppId(nint hwnd, bool set)
    {
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, PropertyStoreId, out var pointer));
        if (pointer == 0) throw new COMException("Der Fenstereigenschaftsspeicher ist nicht verfügbar.");
        var store = (IPropertyStore)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        try
        {
            if (set)
            {
                var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(AppUserModelId) };
                try
                {
                    Marshal.ThrowExceptionForHR(store.SetValue(AppIdKey, value));
                    Marshal.ThrowExceptionForHR(store.Commit());
                }
                finally { PropVariantClear(ref value); }
            }
            Marshal.ThrowExceptionForHR(store.GetValue(AppIdKey, out var current));
            try { return current.Type == 31 ? Marshal.PtrToStringUni(current.Pointer) : null; }
            finally { PropVariantClear(ref current); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    internal static WindowDiagnostic Capture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) throw new InvalidOperationException("Das Fenster besitzt noch kein HWND.");
        static string Hex(nint value) => $"0x{value.ToInt64():X}";
        return new(DateTimeOffset.UtcNow, Environment.ProcessId, Environment.ProcessPath, Hex(hwnd),
            GetForCurrentProcess(), WindowAppId(hwnd, false), Hex(SendMessageW(hwnd, 0x7F, 1, 0)),
            Hex(SendMessageW(hwnd, 0x7F, 0, 0)), Hex(SendMessageW(hwnd, 0x7F, 2, 0)),
            Hex(GetClassLongPtr(hwnd, -14)), Hex(GetClassLongPtr(hwnd, -34)),
            window.Icon?.ToString(), window.ShowInTaskbar);
    }

    internal static WindowDiagnostic EnsureWindowIdentity(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) throw new InvalidOperationException("Das Fenster besitzt noch kein HWND.");
        if (WindowAppId(hwnd, false) != AppUserModelId) WindowAppId(hwnd, true);
        var big = SendMessageW(hwnd, 0x7F, 1, 0);
        var small = SendMessageW(hwnd, 0x7F, 0, 0);
        if (big == 0 || small == 0)
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                ?? throw new InvalidDataException("Das Prozesssymbol ist nicht verfügbar.");
            var ownedIcons = new List<nint>();
            window.Closed += (_, _) => { foreach (var handle in ownedIcons) DestroyIcon(handle); };
            foreach (var (kind, width, height) in new[]
                { (1, GetSystemMetrics(11), GetSystemMetrics(12)), (0, GetSystemMetrics(49), GetSystemMetrics(50)) })
            {
                if (kind == 1 && big != 0 || kind == 0 && small != 0) continue;
                var handle = CopyImage(icon.Handle, 1, width, height, 0);
                if (handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                ownedIcons.Add(handle);
                SendMessageW(hwnd, 0x80, kind, handle);
            }
        }
        lock (ClassIcons)
        {
            foreach (var (index, kind, width, height) in new[]
                { (-14, 1, GetSystemMetrics(11), GetSystemMetrics(12)), (-34, 0, GetSystemMetrics(49), GetSystemMetrics(50)) })
            {
                if (GetClassLongPtr(hwnd, index) != 0) continue;
                var source = SendMessageW(hwnd, 0x7F, kind, 0);
                if (source == 0) throw new InvalidDataException("Das Fenstersymbol ist nicht verfügbar.");
                var handle = CopyImage(source, 1, width, height, 0);
                if (handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Marshal.SetLastPInvokeError(0);
                var previous = SetClassLongPtr(hwnd, index, handle);
                var error = Marshal.GetLastPInvokeError();
                if (previous == 0 && GetClassLongPtr(hwnd, index) != handle)
                {
                    DestroyIcon(handle);
                    throw new System.ComponentModel.Win32Exception(error,
                        $"Klassensymbol konnte nicht gesetzt werden: Index {index}, HWND 0x{hwnd.ToInt64():X}.");
                }
                if (previous != 0)
                {
                    SetClassLongPtr(hwnd, index, previous);
                    if (GetClassLongPtr(hwnd, index) != previous)
                    {
                        ClassIcons.Add(handle);
                        throw new InvalidOperationException($"Das vorherige Klassensymbol für Index {index} konnte nicht wiederhergestellt werden.");
                    }
                    DestroyIcon(handle);
                    continue;
                }
                ClassIcons.Add(handle);
            }
        }
        return Capture(window);
    }

    internal static void WriteDiagnostic(WindowDiagnostic diagnostic)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AETHER", "IntelligenceFabric", "diagnostics");
        EvidenceFile.RequireLocalDirectory(directory);
        Directory.CreateDirectory(directory);
        EvidenceFile.RequireLocalDirectory(directory);
        var path = Path.Combine(directory, $"taskbar-{diagnostic.ProcessId}-{Guid.NewGuid():N}.json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, diagnostic);
    }
}
