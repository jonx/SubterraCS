namespace SubterraCS.Core;

/// <summary>
/// STUB — enemy-ship subsystem at <c>$E597</c>.  See
/// <c>docs/disasm/enemies.md</c> for the source trace.
///
/// 7-slot live table populated at level-load from <c>$E48D</c>+
/// (via <c>$E319</c>).  Each ship is visible as a dot on the
/// mini-map (via <c>$E213</c> / <c>$E235</c>), processed per-frame
/// by <c>$E920</c> with a 4-cycle time-slice (cycle counter at
/// <c>$E48B</c>), and fires bullets that land in
/// <see cref="EnemyBullets"/> via the <c>$EBB2</c> chain.
///
/// Per-cycle sprite data sits at <c>$E5DB</c> (4 banks × 8 bytes).
///
/// All methods are stubs — to be implemented once the full <c>$E920</c>
/// AI is traced.
/// </summary>
public sealed class EnemyShips
{
    public const int SlotCount = 7;

    public struct Ship
    {
        public byte X;        // +0: world byte (0..255)
        public byte Y;        // +1: pixel Y
        public byte Status;   // +2: bit 7 = alive
        public byte Sub;      // +3: AI sub-state / frame
    }

    public readonly Ship[] Slots = new Ship[SlotCount];

    /// <summary>$E48B — global 4-cycle counter for AI time-slicing.</summary>
    public int Cycle;

    /// <summary>$E5DB — 4 banks × 8 bytes of per-cycle sprite data.
    /// Populated from the asset at level-load.</summary>
    public byte[] SpriteBanks = new byte[4 * 8];

    public bool IsAlive(int i) => (Slots[i].Status & 0x80) != 0;

    public void Reset()
    {
        for (int i = 0; i < SlotCount; i++) Slots[i] = default;
        Cycle = 0;
    }

    /// <summary>Load the 7 ships' (X, Y, status, ?) from the level's
    /// init-data block at <c>$E48D + level*32</c>.  Port of
    /// <c>$E319</c>'s LDIR — 32 bytes copied = 8 records.  We use 7
    /// of them (matching the $E597 stride-4 loop count in
    /// <c>$DD67</c>/<c>$E213</c>); the 8th slot is something else (TBD).</summary>
    public void LoadFromInit(byte[] initData, int level)
    {
        Reset();
        int baseOff = level * 32;
        if (baseOff + SlotCount * 4 > initData.Length) return;
        for (int i = 0; i < SlotCount; i++)
        {
            Slots[i].X      = initData[baseOff + i*4 + 0];
            Slots[i].Y      = initData[baseOff + i*4 + 1];
            Slots[i].Status = initData[baseOff + i*4 + 2];
            Slots[i].Sub    = initData[baseOff + i*4 + 3];
        }
    }

    /// <summary>STUB — port of <c>$E8FD</c> entity supercaller:
    /// mini-map draw, AI tick, mini-map blink, bullet tick, collision.
    /// </summary>
    public void TickAndDraw(Framebuffer fb, int scrollCursor, int playerByteX, int playerY,
                            EnemyBullets bullets, Random rng)
    {
        DrawMiniMapDots(fb, scrollCursor);     // $E213 first pass
        TickAi(scrollCursor, playerByteX, playerY, bullets, rng);   // $E920
        // $EC10 boss tick goes here (Boss class)
        DrawMiniMapDots(fb, scrollCursor);     // $E213 second pass (blink)
        // bullets.Tick(...) called by caller after $ED00
        // collision pass via $DD4D handled by caller
    }

    /// <summary>Port of <c>$E213</c>/<c>$E235</c>/<c>$E1DE</c>:
    /// draw one mini-map pixel per alive ship.
    ///   B = $1E - Y/4; passed to $E1DE which computes
    ///   scanline = $BF - B = $A1 + Y/4 = 161 + Y/4 (mini-map row).
    ///   C = X + 1; lower 3 bits pick the pixel within the byte,
    ///   upper bits the byte column.  $E1DE XORs the bit into
    ///   the screen byte.
    /// Mini-map covers y=160..191 (32 px tall) and the full 256 px
    /// width, so each world byte (0..255) maps to one mini-map pixel.</summary>
    public void DrawMiniMapDots(Framebuffer fb, int scrollCursor)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (!IsAlive(i)) continue;
            int pixelX = (Slots[i].X + 1) & 0xFF;
            int pixelY = 0xA1 + (Slots[i].Y >> 2);   // 161 + Y/4
            if (pixelY < 160 || pixelY >= 192) continue;
            int addr = Framebuffer.BitmapAddress(pixelX, pixelY);
            byte bit = (byte)(0x80 >> (pixelX & 7));
            fb.Bitmap[addr] ^= bit;
        }
    }

    /// <summary>STUB — port of <c>$E920</c>: per-cycle AI dispatch.
    /// Reads <see cref="SpriteBanks"/> at offset <see cref="Cycle"/> * 16
    /// (= 4 banks of 16 bytes in the original; we use 4×8).  For each
    /// alive ship, runs <c>$E97F..$E99C</c> sub-logic which:
    ///   - if level &lt; 6, calls <c>$EAA6</c> path
    ///   - else <c>$EADE</c> + <c>$EB5B</c> repeated
    /// Then draws via <c>$E9AC</c> twice.  Possibly fires bullets
    /// via <c>$EBB2</c> indirectly during the inner loop.</summary>
    public void TickAi(int scrollCursor, int playerByteX, int playerY,
                        EnemyBullets bullets, Random rng)
    {
        // TODO: $E920 chain
        Cycle = (Cycle + 1) & 0x03;
    }

    /// <summary>STUB — port of <c>$E9AC</c>: blit one 8x8 cell of the
    /// per-cycle sprite at the ship's screen position.</summary>
    public void DrawShipSprite(Framebuffer fb, int slot, int scrollCursor)
    {
        // TODO: implement
    }
}

/// <summary>
/// STUB — boss / special entity at <c>$EE7D..$EE84</c> (single slot).
/// Spawned by <c>$EC10</c> when scroll-progress (<c>$EE74</c>)
/// reaches <c>$4A38</c> with a 1-in-2-ish random gate.  Calls
/// <c>$F8F9</c> to print a warning message on screen, then ticks
/// via <c>$EC4C</c> each frame (twice with extra chance from
/// <c>$EC45 LD A,R; CP $16</c>).
/// </summary>
public sealed class BossEntity
{
    public byte X;             // +0
    public byte Y;             // +1
    public byte Status;        // +2
    public byte Sub;           // +3
    public byte Frame;         // +4 / cycle
    public byte Reserved5;     // +5
    public byte Reserved6;     // +6
    public byte LifetimeCheck; // +7

    public bool Active;             // $EE7C: 0 = not spawned, 1 = active
    public byte KillCount;          // $EE83
    public byte AltFrame;           // $EE82 (toggles 0/1 each frame)
    public int  ScrollProgress;    // $EE74 — increments with player scroll

    public const int SpawnThreshold = 0x4A38;  // $EC1A

    public void Reset()
    {
        X = Y = Status = Sub = Frame = Reserved5 = Reserved6 = LifetimeCheck = 0;
        Active = false;
        KillCount = 0;
        AltFrame = 0;
        ScrollProgress = 0;
    }

    /// <summary>STUB — port of <c>$EC10</c>: spawn check + per-frame
    /// processing.  Triggers boss spawn after enough scroll progress.</summary>
    public void Tick(int scrollCursor, int playerByteX, int playerY, Random rng)
    {
        // TODO: $EC10 chain
    }

    /// <summary>STUB — boss draw routine.</summary>
    public void Draw(Framebuffer fb, int scrollCursor)
    {
        // TODO: draw boss sprite if Active
    }
}
