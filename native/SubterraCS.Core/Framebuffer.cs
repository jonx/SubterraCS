namespace SubterraCS.Core;

/// <summary>
/// 256 × 192 1-bit-per-pixel bitmap + 32 × 24 attribute grid, in the
/// same memory layout the Spectrum ULA uses.  Lets the four blitters
/// run their original Z80 address arithmetic verbatim.
///
/// The bitmap byte at a given (x, y) lives at
/// <see cref="BitmapAddress"/>(x, y) inside <see cref="Bitmap"/>;
/// the attribute byte for the 8×8 cell containing that pixel lives at
/// <see cref="AttributeAddress"/>(x, y) inside <see cref="Attributes"/>.
/// </summary>
public sealed class Framebuffer
{
    public const int Width = 256;
    public const int Height = 192;
    public const int BitmapBytes = 6144;
    public const int AttributeBytes = 768;

    public byte[] Bitmap { get; } = new byte[BitmapBytes];
    public byte[] Attributes { get; } = new byte[AttributeBytes];

    /// <summary>
    /// Compute the byte offset inside <see cref="Bitmap"/> containing
    /// the 8 pixels with column <c>x/8</c> on row <paramref name="y"/>.
    /// Within the returned byte, the MSB is the leftmost pixel.
    /// </summary>
    public static int BitmapAddress(int x, int y)
    {
        return ((y & 0xC0) << 5)   // band       → bits 12,11
             | ((y & 0x07) << 8)   // pixel row  → bits 10..8
             | ((y & 0x38) << 2)   // char row   → bits 7..5
             | (x >> 3);           // x byte     → bits 4..0
    }

    /// <summary>Attribute byte for the cell containing pixel (x, y).</summary>
    public static int AttributeAddress(int x, int y)
        => (y >> 3) * 32 + (x >> 3);

    public void Clear()
    {
        Array.Clear(Bitmap, 0, Bitmap.Length);
        Array.Clear(Attributes, 0, Attributes.Length);
    }

    public void FillAttributes(byte attr)
    {
        for (int i = 0; i < Attributes.Length; i++)
        {
            Attributes[i] = attr;
        }
    }

    /// <summary>
    /// Decode the current framebuffer into an RGBA8 byte array of size
    /// <c>Width × Height × 4</c>.  Treats flash as the steady "ink"
    /// state (no swap).
    /// </summary>
    public byte[] ToRgba()
    {
        var output = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte b = Bitmap[BitmapAddress(x, y)];
                int bit = 7 - (x & 7);
                bool on = (b & (1 << bit)) != 0;
                byte attr = Attributes[AttributeAddress(x, y)];
                var (r, g, bl) = on ? SpectrumPalette.Ink(attr) : SpectrumPalette.Paper(attr);
                int o = ((y * Width) + x) * 4;
                output[o + 0] = r;
                output[o + 1] = g;
                output[o + 2] = bl;
                output[o + 3] = 0xFF;
            }
        }
        return output;
    }
}
