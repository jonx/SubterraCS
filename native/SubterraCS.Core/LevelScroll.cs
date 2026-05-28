namespace SubterraCS.Core;

/// <summary>
/// Level scenery painter — port of <c>$DB1A</c> (the level-load
/// paint routine).
///
/// What `$DB1A` does, per RE-LOG §37/§38 disassembly:
///
/// 1. Read the per-level source pointer from <c>$E56D + level*2</c>
///    (e.g. <c>$60F4</c> for level 1).  Stored in IX.
/// 2. Loop 16 times (one per char row of the play area):
///    a. Scroll the bitmap + attributes UP by 8 scanlines
///       (<c>$DB7A</c>).
///    b. Read 32 tile indices from <c>(IX..IX+31)</c>, blit each
///       tile from the master bank at <c>$B0F4</c> into the
///       bottom char row of the play area (y=120..127) via
///       <c>$DAF2</c>.
///    c. Paint 32 attribute cells in the bottom row with the
///       per-level colour byte from <c>$E57B</c>.
///    d. Advance IX by <c>$E0</c> (224 bytes).
/// 3. RET.
///
/// After all 16 iterations, the level's scenery is fully drawn
/// in the play area.  Each char row's tile indices live at
/// <c>$60F4 + (row * 256)</c> in the source buffer — verified by
/// matching the EXACT tile indices the emu draws on screen.
///
/// In the native port we drop the scroll/scroll-and-draw idiom
/// (it was just an implementation detail to fit the Z80's
/// stride math) and paint directly: for each char row 0..15,
/// blit 32 tiles from the master bank using the tile indices at
/// <c>(level buffer) + row * 256 + col</c>.
/// </summary>
public sealed class LevelScroll
{
    /// <summary>Bytes per source row (32 tile indices + 224-byte stride).</summary>
    public const int SourceStride = 256;

    /// <summary>Number of char rows the play area covers.</summary>
    public const int CharRows = 16;

    /// <summary>Per-level colour attribute (from <c>$E57C+level</c>).</summary>
    public byte LevelColour { get; set; } = 0x04;     // green ink on black

    /// <summary>Persistent play-area bitmap.  Loaded from the level
    /// at <see cref="PaintLevel"/> time, then static for the level.
    /// Same Spectrum-interleaved layout as <see cref="Framebuffer.Bitmap"/>;
    /// covers bands 0+1 (y=0..127).</summary>
    public byte[] PlayBitmap { get; } = new byte[4096];

    /// <summary>How many rows of the level have been scrolled in so far (0..16).</summary>
    public int ScrolledRows { get; private set; }

    public bool ScrollComplete => ScrolledRows >= CharRows;

    public void Reset()
    {
        Array.Clear(PlayBitmap, 0, PlayBitmap.Length);
        ScrolledRows = 0;
    }

    /// <summary>
    /// Advance the scroll-in by one step — port of one outer-loop
    /// iteration of <c>$DB1A</c>.  Scrolls the existing PlayBitmap UP
    /// by 8 scanlines, then paints the next source row at the bottom
    /// char row (y=120..127).
    /// </summary>
    public void ScrollOneStep(TileBank tileBank, byte[] levelBuffer)
    {
        if (ScrollComplete) return;
        if (levelBuffer.Length < CharRows * SourceStride) return;

        // Scroll the play area UP by 8 scanlines (one char row).
        // Spectrum's interleaved layout means we can't do a flat
        // memmove; iterate scanline by scanline.
        for (int y = 0; y < World.PlayfieldBottom - 8; y++)
        {
            int srcY = y + 8;
            for (int col = 0; col < 32; col++)
            {
                int src = Framebuffer.BitmapAddress(col * 8, srcY);
                int dst = Framebuffer.BitmapAddress(col * 8, y);
                PlayBitmap[dst] = PlayBitmap[src];
            }
        }

        // Paint the bottom char row (y=120..127) from source row K
        // where K = ScrolledRows + 1 (1-indexed match for $DB1A's
        // outer iteration counter).
        int srcBase = ScrolledRows * SourceStride;
        int bottomY = World.PlayfieldBottom - 8;
        for (int col = 0; col < 32; col++)
        {
            byte tileIdx = levelBuffer[srcBase + col];
            for (int sl = 0; sl < 8; sl++)
            {
                if (tileIdx == 0 || tileIdx >= tileBank.TileCount)
                {
                    PlayBitmap[Framebuffer.BitmapAddress(col * 8, bottomY + sl)] = 0;
                }
                else
                {
                    PlayBitmap[Framebuffer.BitmapAddress(col * 8, bottomY + sl)] = tileBank[tileIdx][sl];
                }
            }
        }

        ScrolledRows++;
    }

    /// <summary>
    /// Paint the entire level's scenery into <see cref="PlayBitmap"/>.
    /// Call this once at level-load.  For each char row 0..15 the
    /// tile indices are at <c>levelBuffer[row * SourceStride .. + 32]</c>.
    /// Each tile is 8 bytes from <c>tileBank</c>.
    /// </summary>
    public void PaintLevel(TileBank tileBank, byte[] levelBuffer)
        => PaintLevelAtOffset(tileBank, levelBuffer, 0);

    /// <summary>
    /// Paint the level scenery with a horizontal scroll offset (0..255).
    /// Each source row has 256 tile-index bytes; the visible window is
    /// 32 cols starting at <paramref name="offsetX"/>, wrapping at 256.
    ///
    /// Port of $DA23 / $DA62 (which shift the entire bitmap one byte
    /// left/right per L-press and paint a fresh column at the exposed
    /// edge).  We track the offset directly and re-paint, which is
    /// simpler and equivalent to the Z80 chain.
    /// </summary>
    public void PaintLevelAtOffset(TileBank tileBank, byte[] levelBuffer, int offsetX)
    {
        Array.Clear(PlayBitmap, 0, PlayBitmap.Length);
        if (levelBuffer.Length < CharRows * SourceStride) return;

        offsetX &= 0xFF;
        for (int row = 0; row < CharRows; row++)
        {
            int srcRowBase = row * SourceStride;
            int destY = row * 8;
            for (int col = 0; col < 32; col++)
            {
                int srcCol = (col + offsetX) & 0xFF;
                byte tileIdx = levelBuffer[srcRowBase + srcCol];
                if (tileIdx == 0 || tileIdx >= tileBank.TileCount) continue;
                var tile = tileBank[tileIdx];
                for (int sl = 0; sl < 8; sl++)
                {
                    PlayBitmap[Framebuffer.BitmapAddress(col * 8, destY + sl)] = tile[sl];
                }
            }
        }
    }

    /// <summary>Copy the persistent play-area bitmap into the
    /// framebuffer each draw frame.</summary>
    public void Blit(Framebuffer fb)
    {
        Buffer.BlockCopy(PlayBitmap, 0, fb.Bitmap, 0, PlayBitmap.Length);
    }
}
