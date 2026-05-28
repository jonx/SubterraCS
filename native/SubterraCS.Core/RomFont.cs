namespace SubterraCS.Core;

/// <summary>
/// The Spectrum 48K ROM character set, copied verbatim from
/// <c>$3D00..$3FFF</c> (96 chars × 8 bytes).  This is the font the
/// original game prints the HUD through via <c>RST 10</c>: the
/// painter at <c>$E347</c> sets the CHARS sysvar at <c>$5C36</c> to
/// <c>$3C00</c> (the ROM-font base, where char $20 = SPACE lands at
/// <c>$3D00</c>).
///
/// Shipping these 768 bytes verbatim is the only way to get byte-
/// identical HUD text in the native port without bundling a Spectrum
/// emulator at runtime.
/// </summary>
public sealed class RomFont
{
    /// <summary>Byte 0 of the font (char $20 = SPACE in ROM).</summary>
    public const int FirstChar = 0x20;
    public const int LastChar = 0x7F;
    public const int BytesPerGlyph = 8;
    public const int TotalBytes = (LastChar - FirstChar + 1) * BytesPerGlyph;  // 768

    public byte[] Data { get; }
    public RomFont(byte[] data)
    {
        if (data is null || data.Length < TotalBytes)
        {
            throw new ArgumentException(
                $"ROM font must be at least {TotalBytes} bytes; got {data?.Length ?? 0}.");
        }
        Data = data;
    }

    public static RomFont Load(string path) => new(File.ReadAllBytes(path));

    /// <summary>Get the 8 scanlines for the glyph of <paramref name="ch"/>.</summary>
    public ReadOnlySpan<byte> Glyph(char ch)
    {
        int code = ch;
        if (code < FirstChar || code > LastChar) code = ' ';
        int offset = (code - FirstChar) * BytesPerGlyph;
        return Data.AsSpan(offset, BytesPerGlyph);
    }

    /// <summary>Print a string into the framebuffer starting at (x, y)
    /// with the given attribute byte.  Equivalent to the original's
    /// <c>RST 10</c> for printable characters (no control codes).</summary>
    public void Draw(Framebuffer fb, int x, int y, string s, byte attr)
    {
        foreach (var ch in s)
        {
            if (x >= Framebuffer.Width) return;
            Blitters.DrawTile8x8(fb, x, y, Glyph(ch), attr);
            x += 8;
        }
    }

    /// <summary>Print a string horizontally centered on row <paramref name="y"/>.</summary>
    public void DrawCentered(Framebuffer fb, int y, string s, byte attr)
    {
        int width = s.Length * 8;
        int x = Math.Max(0, (Framebuffer.Width - width) / 2);
        x &= ~7;
        Draw(fb, x, y, s, attr);
    }
}
