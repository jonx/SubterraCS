namespace Subterra.Spectrum;

/// <summary>
/// Minimal mono 16-bit PCM RIFF/WAVE writer.  Hand-rolled (no
/// NuGet) — same policy as the rest of the project: every byte
/// in the file is something we wrote.  Spec: Microsoft's RIFF
/// chunk format + the WAVE-PCM subtype.
/// </summary>
public static class WavWriter
{
    public static void WriteMono16(string path, ReadOnlySpan<short> samples, int sampleRate)
    {
        using var fs = File.Create(path);
        WriteMono16(fs, samples, sampleRate);
    }

    public static void WriteMono16(Stream stream, ReadOnlySpan<short> samples, int sampleRate)
    {
        const int numChannels = 1;
        const int bitsPerSample = 16;
        int byteRate = sampleRate * numChannels * (bitsPerSample / 8);
        int blockAlign = numChannels * (bitsPerSample / 8);
        int dataSize = samples.Length * sizeof(short);
        int riffSize = 36 + dataSize;

        Span<byte> hdr = stackalloc byte[44];
        // RIFF chunk
        hdr[0] = (byte)'R'; hdr[1] = (byte)'I'; hdr[2] = (byte)'F'; hdr[3] = (byte)'F';
        WriteI32LE(hdr.Slice(4, 4), riffSize);
        hdr[8] = (byte)'W'; hdr[9] = (byte)'A'; hdr[10] = (byte)'V'; hdr[11] = (byte)'E';
        // fmt chunk
        hdr[12] = (byte)'f'; hdr[13] = (byte)'m'; hdr[14] = (byte)'t'; hdr[15] = (byte)' ';
        WriteI32LE(hdr.Slice(16, 4), 16);              // fmt subchunk size
        WriteI16LE(hdr.Slice(20, 2), 1);                // audio format = PCM
        WriteI16LE(hdr.Slice(22, 2), (short)numChannels);
        WriteI32LE(hdr.Slice(24, 4), sampleRate);
        WriteI32LE(hdr.Slice(28, 4), byteRate);
        WriteI16LE(hdr.Slice(32, 2), (short)blockAlign);
        WriteI16LE(hdr.Slice(34, 2), (short)bitsPerSample);
        // data chunk
        hdr[36] = (byte)'d'; hdr[37] = (byte)'a'; hdr[38] = (byte)'t'; hdr[39] = (byte)'a';
        WriteI32LE(hdr.Slice(40, 4), dataSize);

        stream.Write(hdr);

        // Sample bytes — little-endian signed 16-bit.
        Span<byte> buf = stackalloc byte[2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = samples[i];
            buf[0] = (byte)(s & 0xFF);
            buf[1] = (byte)((s >> 8) & 0xFF);
            stream.Write(buf);
        }
    }

    private static void WriteI32LE(Span<byte> dst, int v)
    {
        dst[0] = (byte)(v & 0xFF);
        dst[1] = (byte)((v >> 8) & 0xFF);
        dst[2] = (byte)((v >> 16) & 0xFF);
        dst[3] = (byte)((v >> 24) & 0xFF);
    }

    private static void WriteI16LE(Span<byte> dst, short v)
    {
        dst[0] = (byte)(v & 0xFF);
        dst[1] = (byte)((v >> 8) & 0xFF);
    }
}
