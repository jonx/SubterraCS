namespace SubterraCS.Core;

/// <summary>
/// Level-scroll system — port of the original's <c>$DBC8</c> /
/// <c>$DB85</c> routines that scroll the play area UP and bring
/// new scenery rows in from below.  RE-LOG §36 has the full trace.
///
/// Pipeline matched to the original:
/// 1. <see cref="DrawBottomTileRow"/> — port of <c>$DAF2</c>.
///    Reads 32 tile indices from the per-level scenery buffer (the
///    same <c>$60F4..$70F4</c> data we use for the mini-map) and
///    blits 8 scanlines from the master tile bank at <c>$B0F4</c>
///    into the bottom char-row of the play area.
/// 2. <see cref="ScrollUpOneCharRow"/> — port of <c>$DB85</c>.
///    Copies char row N+1 into char row N for each of the 16 char
///    rows in the play area.  Content moves up; the bottom row is
///    overwritten by the next call to <see cref="DrawBottomTileRow"/>.
///
/// Source advance: each scroll consumes 32 tile indices from the
/// source buffer (one tile row).  4096 bytes / 32 = 128 source rows
/// per level, which is ~5 screen-heights of vertical scenery.
/// </summary>
public sealed class LevelScroll
{
    /// <summary>Current row offset into the scenery source.  Advances
    /// each scroll.  Wraps at the buffer end.</summary>
    public int SourceRow { get; private set; }

    /// <summary>
    /// Persistent play-area bitmap.  Mirrors the same Spectrum
    /// bitmap layout as <see cref="Framebuffer.Bitmap"/> but is
    /// owned by World rather than cleared every frame.
    /// 4096 bytes covers bands 0 + 1 (y=0..127).
    /// </summary>
    public byte[] PlayBitmap { get; } = new byte[4096];

    public void Reset()
    {
        SourceRow = 0;
        Array.Clear(PlayBitmap, 0, PlayBitmap.Length);
    }

    /// <summary>
    /// One scroll tick.  Order matches the original's <c>$DBC8</c>:
    /// the new bottom row is drawn FIRST (so the just-drawn content
    /// scrolls up next frame), then the play area is scrolled.
    /// </summary>
    public void Tick(TileBank tileBank, byte[] sceneryBuffer)
    {
        DrawBottomTileRow(PlayBitmap, tileBank, sceneryBuffer, SourceRow);
        ScrollUpOneCharRow(PlayBitmap);
        SourceRow = (SourceRow + 1) % 128;  // 4096 / 32 cols = 128 rows
    }

    /// <summary>Copy the persistent play-area bitmap into the
    /// framebuffer's bitmap region.  Called every frame after the
    /// framebuffer is cleared, before HUD and entities draw.</summary>
    public void Blit(Framebuffer fb)
    {
        // PlayBitmap covers bands 0 + 1 ($0..$0FFF in offset terms).
        // Use Spectrum interleaved addressing so the bytes line up.
        Buffer.BlockCopy(PlayBitmap, 0, fb.Bitmap, 0, PlayBitmap.Length);
    }

    /// <summary>
    /// Port of <c>$DAF2</c>: read 32 tile indices from the source
    /// buffer at <c>sourceRow * 32</c>, blit each 8-byte tile from
    /// the master bank into the bottom char-row of the play area
    /// (y=120..127).
    /// </summary>
    private static void DrawBottomTileRow(
        byte[] bitmap, TileBank tileBank, byte[] sceneryBuffer, int sourceRow)
    {
        if (sceneryBuffer.Length < (sourceRow + 1) * 32) return;
        int bottomY = World.PlayfieldBottom - 8;          // y=120
        for (int col = 0; col < 32; col++)
        {
            byte tileIdx = sceneryBuffer[sourceRow * 32 + col];
            var tile = tileIdx < tileBank.TileCount
                ? tileBank[tileIdx]
                : ReadOnlySpan<byte>.Empty;
            if (tile.IsEmpty) continue;
            for (int sl = 0; sl < 8; sl++)
            {
                bitmap[Framebuffer.BitmapAddress(col * 8, bottomY + sl)] = tile[sl];
            }
        }
    }

    /// <summary>
    /// Port of <c>$DB85</c>: scroll the play area UP by 8 scanlines
    /// (one char row).  Each char row N gets the content from char
    /// row N+1; the bottom char row is cleared (the next call to
    /// <see cref="DrawBottomTileRow"/> repaints it).
    /// </summary>
    private static void ScrollUpOneCharRow(byte[] bitmap)
    {
        const int Bottom = World.PlayfieldBottom;          // 128
        for (int y = 0; y < Bottom - 8; y++)
        {
            int srcY = y + 8;
            for (int col = 0; col < 32; col++)
            {
                int src = Framebuffer.BitmapAddress(col * 8, srcY);
                int dst = Framebuffer.BitmapAddress(col * 8, y);
                bitmap[dst] = bitmap[src];
            }
        }
        for (int y = Bottom - 8; y < Bottom; y++)
        {
            for (int col = 0; col < 32; col++)
            {
                bitmap[Framebuffer.BitmapAddress(col * 8, y)] = 0;
            }
        }
    }
}
