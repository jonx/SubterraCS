namespace SubterraCS.Core;

/// <summary>
/// HUD reproduced from the original game's <c>$E785</c> string table
/// (see docs/MEMORY-MAP.md and RE-LOG §24).  The cassette stores the
/// HUD layout as a literal Spectrum-print stream the painter at
/// <c>$E347</c> walks via <c>RST 10</c>:
/// <pre>
///   row 16 (y=128):  INK 6  "DEPTH :"
///   row 17 (y=136):  "SCORE :"                      "RESCUED:"@col 22
///   row 18 (y=144):  "SHIELD:"  ▓▓▓▓▓ ▓▓▓▓▓ ▓▓▓▓▓ ▓▓▓▓▓ ▓▓▓▓
///                                red   mag   yel   cyan green
///                                = 5+5+5+5+4 = 24 cells
///   row 19 (y=152):  "FUEL  :"  same 24-cell stripe
/// </pre>
/// The bar drains right-to-left: the trailing cells get overwritten
/// with attribute $00 (INK 0 / PAPER 0) so the ink colour disappears.
/// At full strength = rainbow; at 0 = invisible.
/// </summary>
public static class Hud
{
    public const int HudTop = 128;          // y of "DEPTH :" row
    public const int HudCharRow = 16;       // first HUD char row (y/8)

    // The exact 24-cell stripe pattern from $E785 — five colour bands,
    // 5+5+5+5+4 cells.  Each entry is a Spectrum INK colour (paper 0,
    // bright not set in the original; we render bright for visibility).
    private static readonly byte[] StripePattern = BuildStripe();
    private static byte[] BuildStripe()
    {
        var s = new byte[24];
        // INK 2 red ×5, INK 3 magenta ×5, INK 6 yellow ×5, INK 5 cyan ×5, INK 4 green ×4.
        // Attribute byte = ink | (paper<<3) | bright; we set bright for punch.
        Span<byte> inks = stackalloc byte[] { 2, 3, 6, 5, 4 };
        Span<byte> runs = stackalloc byte[] { 5, 5, 5, 5, 4 };
        int idx = 0;
        for (int band = 0; band < 5; band++)
        {
            byte attr = (byte)(0x40 | inks[band]);    // bright | ink
            for (int j = 0; j < runs[band]; j++) s[idx++] = attr;
        }
        return s;
    }

    public static void Draw(Framebuffer fb, World world)
    {
        // Clear the HUD region (bitmap + attrs) so we have a clean slate.
        for (int y = HudTop; y < 192; y++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Bitmap[Framebuffer.BitmapAddress(col * 8, y)] = 0;
            }
        }
        // Rows 16-19: HUD chrome — bright yellow ink on black.
        for (int row = HudCharRow; row < 20; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Attributes[row * 32 + col] = 0x46;
            }
        }
        // Rows 20-23: bottom-decor strip — green ink on black,
        // matching the emulator-peeked attributes at $5A80..$5AFF.
        for (int row = 20; row < 24; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Attributes[row * 32 + col] = 0x04;
            }
        }

        // Row 16: "DEPTH :   N"
        MiniFont.Draw(fb, 0, 128, $"DEPTH :{world.Depth + 1,3}", 0x46);

        // Row 17: "SCORE : NNNNNN" + "RESCUED:NN" at col 22.
        MiniFont.Draw(fb, 0,   136, $"SCORE :{world.Score:D6}", 0x46);
        MiniFont.Draw(fb, 22 * 8, 136, $"RESCUED:{world.Rescued:D2}", 0x46);

        // Row 18: "SHIELD:" + 24-cell stripe bar.
        MiniFont.Draw(fb, 0, 144, "SHIELD:", 0x46);
        DrawStripeBar(fb, 8 * 8, 144, world.Shield);

        // Row 19: "FUEL  :" + same stripe bar (note double-space).
        MiniFont.Draw(fb, 0, 152, "FUEL  :", 0x46);
        DrawStripeBar(fb, 8 * 8, 152, world.Fuel);

        // Lives — three small magenta squares on the bottom-right of
        // the title row.  Not in the original $E785 table — extra
        // affordance we'll remove once the rest matches the cassette.
        Span<byte> chip = stackalloc byte[8];
        for (int r = 2; r < 6; r++) chip[r] = 0x3C;
        for (int i = 0; i < Math.Min(world.Lives, 3); i++)
        {
            Blitters.DrawTile8x8(fb, 232 + i * 8, 128, chip, 0x43);
        }
    }

    /// <summary>
    /// Paints the 24-cell stripe bar for SHIELD or FUEL.  Each cell is
    /// 8×8 pixels: filled at full ink colour while within the value,
    /// blacked out (INK 0 on PAPER 0) beyond it — same drain idiom the
    /// original uses by overwriting trailing attributes.
    /// </summary>
    private static void DrawStripeBar(Framebuffer fb, int x, int y, int value)
    {
        int filled = Math.Clamp(value * StripePattern.Length / 100, 0, StripePattern.Length);
        Span<byte> solid = stackalloc byte[8];
        Span<byte> empty = stackalloc byte[8];
        for (int r = 0; r < 8; r++) solid[r] = 0xFF;  // every pixel set
        for (int i = 0; i < StripePattern.Length; i++)
        {
            int cx = x + i * 8;
            if (cx + 8 > 256) break;
            if (i < filled)
            {
                Blitters.DrawTile8x8(fb, cx, y, solid, StripePattern[i]);
            }
            else
            {
                Blitters.DrawTile8x8(fb, cx, y, empty, 0x00);
            }
        }
    }
}
