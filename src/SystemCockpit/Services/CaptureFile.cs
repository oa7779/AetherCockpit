using System.IO;

namespace SystemCockpit.Services;

internal sealed class CaptureFile : IDisposable
{
    private readonly BinaryWriter _writer;
    internal CaptureFile(Stream stream)
    {
        _writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        NpcapCapture.WriteHeader(_writer);
    }

    internal bool TryWrite(uint seconds, uint microseconds, uint wireLength, ReadOnlySpan<byte> packet, TimeSpan elapsed)
    {
        if (packet.Length > NpcapCapture.SnapLength || packet.Length > wireLength || microseconds >= 1000000)
            throw new InvalidDataException("Ungueltige Capture-Paketdaten.");
        if (elapsed >= NpcapCapture.MaxDuration ||
            _writer.BaseStream.Position + 16 + packet.Length > NpcapCapture.MaxFileBytes) return false;
        _writer.Write(seconds); _writer.Write(microseconds);
        _writer.Write((uint)packet.Length); _writer.Write(wireLength);
        _writer.Write(packet);
        return true;
    }

    public void Dispose() => _writer.Dispose();
}
