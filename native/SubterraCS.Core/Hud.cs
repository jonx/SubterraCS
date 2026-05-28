namespace SubterraCS.Core;

/// <summary>
/// HUD reproduction — every byte verified against the running emulator.
///
/// LAYOUT (from the cassette's <c>$E785</c> print-stream, decoded in
/// RE-LOG §24):
/// <pre>
///   char row 16 (y=128):  INK 6  "DEPTH :"   N
///   char row 17 (y=136):  "SCORE :"  NNNNNN          @col22: "RESCUED:" NN
///   char row 18 (y=144):  "SHIELD:" [24 stripe cells]
///   char row 19 (y=152):  "FUEL  :" [24 stripe cells]
/// </pre>
///
/// BAR ANATOMY (verified via mid-gameplay RAM peek):
/// Each of the 24 cells is an 8×8 sprite assembled from a fixed
/// UDG-A frame ($E62B = <c>88 80 00 00 00 00 80 88</c> — left/right
/// "corner brackets") plus 4 middle scanlines that hold the fill
/// state.  When the bar is FULL the middle 4 bytes are <c>$FF</c>;
/// when DRAINED they're <c>$00</c>; at the boundary they're one of
/// <c>{$00, $C0, $F0, $FC}</c> from the partial-mask table at
/// <c>$E0EC</c>.  Combined: a full cell reads <c>88 80 FF FF FF FF
/// 80 88</c> on the bitmap, an empty cell reads <c>88 80 00 00 00
/// 00 80 88</c>.
///
/// VALUE RANGE: 0..<c>$60</c> (96), with each cell representing
/// 4 quanta of value (24 × 4 = 96).  This is the actual range the
/// game uses — <see cref="World.Shield"/> and <see cref="World.Fuel"/>
/// in our port should be in this 0..96 range to render faithfully.
///
/// COLOUR STRIPE: cells coloured in 5+5+5+5+4 runs of bright red,
/// magenta, yellow, cyan, green — the literal byte runs in the
/// <c>$E785</c> string after each <c>INK</c> control byte.  The
/// attribute byte for a cell is <c>0x40 | ink</c> (bright + ink).
///
/// The native port draws this directly into <see cref="Framebuffer"/>
/// rather than going through a print stream — the result is bitmap-
/// identical to the original game's HUD bars.
/// </summary>
public static class Hud
{
    public const int HudCharRow = 16;
    public const int HudTop = HudCharRow * 8;       // y=128

    /// <summary>Maximum shield/fuel value the bar reaches.  Matches <c>$60</c>.</summary>
    public const int BarMaxValue = 96;

    /// <summary>Number of bar cells.  Hard-coded in the <c>$E785</c> stripe pattern.</summary>
    public const int BarCells = 24;

    /// <summary>Quanta-per-cell.  Each cell shows 4 units of value (8 pixels / 2 per quantum).</summary>
    public const int QuantaPerCell = 4;

    /// <summary>The UDG-A pattern from <c>$E62B</c>: corner brackets.</summary>
    private static readonly byte[] UdgA = { 0x88, 0x80, 0x00, 0x00, 0x00, 0x00, 0x80, 0x88 };

    /// <summary>The partial-fill mask table from <c>$E0EC</c>:
    /// index = (value &amp; 3), value = bitmap byte for the middle
    /// 4 scanlines of the boundary cell.</summary>
    private static readonly byte[] PartialMask = { 0x00, 0xC0, 0xF0, 0xFC };

    /// <summary>The 24-cell colour stripe — 5 red, 5 magenta, 5 yellow,
    /// 5 cyan, 4 green.  Verified against the <c>$E785</c> print stream.
    /// Each entry is a Spectrum attribute byte (bright | ink).</summary>
    private static readonly byte[] StripeAttr = BuildStripeAttr();
    private static byte[] BuildStripeAttr()
    {
        var s = new byte[BarCells];
        // INK colours from the cassette: 2 red, 3 magenta, 6 yellow, 5 cyan, 4 green.
        Span<byte> inks = stackalloc byte[] { 2, 3, 6, 5, 4 };
        Span<byte> runs = stackalloc byte[] { 5, 5, 5, 5, 4 };
        int idx = 0;
        for (int band = 0; band < 5; band++)
        {
            byte attr = (byte)(0x40 | inks[band]);    // bright | ink, paper 0
            for (int j = 0; j < runs[band]; j++) s[idx++] = attr;
        }
        return s;
    }

    public static void Draw(Framebuffer fb, World world)
    {
        // Clear the HUD bitmap region (rows 16..23 = y=128..191).
        for (int y = HudTop; y < 192; y++)
            for (int col = 0; col < 32; col++)
                fb.Bitmap[Framebuffer.BitmapAddress(col * 8, y)] = 0;

        // Default attribute for the HUD chrome rows = bright yellow on black,
        // matching the running emulator's $5A00..$5A7F (rows 16..19) values.
        for (int row = HudCharRow; row < 20; row++)
            for (int col = 0; col < 32; col++)
                fb.Attributes[row * 32 + col] = 0x46;

        // Use the Spectrum ROM font ($3D00..$3FFF) for the labels — same
        // bytes the original $E347 painter writes via RST 10 → ROM font.
        var font = world.RomFont;

        // Row 16: "DEPTH :   N"
        // Depth is the original's $E587 (1-based at first playable level).
        DrawText(fb, font, 0, 128, $"DEPTH :{world.Depth,3}", 0x46);

        // Row 17: "SCORE : NNNNNN"     "RESCUED:NN"@col22
        DrawText(fb, font, 0,      136, $"SCORE :{world.Score:D6}", 0x46);
        DrawText(fb, font, 22 * 8, 136, $"RESCUED:{world.Rescued:D2}", 0x46);

        // Row 18: "SHIELD:" + bar
        DrawText(fb, font, 0, 144, "SHIELD:", 0x46);
        int shieldBarValue = world.BarFillOverride >= 0
            ? world.BarFillOverride
            : ScaleToBar(world.Shield);
        DrawBar(fb, 7 * 8, 144, shieldBarValue);

        // Row 19: "FUEL  :" + bar
        DrawText(fb, font, 0, 152, "FUEL  :", 0x46);
        int fuelBarValue = world.BarFillOverride >= 0
            ? world.BarFillOverride
            : ScaleToBar(world.Fuel);
        DrawBar(fb, 7 * 8, 152, fuelBarValue);

        // Bottom decor strip (rows 20..23) — green-on-black attribute strip
        // matching the emulator's $5A80..$5AFF.  Pixel content is procedural
        // (built up by decor entities — see RE-LOG §26).
        for (int row = 20; row < 24; row++)
            for (int col = 0; col < 32; col++)
                fb.Attributes[row * 32 + col] = 0x04;
    }

    /// <summary>Draw text using the ROM font when available, otherwise
    /// fall back to <see cref="MiniFont"/>.  This keeps the HUD working
    /// in headless tests that haven't loaded the ROM-font asset.</summary>
    private static void DrawText(Framebuffer fb, RomFont? font, int x, int y, string s, byte attr)
    {
        if (font is not null) font.Draw(fb, x, y, s, attr);
        else MiniFont.Draw(fb, x, y, s, attr);
    }

    /// <summary>
    /// Convert a 0..100 percentage (used through the rest of the World)
    /// into the 0..96 game-internal range.
    /// </summary>
    private static int ScaleToBar(int value0to100)
        => Math.Clamp(value0to100 * BarMaxValue / 100, 0, BarMaxValue);

    /// <summary>
    /// Draw a 24-cell bar starting at (<paramref name="x"/>, <paramref name="y"/>)
    /// showing <paramref name="value"/> 0..<see cref="BarMaxValue"/>.  Faithful
    /// reproduction of <c>$E0BE</c>'s output: each cell is UDG-A
    /// (corners) plus 4 middle scanlines whose bytes are <c>$FF</c>
    /// (full), <c>$00</c> (empty), or one of the partial-mask values
    /// for the boundary cell.
    /// </summary>
    private static void DrawBar(Framebuffer fb, int x, int y, int value)
    {
        int fullCells = value / QuantaPerCell;
        int partialIdx = value % QuantaPerCell;

        Span<byte> cell = stackalloc byte[8];
        for (int i = 0; i < BarCells; i++)
        {
            int cx = x + i * 8;
            if (cx + 8 > 256) break;

            // Top + bottom corners always come from UDG-A.
            cell[0] = UdgA[0]; cell[1] = UdgA[1];
            cell[6] = UdgA[6]; cell[7] = UdgA[7];

            // Middle 4 scanlines: full / partial / empty depending on
            // cell position vs the value's boundary.
            byte middle = i < fullCells           ? (byte)0xFF
                        : i == fullCells          ? PartialMask[partialIdx]
                                                  : (byte)0x00;
            cell[2] = middle; cell[3] = middle;
            cell[4] = middle; cell[5] = middle;

            Blitters.DrawTile8x8(fb, cx, y, cell, StripeAttr[i]);
        }
    }
}
