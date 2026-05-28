namespace SubterraCS.Core;

/// <summary>
/// The four sprite-drawing primitives we identified in
/// docs/MEMORY-MAP.md.  Each takes a target <see cref="Framebuffer"/>
/// and writes into its bitmap (and where applicable, its attributes).
/// </summary>
public static class Blitters
{
    /// <summary>
    /// 8×8 overwrite tile copy.  Mirrors the original at <c>$E03D</c>:
    /// 8 rows × 1 byte, plain assignment (no XOR).  Used for static
    /// scenery and the HUD font.
    ///
    /// <paramref name="x"/> is in pixels (snapped to byte boundary);
    /// <paramref name="y"/> is in pixel rows.  The tile lives at
    /// <c>tile[0..7]</c> as one byte per scanline.
    /// </summary>
    public static void DrawTile8x8(Framebuffer fb, int x, int y, ReadOnlySpan<byte> tile, byte? attr = null)
    {
        int col = x >> 3;
        if (col < 0 || col >= 32) return;
        for (int row = 0; row < 8; row++)
        {
            int yy = y + row;
            if ((uint)yy >= Framebuffer.Height) continue;
            fb.Bitmap[Framebuffer.BitmapAddress(x, yy)] = tile[row];
        }
        if (attr is byte a)
        {
            fb.Attributes[Framebuffer.AttributeAddress(x, y)] = a;
        }
    }

    /// <summary>
    /// 16×16 column-major quadrant blit, matching <c>$F2BC</c>'s outer
    /// 4-call wrapper.  <paramref name="sprite"/> is 32 bytes laid out
    /// as [TL 8 bytes][TR 8 bytes][BL 8 bytes][BR 8 bytes].
    /// </summary>
    public static void DrawSprite16x16(Framebuffer fb, int x, int y, ReadOnlySpan<byte> sprite, byte attr)
    {
        if (sprite.Length < 32) return;
        // Top half (rows 0..7)
        for (int row = 0; row < 8; row++)
        {
            int yy = y + row;
            if ((uint)yy < Framebuffer.Height && (uint)x < Framebuffer.Width)
            {
                fb.Bitmap[Framebuffer.BitmapAddress(x, yy)] = sprite[row];
            }
            if ((uint)yy < Framebuffer.Height && (uint)(x + 8) < Framebuffer.Width)
            {
                fb.Bitmap[Framebuffer.BitmapAddress(x + 8, yy)] = sprite[8 + row];
            }
        }
        // Bottom half (rows 8..15)
        for (int row = 0; row < 8; row++)
        {
            int yy = y + 8 + row;
            if ((uint)yy < Framebuffer.Height && (uint)x < Framebuffer.Width)
            {
                fb.Bitmap[Framebuffer.BitmapAddress(x, yy)] = sprite[16 + row];
            }
            if ((uint)yy < Framebuffer.Height && (uint)(x + 8) < Framebuffer.Width)
            {
                fb.Bitmap[Framebuffer.BitmapAddress(x + 8, yy)] = sprite[24 + row];
            }
        }
        // Paint the four 8×8 attribute cells.
        PaintAttr(fb, x,     y,     attr);
        PaintAttr(fb, x + 8, y,     attr);
        PaintAttr(fb, x,     y + 8, attr);
        PaintAttr(fb, x + 8, y + 8, attr);
    }

    /// <summary>
    /// 16×8 column-major XOR blit, matching the player draw at
    /// <c>$DCF5</c>.  <paramref name="sprite"/> is 16 bytes:
    /// [TL 8 bytes][TR 8 bytes]; the bottom 16 bytes are treated as
    /// zero (matches the live game: the Stryker is 16 × 8).
    ///
    /// Returns true if ANY non-transparent sprite byte was about to
    /// XOR into an already-non-zero screen byte — port of the
    /// <c>$DD25 INC (HL); DEC (HL); JR Z,$DD2C → $DD29 EX AF,AF';
    /// SCF; EX AF,AF'</c> shadow-carry flag, which the cassette uses
    /// at <c>$DD3A EX AF,AF'; CALL C,$DD4A</c> to fire the damage
    /// chain.  This is the cassette's PRIMARY collision trigger
    /// (see docs/disasm/collision.md).  Caller forwards this flag
    /// into the per-frame damage path.
    /// </summary>
    public static bool DrawPlayerXor(Framebuffer fb, int x, int y, ReadOnlySpan<byte> sprite, byte attr)
    {
        if (sprite.Length < 16) return false;
        bool overlap = false;
        for (int row = 0; row < 8; row++)
        {
            int yy = y + row;
            if ((uint)yy < Framebuffer.Height && (uint)x < Framebuffer.Width)
            {
                int idx = Framebuffer.BitmapAddress(x, yy);
                byte sp = sprite[row];
                if (sp != 0 && fb.Bitmap[idx] != 0) overlap = true;
                fb.Bitmap[idx] ^= sp;
            }
            if ((uint)yy < Framebuffer.Height && (uint)(x + 8) < Framebuffer.Width)
            {
                int idx = Framebuffer.BitmapAddress(x + 8, yy);
                byte sp = sprite[8 + row];
                if (sp != 0 && fb.Bitmap[idx] != 0) overlap = true;
                fb.Bitmap[idx] ^= sp;
            }
        }
        PaintAttr(fb, x,     y, attr);
        PaintAttr(fb, x + 8, y, attr);
        return overlap;
    }

    /// <summary>
    /// Single-byte XOR write — the bullet/particle primitive at
    /// <c>$E1DE</c>.  Toggles one screen byte at (x, y).
    /// </summary>
    public static void DrawBulletXor(Framebuffer fb, int x, int y, byte pattern, byte attr)
    {
        if ((uint)x >= Framebuffer.Width || (uint)y >= Framebuffer.Height) return;
        int idx = Framebuffer.BitmapAddress(x, y);
        fb.Bitmap[idx] ^= pattern;
        PaintAttr(fb, x, y, attr);
    }

    private static void PaintAttr(Framebuffer fb, int x, int y, byte attr)
    {
        if ((uint)x >= Framebuffer.Width || (uint)y >= Framebuffer.Height) return;
        fb.Attributes[Framebuffer.AttributeAddress(x, y)] = attr;
    }
}
