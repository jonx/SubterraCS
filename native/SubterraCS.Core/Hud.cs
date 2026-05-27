namespace SubterraCS.Core;

/// <summary>
/// Renders the HUD into a <see cref="Framebuffer"/> using a small
/// hand-built 8 × 8 font (A-Z, 0-9, space, period, colon, slash, dash).
/// Keeps the native build free of the Spectrum ROM character set the
/// original game leans on.
///
/// Layout (matches the rough shape of the original):
///   row 168:  DEPTH:###  SCORE:#####  RESCUED:##
///   row 176:  SHIELD bar (red→yellow→green)
///   row 184:  FUEL   bar (cyan)
/// </summary>
public static class Hud
{
    public static void Draw(Framebuffer fb, World world)
    {
        // Black-out the HUD rows so the cave decor at row 184 doesn't
        // bleed through.  Keep the cave colour everywhere else.
        for (int row = 22; row < 24; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Attributes[row * 32 + col] = 0x47; // bright white on black
            }
        }
        for (int y = 168; y < 192; y++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Bitmap[Framebuffer.BitmapAddress(col * 8, y)] = 0;
            }
        }

        DrawString(fb, 0,  168, $"DEPTH:{world.Depth:D3}", 0x47);
        DrawString(fb, 88, 168, $"SCORE:{world.Score:D5}", 0x46);
        DrawString(fb, 184,168, $"RES:{world.Rescued:D2}", 0x44);

        DrawBar(fb, 0,   176, "SH", world.Shield, 0x42);   // red(ish)
        DrawBar(fb, 128, 176, "FU", world.Fuel,   0x45);   // cyan
    }

    private static void DrawBar(Framebuffer fb, int x, int y, string label, int value, byte attr)
    {
        DrawString(fb, x, y, label, attr);
        // 12-cell bar starting after the 2-char label + space.
        int bx = x + 24;
        int cells = Math.Clamp(value / 8, 0, 12);   // value 0..100 → 0..12 cells
        Span<byte> solid = stackalloc byte[8];
        Span<byte> empty = stackalloc byte[8];
        for (int r = 0; r < 8; r++) { solid[r] = 0x7C; empty[r] = 0x00; }
        for (int i = 0; i < 12; i++)
        {
            int xx = bx + i * 8;
            Blitters.DrawTile8x8(fb, xx, y, i < cells ? solid : empty, attr);
        }
    }

    private static void DrawString(Framebuffer fb, int x, int y, string s, byte attr)
    {
        foreach (var ch in s)
        {
            if (x >= Framebuffer.Width) return;
            var glyph = MiniFont.Glyph(ch);
            Blitters.DrawTile8x8(fb, x, y, glyph, attr);
            x += 8;
        }
    }
}
