namespace SubterraCS.Core;

/// <summary>
/// Level-scroll system (port-in-progress).  Stub for the original's
/// <c>$DBC8</c> routine which scrolls the play area UP one row at
/// a time, with new scenery rows entering from below.
///
/// What we know (RE-LOG §36):
/// * <c>$DB85</c> walks bitmap rows in band 0, copying byte from
///   <c>(HL + $20)</c> to <c>(HL)</c> — content moves up one char
///   row.  Every 8th row zeros the source.
/// * <c>$DBC8</c> wraps the band-0 scroll plus three more calls
///   to <c>$DBDA</c> (band 1 + 2 scrolls).
/// * <c>$DAF2</c> (tile blitter) is called concurrently to draw
///   NEW tiles at the bottom of the play area before they scroll
///   up.  Trace at f140..f150 showed 768 writes from <c>$DB01</c>
///   (inside <c>$DAF2</c>) — ~96 tile draws over 10 frames.
/// * The scroll is triggered conditionally: <c>CP $08; JP C,$DBC8</c>
///   patterns at <c>$DDA7</c> and <c>$DDC0</c> fire when a comparison
///   value is &lt; 8.  Not every frame.
///
/// What's still TBD:
/// * The exact trigger condition (which state goes &lt; 8).
/// * The source of scenery tile indices (likely the
///   <c>$60F4..$70F4</c> per-level buffer we already extracted
///   for the mini-map — may double as scenery data).
/// * How the bottom-row draw at <c>$DAF2</c> coordinates with the
///   scroll cadence.
///
/// Until ported, the play area middle/bottom is blank in the
/// native render — visible as the f200+ diff jump (3.23% → 10.59%).
/// </summary>
public sealed class LevelScroll
{
    /// <summary>Pixel offset within a char row that scrolling has reached.</summary>
    public int FineY { get; private set; }

    public void Reset()
    {
        FineY = 0;
    }

    /// <summary>
    /// Scroll the play area (y=0..127) UP by one scanline.
    /// Port of <c>$DB85</c> — pulls byte from (x, y+1) to (x, y).
    /// Bottom row stays unchanged (caller is responsible for filling it).
    /// </summary>
    public static void ScrollUpOneScanline(Framebuffer fb)
    {
        for (int y = 0; y < World.PlayfieldBottom - 1; y++)
        {
            for (int col = 0; col < 32; col++)
            {
                int src = Framebuffer.BitmapAddress(col * 8, y + 1);
                int dst = Framebuffer.BitmapAddress(col * 8, y);
                fb.Bitmap[dst] = fb.Bitmap[src];
            }
        }
        // Clear the bottom row — caller fills with new scenery before
        // the next scroll.
        for (int col = 0; col < 32; col++)
        {
            fb.Bitmap[Framebuffer.BitmapAddress(col * 8, World.PlayfieldBottom - 1)] = 0;
        }
    }
}
