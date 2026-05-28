namespace SubterraCS.Core;

/// <summary>
/// Bottom-strip data buffer — what the user identified as the
/// level mini-map.  The structure is partially verified, partially
/// hypothesis; this file documents both honestly.
///
/// VERIFIED facts:
///
/// * The bottom-strip bitmap (y=160..191) has the same content at
///   frame 100 and frame 1500 — once drawn, it doesn't change.
/// * The bitmap pattern has paired-scanlines (byte 0 == byte 1, etc.)
///   which is the signature of vertical 2× stretch.
/// * <c>$60F4..$70F3</c> contains 37% non-zero data at frame 100,
///   and that data is BYTE-IDENTICAL at frame 60 → some early init
///   populates it once.  This region is NOT changing during play.
/// * <c>$E104</c> is a 4096-byte backwards walker that calls
///   <c>$E127</c> for every non-zero byte; the screen-row math
///   computes a row 0..30 in steps of 2, matching 16 source rows
///   stretched 2× to 32 screen rows.
///
/// HYPOTHESIS (not yet pixel-tested):
///
/// * The 4096 bytes at <c>$60F4..$70F3</c> drive the mini-map via
///   <c>$E104</c>, with each byte mapping to one screen pixel.
///   Whether the byte's VALUE matters (as a pixel mask) or only its
///   non-zero-ness (as a stamp marker) needs further test.
///
/// UNKNOWNS:
///
/// * The 272 bitmap-byte differences we observed between f60 and f100
///   in the y=160..191 region are NOT driven by changes in
///   <c>$60F4..$70F3</c> (which is static).  They must come from
///   another source — likely entity sprites that happen to draw in
///   the bottom-strip area (e.g. type 8 explosions spawn at y=179
///   per our level-1 entity records).  That means the bottom strip
///   = static mini-map background + overlapping entity sprites.
///
/// This class owns the buffer and the walker.  Until we extract a
/// per-level base mini-map from the running game (or trace the code
/// that fills it), the buffer stays empty and the strip renders blank.
/// </summary>
public sealed class MiniMap
{
    /// <summary>Source-buffer rows.  Each row = 256 bytes (one byte per pixel).</summary>
    public const int Rows = 16;
    public const int Cols = 256;
    public const int BufferSize = Rows * Cols;  // 4096

    /// <summary>Y-coordinate of the first screen row the mini-map paints to.</summary>
    public const int ScreenTop = 160;
    public const int ScreenHeight = 32;          // 16 source rows × 2 (vertical stretch)

    /// <summary>The 16 × 256 source buffer (read by the walker).</summary>
    public byte[] Buffer { get; } = new byte[BufferSize];

    public void Clear() => Array.Clear(Buffer, 0, Buffer.Length);

    /// <summary>
    /// Set a single source-pixel at (col, row).  <paramref name="row"/>
    /// is 0..15, <paramref name="col"/> is 0..255.  The on-screen pixel
    /// will appear at screen y = <see cref="ScreenTop"/> + 2*row and
    /// y+1 (vertical 2× stretch), at screen x = col.
    /// </summary>
    public void SetPixel(int col, int row)
    {
        if ((uint)col >= Cols || (uint)row >= Rows) return;
        Buffer[row * Cols + col] = 0xFF;
    }

    /// <summary>
    /// Port of <c>$E104</c>: walk the source buffer, OR each non-zero
    /// byte into the framebuffer's bottom-strip bitmap.  The original
    /// uses a single bit per pixel but its source bytes are the full
    /// 8-bit value; we mirror that by writing the source byte into the
    /// bitmap at the right offset.  Each source row is drawn TWICE for
    /// the vertical 2× stretch.
    /// </summary>
    public void DrawTo(Framebuffer fb)
    {
        for (int row = 0; row < Rows; row++)
        {
            int screenY1 = ScreenTop + row * 2;
            int screenY2 = screenY1 + 1;
            if (screenY2 >= Framebuffer.Height) continue;
            for (int col = 0; col < Cols; col++)
            {
                byte src = Buffer[row * Cols + col];
                if (src == 0) continue;
                // The original sets one pixel per source byte; for the
                // native port we render the byte as a tight horizontal
                // segment proportional to its value (preserves the
                // "fill density" semantics without needing to know the
                // exact bit-to-pixel mapping the original uses).
                int xByte = col >> 3;
                int bit = 0x80 >> (col & 7);
                fb.Bitmap[Framebuffer.BitmapAddress(xByte * 8, screenY1)] |= (byte)bit;
                fb.Bitmap[Framebuffer.BitmapAddress(xByte * 8, screenY2)] |= (byte)bit;
            }
        }
    }
}
