using System.IO.Compression;
using System.Buffers.Binary;

namespace Subterra.Spectrum;

/// <summary>
/// A small RGBA-only PNG encoder. Hand-rolled so this project has zero
/// graphics dependencies (apart from the .NET BCL).
///
/// We do filter 0 (None) on every scanline and let zlib's deflate handle
/// compression. The output is a valid PNG that every viewer accepts;
/// it's not the smallest possible encoding but it's small enough.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature =
    {
        0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
    };

    public static void WriteRgba(
        string path, ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"rgba length {rgba.Length} doesn't match {width}×{height}×4.");
        }
        using var fs = File.Create(path);
        WriteRgba(fs, rgba, width, height);
    }

    public static void WriteRgba(
        Stream destination, ReadOnlySpan<byte> rgba, int width, int height)
    {
        destination.Write(Signature, 0, Signature.Length);

        // IHDR chunk: width, height, bit depth, colour type, compression,
        // filter, interlace.
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type: truecolour with alpha (RGBA)
        ihdr[10] = 0;  // compression: deflate
        ihdr[11] = 0;  // filter: adaptive (we still use filter 0 per row)
        ihdr[12] = 0;  // interlace: none
        WriteChunk(destination, "IHDR", ihdr);

        // IDAT: scanlines, each prefixed with a filter byte (0 = None),
        // run through zlib.
        var raw = new byte[(width * 4 + 1) * height];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0; // filter byte
            rgba.Slice(y * width * 4, width * 4).CopyTo(raw.AsSpan(o, width * 4));
            o += width * 4;
        }
        var compressed = ZlibCompress(raw);
        WriteChunk(destination, "IDAT", compressed);

        WriteChunk(destination, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(
        Stream destination, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBuf, (uint)data.Length);
        destination.Write(lenBuf);

        Span<byte> typeBuf = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            typeBuf[i] = (byte)type[i];
        }
        destination.Write(typeBuf);

        destination.Write(data);

        uint crc = 0xFFFFFFFFu;
        crc = Crc32.Update(crc, typeBuf);
        crc = Crc32.Update(crc, data);
        crc ^= 0xFFFFFFFFu;
        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
        destination.Write(crcBuf);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        // zlib stream: 2-byte header + deflate stream + 4-byte adler-32.
        using var ms = new MemoryStream();
        // zlib header: CMF (0x78 = deflate, 32k window), FLG: chosen so
        // (CMF<<8 | FLG) % 31 == 0. 0x78 0x9C is the classic
        // "default compression" pair.
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);

        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
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
        foreach (var by in data)
        {
            a = (a + by) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }
}
