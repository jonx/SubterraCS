namespace Subterra.Spectrum;

/// <summary>
/// Tiny IEEE 802.3 (PNG/zlib polynomial 0xEDB88320) CRC-32, table-driven.
/// We have our own because we deliberately avoid the
/// System.IO.Hashing NuGet package — the whole point of this project is
/// to write the tooling ourselves.
/// </summary>
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
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            t[n] = c;
        }
        return t;
    }

    /// <summary>
    /// Update a running CRC. Start with <c>0xFFFFFFFFu</c>, feed it data,
    /// then XOR the final value with <c>0xFFFFFFFFu</c>.
    /// </summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }
}
