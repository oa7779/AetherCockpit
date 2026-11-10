using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SystemCockpit.Services;

internal static class ProcessFileInfo
{
    internal static void RequireLocal(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new NotSupportedException("Dateimetadaten nur von lokalen Laufwerken; keine Netzdateiabfrage.");
    }

    internal static BitmapSource? Icon(string path)
    {
        RequireLocal(path);
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        if (icon is null) return null;
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24));
        bitmap.Freeze();
        return bitmap;
    }

    internal static string Trust(string path)
    {
        RequireLocal(path);
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var initialized = false;
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
            File = ptr, StateAction = 1, ProviderFlags = 0x1010 };
        var policy = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(file, ptr, false); initialized = true;
            var status = WinVerifyTrust(new IntPtr(-1), ref policy, ref data);
            return status == 0 ? "Offline-Windows-Vertrauensprüfung bestanden (nur Cache; keine aktuelle Online-Sperrlistenprüfung)" :
                $"Nicht offline bestätigt; WinVerifyTrust 0x{unchecked((uint)status):X8} (kein Schadsoftwareurteil)";
        }
        finally
        {
            if (data.StateData != IntPtr.Zero)
            {
                data.StateAction = 2;
                var close = WinVerifyTrust(new IntPtr(-1), ref policy, ref data);
                if (close != 0) System.Diagnostics.Trace.TraceError("WinVerifyTrust cleanup: 0x{0:X8}", close);
            }
            if (initialized) Marshal.DestroyStructure<TrustFile>(ptr);
            Marshal.FreeHGlobal(ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr Handle, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public IntPtr PolicyCallback, SipClient;
        public uint UiChoice, RevocationChecks, UnionChoice; public IntPtr File;
        public uint StateAction; public IntPtr StateData, UrlReference;
        public uint ProviderFlags, UiContext; public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
}
