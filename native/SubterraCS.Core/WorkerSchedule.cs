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
            // $EF0F just-picked path: bit 5 set last frame → clear it,
            // set bit 7 (permanently picked).  One-frame freeze.
            if ((w.Status & 0x20) != 0)
            {
                w.Status = (byte)((w.Status & ~0x20) | 0x80);
                continue;
            }
            if ((w.Status & 0x80) != 0) continue;          // already picked

            // 4-byte horizontal window: A in [playerByte..playerByte+3]
            int dx = (w.X - playerByte) & 0xFF;
            if (dx > 3) continue;
            // 3-row vertical window: row in [playerRow..playerRow+2]
            int dr = (w.Row - playerRow) & 0xFF;
            if (dr > 2) continue;

            // Pickup!  Port of $EFE0: set bit 5 (just-picked); the
            // NEXT frame's $EF0F pass converts it to bit 7.
            w.Status |= 0x20;
            rescued++;
            RescuedThisLevel++;
        }
        return rescued;
    }

    /// <summary>The single 8-byte worker sprite at <c>$F071</c>.
    /// The cassette has NO frame animation: $EF4E draws the SAME
    /// sprite every frame ($F071 for the white pass; the $F0F1
    /// "level-colour" sprite is 8 zero bytes — dump-verified), and
    /// the cycle byte ($EF2F INC; AND $1F) never selects frames.
    /// The bytes at $F079/$F081/$F089 exist in RAM but nothing ever
    /// indexes them.</summary>
    private static readonly byte[] WorkerSprite =
        { 0x18, 0x1C, 0x0E, 0x0E, 0x16, 0xAE, 0xCA, 0x49 };

    /// <summary>Port of <c>$EF28..$EF42</c> + <c>$EF9C</c>: each
    /// non-picked worker is drawn TWICE per frame with the OVERWRITE
    /// blitter (`LD (HL),A` — not XOR): first the all-zero $F0F1
    /// sprite with the level-colour attribute, then the $F071 sprite
    /// with white.  Net bitmap = the white-pass sprite stamped over
    /// the cell; the attribute lands on white but flickers against
    /// the level colour within the frame on real hardware.  We model
    /// that shimmer by alternating the attribute per host frame.</summary>
    public void DrawPlayfield(Framebuffer fb, int scrollCursor, byte levelColour, int frameCounter)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            ref var w = ref Slots[i];
            if ((w.Status & 0x80) != 0) continue;          // picked → hidden

            int offset = (w.X - scrollCursor) & 0xFF;
            if (offset >= 0x20) continue;
            int sx = offset * 8;
            int sy = w.Row * 8;
            if (sy + 8 > 128) continue;

            // Just-picked freeze frame ($EF1D → $EF28 without the
            // white pass): the zero $F0F1 sprite stamped with the
            // level colour — a blank cell for one frame.
            bool freeze = (w.Status & 0x20) != 0;

            // $EF2F: INC A; AND $1F — the cycle byte advances but
            // selects nothing; kept for state fidelity.
            w.Cycle = (byte)((w.Cycle + 1) & 0x1F);

            for (int row = 0; row < 8; row++)
            {
                int yy = sy + row;
                fb.Bitmap[Framebuffer.BitmapAddress(sx, yy)] = freeze ? (byte)0 : WorkerSprite[row];
            }
            byte attr = freeze ? levelColour
                : (frameCounter & 1) == 0 ? (byte)0x07 : levelColour;
            fb.Attributes[Framebuffer.AttributeAddress(sx, sy)] = attr;
        }
    }

    /// <summary>Port of <c>$F02E</c>: mini-map worker dots FLASH —
    /// the $F070 counter cycles 0..7 every 2 frames and bit 2 gates
    /// an OR-draw (on) vs AND-clear (off), so workers blink at ~3 Hz
    /// while ship dots stay steady.  Row = $A0 + 2·row ($F036
    /// LD A,$1F; SLA B; SUB B → scanline $BF-B).</summary>
    public void DrawMiniMapDots(Framebuffer fb, int frameCounter)
    {
        bool onCycle = ((frameCounter >> 1) & 0x04) != 0;   // bit 2 of the $F070 counter
        if (!onCycle) return;   // base strip is repainted per frame, so "off" = just skip
        for (int i = 0; i < SlotCount; i++)
        {
            ref var w = ref Slots[i];
            if ((w.Status & 0x80) != 0) continue;          // picked → hidden
            int miniX = (w.X + 1) & 0xFF;
            int miniY = 0xA0 + (w.Row * 2);
            if (miniY < 160 || miniY >= 192) continue;
            int addr = Framebuffer.BitmapAddress(miniX, miniY);
            byte bit = (byte)(0x80 >> (miniX & 7));
            fb.Bitmap[addr] |= bit;
        }
    }
}
