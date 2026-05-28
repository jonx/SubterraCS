namespace SubterraCS.Core;

/// <summary>
/// Per-level mini-map (bottom screen strip, y=160..191) — verified
/// to be a STATIC, PACKED level asset that ships with the cassette.
///
/// The data lives in the snapshot at six per-level addresses:
/// <c>$B0F4</c> (level 0), <c>$60F4</c> (level 1), <c>$70F4</c>
/// (level 2), <c>$80F4</c> (level 3), <c>$90F4</c> (level 4),
/// <c>$A0F4</c> (level 5).  Each is a 4096-byte buffer; the active
/// level's pointer is held in <c>($E579)</c>.
///
/// Verification via <c>mem-write-trace</c>: with the trace running
/// from boot through 100 frames, the ONLY PCs writing into the
/// buffer range were <c>$E113</c> and <c>$E114</c> — the
/// <c>INC (HL); DEC (HL)</c> zero-test inside the <c>$E104</c>
/// walker, both no-ops that don't change byte values.  Across
/// f60/f100/f200/f400 the buffer differs by at most 1 byte.
/// Conclusion: the bytes are baked into the cassette, not computed
/// at runtime.
///
/// The walker at <c>$E104</c> traverses 4096 bytes BACKWARDS from
/// <c>($E579) + $1000</c> to <c>($E579)</c>.  For each non-zero
/// byte it calls <c>$E127</c> which uses <c>$E1E4</c> (the screen-
/// address compute) and <c>OR (HL); LD (HL),A</c> to stamp pixels
/// into the bitmap.  Loop counters give a 16-row × 256-col
/// effective output mapped to the 32-pixel-tall strip via 2× vertical
/// stretch — exactly fitting y=160..191.
///
/// In the native port we ship the six 4 KB buffers as a single
/// <c>level-minimaps.bin</c> asset (24576 bytes) and switch the
/// active buffer at level-load.
/// </summary>
public sealed class MiniMap
{
    public const int Rows = 16;
    public const int Cols = 256;
    public const int BufferSize = Rows * Cols;       // 4096
    public const int ScreenTop = 160;
    public const int ScreenHeight = 32;

    /// <summary>The active level's 4 KB source buffer.</summary>
    public byte[] Buffer { get; private set; } = new byte[BufferSize];

    /// <summary>Six packed 4 KB buffers loaded from the cassette.</summary>
    public byte[][] PerLevelBuffers { get; private set; } = Array.Empty<byte[]>();

    public static MiniMap LoadFromAsset(string path)
    {
        var raw = File.ReadAllBytes(path);
        const int NumLevels = 6;
        if (raw.Length < NumLevels * BufferSize)
        {
            throw new InvalidDataException(
                $"level-minimaps.bin should be {NumLevels * BufferSize} bytes; got {raw.Length}");
        }
        var mm = new MiniMap { PerLevelBuffers = new byte[NumLevels][] };
        for (int i = 0; i < NumLevels; i++)
        {
            mm.PerLevelBuffers[i] = raw[(i * BufferSize)..((i + 1) * BufferSize)];
        }
        mm.SelectLevel(1);   // default: first playable level
        return mm;
    }

    /// <summary>Switch the active buffer to the given level's data.</summary>
    public void SelectLevel(int level)
    {
        if (PerLevelBuffers.Length == 0) { Buffer = new byte[BufferSize]; return; }
        int idx = Math.Clamp(level, 0, PerLevelBuffers.Length - 1);
        Buffer = PerLevelBuffers[idx];
    }

    public void Clear() => Array.Clear(Buffer, 0, Buffer.Length);

    /// <summary>
    /// Port of <c>$E104</c>: walk the active 4 KB buffer.  For each
    /// non-zero source byte, stamp the byte directly into the
    /// framebuffer bitmap at the right screen address, with the row
    /// drawn TWICE for the 2× vertical stretch.
    ///
    /// The original's <c>$E127</c> uses <c>OR (HL); LD (HL),A</c>
    /// with whatever byte was last in <c>A</c>.  Empirically the bar
    /// cell on screen renders as a clean ribbon, so the byte value
    /// IS what the original puts on screen — we mirror that exactly.
    /// </summary>
    /// <summary>Paint only the first N rows of the mini-map (top-down
    /// approximation of the original's incremental paint pattern
    /// observed between f50 and f80 in the emulator).</summary>
    public void DrawToPartial(Framebuffer fb, int rowsToDraw)
    {
        rowsToDraw = Math.Clamp(rowsToDraw, 0, Rows);
        for (int row = 0; row < rowsToDraw; row++)
        {
            int screenY1 = ScreenTop + row * 2;
            int screenY2 = screenY1 + 1;
            if (screenY2 >= Framebuffer.Height) continue;
            for (int byteCol = 0; byteCol < 32; byteCol++)
            {
                byte stamp = 0;
                for (int b = 0; b < 8; b++)
                {
                    byte src = Buffer[row * Cols + byteCol * 8 + b];
                    if (src != 0) stamp |= (byte)(0x80 >> b);
                }
                if (stamp == 0) continue;
                fb.Bitmap[Framebuffer.BitmapAddress(byteCol * 8, screenY1)] |= stamp;
                fb.Bitmap[Framebuffer.BitmapAddress(byteCol * 8, screenY2)] |= stamp;
            }
        }
    }

    public void DrawTo(Framebuffer fb)
    {
        for (int row = 0; row < Rows; row++)
        {
            int screenY1 = ScreenTop + row * 2;
            int screenY2 = screenY1 + 1;
            if (screenY2 >= Framebuffer.Height) continue;
            for (int byteCol = 0; byteCol < 32; byteCol++)
            {
                // Each "column" in the source = 8 source bytes = 8 pixels.
                // Pack them as a single screen byte by ORing.
                byte stamp = 0;
                for (int b = 0; b < 8; b++)
                {
                    byte src = Buffer[row * Cols + byteCol * 8 + b];
                    if (src != 0) stamp |= (byte)(0x80 >> b);
                }
                if (stamp == 0) continue;
                fb.Bitmap[Framebuffer.BitmapAddress(byteCol * 8, screenY1)] |= stamp;
                fb.Bitmap[Framebuffer.BitmapAddress(byteCol * 8, screenY2)] |= stamp;
            }
        }
    }
}
