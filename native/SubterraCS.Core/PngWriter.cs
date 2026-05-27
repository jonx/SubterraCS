using System.Buffers.Binary;
using System.IO.Compression;

namespace SubterraCS.Core;

/// <summary>
/// Dependency-free RGBA PNG writer — same hand-rolled encoder we use
/// in the original Subterra solution.  Filter-None scanlines, deflate
/// for compression, our own CRC-32.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static void WriteRgba(string path, ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException($"rgba length {rgba.Length} != {width}×{height}×4");
        }
        using var fs = File.Create(path);
        fs.Write(Signature, 0, Signature.Length);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(fs, "IHDR", ihdr);

        var raw = new byte[(width * 4 + 1) * height];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0;
            rgba.Slice(y * width * 4, width * 4).CopyTo(raw.AsSpan(o, width * 4));
            o += width * 4;
        }
        WriteChunk(fs, "IDAT", ZlibCompress(raw));
        WriteChunk(fs, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBuf, (uint)data.Length);
        s.Write(lenBuf);
        Span<byte> typeBuf = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBuf[i] = (byte)type[i];
        s.Write(typeBuf);
        s.Write(data);
        uint crc = 0xFFFFFFFFu;
        crc = Crc32.Update(crc, typeBuf);
        crc = Crc32.Update(crc, data);
        crc ^= 0xFFFFFFFFu;
        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
        s.Write(crcBuf);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var def = new DeflateStream(ms, CompressionLevel.Optimal, true))
        {
            def.Write(data, 0, data.Length);
        }
        uint adler = Adler32(data);
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, adler);
        ms.Write(tail);
        return ms.ToArray();
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var by in data) { a = (a + by) % mod; b = (b + a) % mod; }
        return (b << 16) | a;
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();
    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
            crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
