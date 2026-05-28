namespace SubterraCS.Core;

/// <summary>
/// Port of the rescuable-workers subsystem at <c>$E75D</c>.  See
/// <c>docs/disasm/workers.md</c> for the full trace.
///
/// 8 workers per level, each with (worldX, row, cycle, status).
/// The player picks them up by overlapping (4 bytes wide × 3 rows
/// tall pickup zone — port of <c>$EFAE</c>); each pickup scores
/// +50 + RESCUED++.  After 8 rescues, the per-level cleared flag
/// at <c>$E77D + level</c> fires.
/// </summary>
public sealed class WorkerSchedule
{
    public const int SlotCount = 8;

    public struct Worker
    {
        public byte X;        // +0: world byte 0..255
        public byte Row;      // +1: char-row 0..15
        public byte Cycle;    // +2: animation counter
        public byte Status;   // +3: bit 5 = just-picked, bit 7 = picked-up
    }

    public readonly Worker[] Slots = new Worker[SlotCount];

    /// <summary>Count of workers picked up so far this level
    /// (per the original's <c>$E467</c> counter; level-clear at 8).</summary>
    public int RescuedThisLevel;

    public int RemainingThisLevel
    {
        get
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
                if ((Slots[i].Status & 0x80) == 0) n++;
            return n;
        }
    }

    public void Reset()
    {
        for (int i = 0; i < SlotCount; i++) Slots[i] = default;
        RescuedThisLevel = 0;
    }

    /// <summary>Port of <c>$E2E5</c> LDIR: copy 32 bytes from
    /// <c>$E69D + level*32</c> into the live table at <c>$E75D</c>.
    /// Source asset is <c>level-schedules-e69d.bin</c> (192 bytes
    /// = 6 levels × 32 each).</summary>
    public void LoadFromSchedule(byte[] scheduleData, int level)
    {
        Reset();
        int baseOff = level * 32;
        if (baseOff + SlotCount * 4 > scheduleData.Length) return;
        for (int i = 0; i < SlotCount; i++)
        {
            Slots[i].X      = scheduleData[baseOff + i*4 + 0];
            Slots[i].Row    = scheduleData[baseOff + i*4 + 1];
            Slots[i].Cycle  = scheduleData[baseOff + i*4 + 2];
            Slots[i].Status = scheduleData[baseOff + i*4 + 3];
        }
    }

    /// <summary>Port of <c>$EF08</c> per-frame tick.  For each worker
    /// not yet picked-up:
    /// - <c>$EFAE</c> pickup-zone check (4 bytes × 3 char-rows around player)
    /// - On overlap: score +50, RESCUED++, set bit 7 (permanently picked).
    /// Returns the number of NEW rescues this tick (caller uses to
    /// trigger score / SFX / level-cleared).</summary>
    public int Tick(int scrollCursor, int altitude)
    {
        int rescued = 0;
        int playerByte = (scrollCursor + 0x0E) & 0xFF;     // $EFB1 ADD A,$0E
        int playerRow = (altitude >> 3) & 0x1F;             // $EFCC SRL ×3
        for (int i = 0; i < SlotCount; i++)
        {
            ref var w = ref Slots[i];
            if ((w.Status & 0x80) != 0) continue;          // already picked

            // 4-byte horizontal window: A in [playerByte..playerByte+3]
            int dx = (w.X - playerByte) & 0xFF;
            if (dx > 3) continue;
            // 3-row vertical window: row in [playerRow..playerRow+2]
            int dr = (w.Row - playerRow) & 0xFF;
            if (dr > 2) continue;

            // Pickup!  Port of $EFE0.
            w.Status |= 0x80;
            rescued++;
            RescuedThisLevel++;
        }
        return rescued;
    }

    /// <summary>Port of <c>$EF4E</c> + <c>$EF9C</c>: per-worker 8×8
    /// playfield blit at (worldX - scrollCursor, row*8).  Drawn twice
    /// in the original (level-color + white for blink); we use a
    /// single bright-yellow pass for simplicity.
    /// Also port of <c>$F02E</c> mini-map dot at (X, mini-map row).</summary>
    public void Draw(Framebuffer fb, int scrollCursor, byte levelAttr)
    {
        // Worker sprite from $F071 (the white "draw" variant) —
        // verified bytes from at-f100.bin.  $F0F1 is the level-color
        // "erase" variant (= all zeros; the original draws both each
        // frame, erase-then-draw).  We just draw the white sprite.
        // The cassette has 4 frames at $F071/$F079/$F081/$F089 for
        // a shovel-swing animation; we use frame 0 for now.
        ReadOnlySpan<byte> sprite = stackalloc byte[]
        {
            0x18, 0x1C, 0x0E, 0x0E, 0x16, 0xAE, 0xCA, 0x49,
        };
        for (int i = 0; i < SlotCount; i++)
        {
            ref var w = ref Slots[i];
            if ((w.Status & 0x80) != 0) continue;          // picked → hidden

            int offset = (w.X - scrollCursor) & 0xFF;
            // Playfield sprite (range gate, $EF52: SUB; CP $20; RET NC)
            if (offset < 0x20)
            {
                int sx = offset * 8;
                int sy = w.Row * 8;
                if (sy + 8 <= 128)
                {
                    for (int row = 0; row < 8; row++)
                    {
                        int yy = sy + row;
                        fb.Bitmap[Framebuffer.BitmapAddress(sx, yy)] ^= sprite[row];
                    }
                    fb.Attributes[Framebuffer.AttributeAddress(sx, sy)] = 0x46;  // bright yellow
                }
            }

            // Mini-map dot at (w.X, mini-map row).  Port of $F02E
            // alternation skipped — single non-flashing dot.
            int miniX = (w.X + 1) & 0xFF;
            int miniY = 0xA1 + (w.Row * 2);   // row direct → 2 rows of mini-map per char-row
            if (miniY >= 160 && miniY < 192)
            {
                int addr = Framebuffer.BitmapAddress(miniX, miniY);
                byte bit = (byte)(0x80 >> (miniX & 7));
                fb.Bitmap[addr] ^= bit;
            }
        }
    }
}
