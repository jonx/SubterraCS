namespace Subterra.Spectrum;

/// <summary>
/// Helpers for the ZX Spectrum display layout.
///
/// The display is 256 × 192 pixels, organised as:
///
/// * **6 144 bytes** of bitmap at 0x4000..0x57FF. Each byte is 8 horizontal
///   pixels, MSB on the left. The vertical layout is not linear — addresses
///   are interleaved into three 64-row "thirds", and within a third the
///   pixel rows are interleaved in 8-row chunks. See
///   <see cref="BitmapAddress(int, int)"/>.
/// * **768 bytes** of attributes at 0x5800..0x5AFF. One byte per 8 × 8
///   character cell (32 columns × 24 rows). The byte encodes:
///     bits 0..2 = ink colour (0..7)
///     bits 3..5 = paper colour (0..7)
///     bit  6    = bright
///     bit  7    = flash
/// </summary>
public static class SpectrumScreen
{
    public const int Width = 256;
    public const int Height = 192;
    public const int BitmapBytes = 6144;
    public const int AttributeBytes = 768;
    public const int ScrBytes = BitmapBytes + AttributeBytes;

    /// <summary>
    /// Map a pixel (x, y) to the byte offset inside the 6 144-byte bitmap
    /// region.  The pixel column inside the byte is <c>7 - (x &amp; 7)</c>.
    /// </summary>
    public static int BitmapAddress(int x, int y)
    {
        // Spectrum bitmap offset layout (13 bits, into the 6 144-byte region):
        //   bit 12,11 = band       (y bits 7,6 — picks one of three 64-row bands)
        //   bit 10,9,8 = pixel row (y bits 2,1,0 — line within an 8-line char)
        //   bit 7,6,5  = char row  (y bits 5,4,3 — char row within a band)
        //   bit 4..0   = x byte    (x >> 3, 0..31)
        // i.e. the "famous" Spectrum interleave: the low 3 bits of y end up
        // in the *high* part of the bitmap address, ahead of the char-row bits.
        return ((y & 0xC0) << 5)
             | ((y & 0x07) << 8)
             | ((y & 0x38) << 2)
             | (x >> 3);
    }

    /// <summary>Attribute byte offset for the 8×8 cell containing pixel (x, y).</summary>
    public static int AttributeAddress(int x, int y)
    {
        int col = x >> 3;      // 0..31
        int row = y >> 3;      // 0..23
        return BitmapBytes + (row * 32) + col;
    }

    /// <summary>
    /// Standard ZX Spectrum 16-colour palette (8 base colours × 2 bright
    /// levels), in RGBA order. Index = (bright ? 8 : 0) + ink. Black is
    /// always (0,0,0) regardless of bright.
    /// </summary>
    public static readonly (byte R, byte G, byte B)[] Palette =
    {
        (0x00, 0x00, 0x00), // 0 black
        (0x00, 0x00, 0xCD), // 1 blue
        (0xCD, 0x00, 0x00), // 2 red
        (0xCD, 0x00, 0xCD), // 3 magenta
        (0x00, 0xCD, 0x00), // 4 green
        (0x00, 0xCD, 0xCD), // 5 cyan
        (0xCD, 0xCD, 0x00), // 6 yellow
        (0xCD, 0xCD, 0xCD), // 7 white
        (0x00, 0x00, 0x00), // 8 black (bright)
        (0x00, 0x00, 0xFF), // 9 blue
        (0xFF, 0x00, 0x00), // 10 red
        (0xFF, 0x00, 0xFF), // 11 magenta
        (0x00, 0xFF, 0x00), // 12 green
        (0x00, 0xFF, 0xFF), // 13 cyan
        (0xFF, 0xFF, 0x00), // 14 yellow
        (0xFF, 0xFF, 0xFF), // 15 white
    };

    /// <summary>
    /// Decode a 6 912-byte screen (.scr layout, or 0x4000..0x5AFF copied
    /// out of a snapshot) into an RGBA byte array of size
    /// <c>Width * Height * 4</c>.  Flash is rendered as the steady "ink"
    /// state (i.e. ink and paper are NOT swapped).
    /// </summary>
    public static byte[] DecodeRgba(ReadOnlySpan<byte> scr)
    {
        if (scr.Length < ScrBytes)
        {
            throw new ArgumentException(
                $"Spectrum screen must be at least {ScrBytes} bytes, got {scr.Length}.",
                nameof(scr));
        }

        var output = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte bitmap = scr[BitmapAddress(x, y)];
                int bit = 7 - (x & 7);
                bool on = (bitmap & (1 << bit)) != 0;

                byte attr = scr[AttributeAddress(x, y)];
                int ink = attr & 0x07;
                int paper = (attr >> 3) & 0x07;
                bool bright = (attr & 0x40) != 0;
                int paletteIndex = (bright ? 8 : 0) + (on ? ink : paper);
                var (r, g, b) = Palette[paletteIndex];

                int o = ((y * Width) + x) * 4;
                output[o + 0] = r;
                output[o + 1] = g;
                output[o + 2] = b;
                output[o + 3] = 0xFF;
            }
        }
        return output;
    }
}
