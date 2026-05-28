namespace SubterraCS.Core;

/// <summary>
/// HUD layout reproduced from the original game's bottom-half chrome:
/// <pre>
///   row 128..159  : sky / playable area (left alone)
///   row 160..167  : DEPTH:   N                  RESCUED:NN
///   row 168..175  : SCORE: NNNNN
///   row 176..183  : SHIELD ▓▓▓▓▓▓▓▓▓▓▓▓▓▓
///   row 184..191  : FUEL   ▓▓▓▓▓▓▓▓▓▓▓▓▓▓
/// </pre>
/// Same DEPTH/SCORE/SHIELD/FUEL stack the original draws via <c>$E046</c>,
/// with the multi-colour shield + fuel bars made of solid 8×8 cells whose
/// attribute byte cycles through bright red→magenta→yellow→cyan to match
/// the original's striped fill.
/// </summary>
public static class Hud
{
    public const int HudTop = 160;

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
        for (int row = HudTop / 8; row < 24; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Attributes[row * 32 + col] = 0x07;
            }
        }

        // Left-stacked labels — bright white on black.
        MiniFont.Draw(fb, 0,   160, $"DEPTH: {world.Depth + 1,3}", 0x47);
        MiniFont.Draw(fb, 152, 160, $"RESCUED:{world.Rescued:D2}/{world.WorkersForThisLevel:D1}", 0x46);
        MiniFont.Draw(fb, 0,   168, $"SCORE: {world.Score:D5}", 0x46);

        DrawStripedBar(fb, 0,   176, "SHIELD", world.Shield);
        DrawStripedBar(fb, 0,   184, "FUEL  ", world.Fuel);

        // Lives — three magenta chips on the bottom-right.
        Span<byte> chip = stackalloc byte[8];
        for (int r = 1; r < 7; r++) chip[r] = 0x7E;
        for (int i = 0; i < Math.Min(world.Lives, 3); i++)
        {
            Blitters.DrawTile8x8(fb, 232 + i * 8, 176, chip, 0x43);
        }
    }

    /// <summary>
    /// Draws a striped multi-colour bar in the same idiom as the
    /// original — the bar is 16 cells of 8 pixels each, each cell
    /// taking its colour from a 4-step palette so the fill looks
    /// "rainbow"-like at full strength and fades back from the right
    /// as the value drops.
    /// </summary>
    private static void DrawStripedBar(Framebuffer fb, int x, int y, string label, int value)
    {
        MiniFont.Draw(fb, x, y, label, 0x47);
        int barStart = x + 8 * label.Length;
        const int Cells = 16;
        // 4-stripe palette: red, magenta, yellow, cyan (all bright).
        ReadOnlySpan<byte> stripe = stackalloc byte[] { 0x42, 0x43, 0x46, 0x45 };
        int filled = Math.Clamp(value * Cells / 100, 0, Cells);

        Span<byte> solid = stackalloc byte[8];
        Span<byte> empty = stackalloc byte[8];
        for (int r = 1; r < 7; r++) solid[r] = 0xFF;

        for (int i = 0; i < Cells; i++)
        {
            int cx = barStart + i * 8;
            if (cx + 8 > 256) break;
            byte attr = i < filled ? stripe[i & 3] : (byte)0x07;
            Blitters.DrawTile8x8(fb, cx, y, i < filled ? solid : empty, attr);
        }
    }
}
