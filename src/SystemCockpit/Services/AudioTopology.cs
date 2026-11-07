using System.Runtime.InteropServices;

namespace SystemCockpit.Services;

// Read session state only; no audio capture, playback, volume changes or process names.
internal static class AudioTopology
{
    internal static IReadOnlyList<BoardDevice> Read()
    {
        var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
        try
        {
            var defaults = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var flow in new[] { 0, 1 })
                for (var role = 0; role < 3; role++)
                {
                    var hr = enumerator.GetDefaultAudioEndpoint(flow, role, out var device);
                    if (hr == unchecked((int)0x80070490)) continue; // No default device for this role.
                    Marshal.ThrowExceptionForHR(hr);
                    try
                    {
                        device.GetId(out var id);
                        if (!defaults.TryGetValue(id, out var roles)) defaults[id] = roles = [];
                        roles.Add((flow == 0 ? "Wiedergabe" : "Aufnahme") + " / " +
                            new[] { "Konsole", "Multimedia", "Kommunikation" }[role]);
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
            enumerator.EnumAudioEndpoints(2, 15, out var collection);
            try
            {
                collection.GetCount(out var count);
                var result = new List<BoardDevice>();
                for (uint index = 0; index < count; index++)
                {
                    collection.Item(index, out var device);
                    try
                    {
                        device.GetId(out var id);
                        device.GetState(out var state);
                        device.OpenPropertyStore(0, out var properties);
                        string name;
                        try
                        {
                            var key = new PropertyKey { Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                            properties.GetValue(ref key, out var value);
                            try
                            {
                                name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer)
                                    ?? throw new InvalidOperationException("Audio-Geraetename ist leer.")
                                    : throw new InvalidOperationException("Audio-Geraetename hat einen unerwarteten Typ.");
                            }
                            finally { Marshal.ThrowExceptionForHR(PropVariantClear(ref value)); }
                        }
                        finally { Marshal.ReleaseComObject(properties); }
                        defaults.TryGetValue(id, out var roles);
                        var connected = (state & 1) != 0;
                        bool? inUse = false;
                        var sessionInfo = "";
                        if (connected)
                        {
                            try { inUse = HasActiveSession(device); }
                            catch (COMException ex)
                            {
                                inUse = null;
                                sessionInfo = $"Sitzungsstatus nicht verfuegbar (HRESULT {ex.HResult:X8}); ";
                                System.Diagnostics.Trace.TraceWarning("Audio-Endpunkt {0}: {1}", id, sessionInfo);
                            }
                        }
                        if (inUse == true) sessionInfo = "WIRD VERWENDET / aktive Audiositzung (auch bei Stille); ";
                        var preferred = connected && roles is { Count: > 0 };
                        var status = state switch
                        {
                            1 => "Aktiv / bereit", 2 => "Deaktiviert", 4 => "Nicht vorhanden",
                            8 => "Nicht angeschlossen", _ => "Windows-Audiostatus: " + state
                        };
                        result.Add(new("SWD\\MMDEVAPI\\" + id, name, "AudioEndpoint", status, "",
                            sessionInfo + (connected && roles is { Count: > 0 } ? "Standard: " + string.Join("; ", roles) :
                                connected ? "Endpunkt aktiv / bereit (nicht zwingend gerade wiedergegeben)" : status),
                            preferred, connected, inUse));
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                return result;
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private static bool HasActiveSession(IDevice device)
    {
        var iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        device.Activate(ref iid, 23, IntPtr.Zero, out var manager);
        try
        {
            manager.GetSessionEnumerator(out var sessions);
            try
            {
                sessions.GetCount(out var count);
                for (var index = 0; index < count; index++)
                {
                    sessions.GetSession(index, out var session);
                    try
                    {
                        session.GetState(out var state);
                        if (state == 1) return true;
                    }
                    finally { Marshal.ReleaseComObject(session); }
                }
                return false;
            }
            finally { Marshal.ReleaseComObject(sessions); }
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint mask, out IDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr parameters, out IAudioSessionManager instance);
        void OpenPropertyStore(uint access, out IPropertyStore properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager
    {
        void GetAudioSessionControl(IntPtr sessionId, uint flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr sessionId, uint flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessions sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessions
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSession session);
    }
    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSession
    {
        void GetState(out int state);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
