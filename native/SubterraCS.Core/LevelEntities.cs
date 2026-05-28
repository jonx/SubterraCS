namespace SubterraCS.Core;

/// <summary>
/// Per-level static entity placement, ported from the original's
/// <c>$F2E2</c> + <c>$F594</c> tables (see RE-LOG §24/§30).
///
/// The cassette stores six per-level entity lists at variable
/// addresses pointed to by the table at <c>$F594</c>:
/// <code>
///   level 0:  6 entries × 8 bytes at $F2E8 (NOTE: anomalous —
///             level 1's pointer is only 3 bytes later, suggesting
///             level 0 either shares records or uses a different
///             format we haven't fully decoded)
///   level 1: 10 entries × 8 bytes at $F2EB
///   level 2:  9 entries × 8 bytes at $F33B
///   level 3: 13 entries × 8 bytes at $F383
///   level 4: 18 entries × 8 bytes at $F3EB
///   level 5: 25 entries × 8 bytes at $F47B
/// </code>
/// Each 8-byte record matches the in-memory IX entity layout
/// documented in MEMORY-MAP §$F1EF:
/// <code>
///   +0  Type id (index into the $F5A0 type table)
///   +1  y coordinate
///   +2  Animation frame index
///   +3,+4  Top-half screen address (Spectrum bitmap, lo/hi)
///   +5,+6  Bottom-half screen address (lo/hi)
///   +7  Flag / facing byte (TBD)
/// </code>
///
/// We ship the 6 + 6×N records as
/// <c>assets/extracted/level-entities-f2e8.bin</c>: 6 count bytes
/// followed by the concatenated records for levels 0..5.
/// </summary>
public sealed class LevelEntities
{
    public readonly record struct Record(
        byte TypeId, byte Y, byte Frame,
        ushort TopAddr, ushort BotAddr, byte Flags);

    public IReadOnlyList<Record>[] Levels { get; }

    public LevelEntities(IReadOnlyList<Record>[] levels) => Levels = levels;

    public static LevelEntities Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        if (raw.Length < 6) throw new InvalidDataException("level-entities file too small");

        var counts = raw[0..6];
        var levels = new IReadOnlyList<Record>[6];
        int cursor = 6;
        for (int level = 0; level < 6; level++)
        {
            int n = counts[level];
            var list = new Record[n];
            for (int i = 0; i < n; i++)
            {
                int o = cursor + i * 8;
                list[i] = new Record(
                    TypeId:  raw[o + 0],
                    Y:       raw[o + 1],
                    Frame:   raw[o + 2],
                    TopAddr: (ushort)(raw[o + 3] | (raw[o + 4] << 8)),
                    BotAddr: (ushort)(raw[o + 5] | (raw[o + 6] << 8)),
                    Flags:   raw[o + 7]);
            }
            levels[level] = list;
            cursor += n * 8;
        }
        return new LevelEntities(levels);
    }

    /// <summary>
    /// Decode a Spectrum bitmap address back to its (x, y) pixel
    /// coordinates.  Spectrum interleaved layout:
    /// bits 12,11 = y bits 7,6 (band); bits 10..8 = y bits 2..0 (pix-row);
    /// bits 7..5 = y bits 5..3 (char-row); bits 4..0 = x byte.
    /// </summary>
    public static (int X, int Y) DecodeBitmapAddress(ushort addr)
    {
        int bitmapOffset = addr - 0x4000;
        if (bitmapOffset < 0 || bitmapOffset >= 0x1800) return (0, 0);
        int yBand    = (bitmapOffset >> 5) & 0xC0;
        int yPixRow  = (bitmapOffset >> 8) & 0x07;
        int yCharRow = (bitmapOffset >> 2) & 0x38;
        int xByte    = bitmapOffset & 0x1F;
        return (xByte << 3, yBand | yCharRow | yPixRow);
    }

    /// <summary>
    /// Decode an entity record's screen position.  Port of $F278's
    /// <c>ADD HL,BC</c> where <c>HL = TopAddr</c> and <c>BC = record.Y
    /// - ($E583)</c>.  All per-level TopAddrs are scanline-starts
    /// (x_byte=0), and the record's "Y" byte is actually a BYTE OFFSET
    /// added to TopAddr — which due to Spectrum interleaved addressing
    /// can shift both X and char-row.  $E583 is a vertical-shift cursor
    /// (zero during normal gameplay; non-zero during certain animations).
    /// </summary>
    public static (int X, int Y) DecodeEntityPosition(ushort topAddr, byte recordY, byte e583)
    {
        ushort effective = (ushort)(topAddr + (byte)(recordY - e583));
        return DecodeBitmapAddress(effective);
    }
}
