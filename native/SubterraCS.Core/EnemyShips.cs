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
    /// <c>$E319</c>'s LDIR — 32 bytes copied = 8 records.  Only 7 are
    /// ever used: every consumer loop in the cassette ($DD67 walker,
    /// $E213 mini-map, $E920 AI) iterates 7 slots, and no instruction
    /// in the binary references the 8th slot's bytes at $E5B3..$E5B6.
    /// The 8th record (e.g. level 1's `50 58 80 00` — a plausible
    /// alive ship at X=$50 Y=$58) is dead data: either a cut 8th ship
    /// or padding so the LDIR length is a round $20.</summary>
    public void LoadFromInit(byte[] initData, int level, byte[]? generated = null)
    {
        Reset();
        // Generated pages (modern depth 6+) supply their own 32-byte
        // block in the same $E48D record format.
        byte[] src = generated ?? initData;
        int baseOff = generated != null ? 0 : level * 32;
        if (baseOff + SlotCount * 4 > src.Length) return;
        for (int i = 0; i < SlotCount; i++)
        {
            Slots[i].X      = src[baseOff + i*4 + 0];
            Slots[i].Y      = src[baseOff + i*4 + 1];
            Slots[i].Status = src[baseOff + i*4 + 2];
            Slots[i].Sub    = src[baseOff + i*4 + 3];
        }
        // Verified contents of $E5DB at f100: 4 banks × 8 bytes of an
        // animated alien sprite (the bytes encode an 8x8 silhouette).
        // We bake this from the snapshot since it's static cassette data.
        SpriteBanks = new byte[]
        {
            0x3C, 0xFF, 0xDB, 0x7E, 0x66, 0xA5, 0x42, 0x00,   // frame 0
            0x3C, 0xFF, 0xDB, 0x7E, 0x66, 0x81, 0x66, 0x00,   // frame 1
            0x3C, 0xFF, 0xDB, 0x7E, 0x66, 0x81, 0x42, 0x42,   // frame 2
            0x3C, 0xE7, 0xFF, 0x7E, 0x66, 0x81, 0x42, 0x81,   // frame 3
        };
    }

    /// <summary>Draw the PLAYFIELD ship sprites only.  Called from
    /// the main draw loop BEFORE Hud.Draw (which clears y=128..191).
    /// Mini-map dots come via <see cref="DrawMiniMapDots"/> AFTER
    /// the HUD + MiniMap.DrawTo base layer.</summary>
    public void Draw(Framebuffer fb, int scrollCursor, byte levelAttr)
    {
        DrawShipSprites(fb, scrollCursor, levelAttr);
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
            // $E1DE XORs the bit and leaves the strip's attribute
            // untouched — no colour override.
            int addr = Framebuffer.BitmapAddress(pixelX, pixelY);
            byte bit = (byte)(0x80 >> (pixelX & 7));
            fb.Bitmap[addr] ^= bit;
        }
    }

    /// <summary>Port of <c>$E920</c> — semantic interpretation of the
    /// ship AI rather than a byte-faithful EXX/alt-bank port.  Models
    /// the cassette's behaviour:
    /// 1. Every-other-frame skip via <see cref="OddFrameToggle"/>
    ///    (= <c>$EE73</c>).
    /// 2. 4-cycle counter advance for <see cref="Cycle"/> (= <c>$E48B</c>).
    /// 3. Per alive slot:
    ///    - Step Y up/down within <c>[4, $70]</c> via the <c>$EB00</c>
    ///      counter-with-bit-5-bounce pattern.
    ///    - Probe scenery via <c>$EB62</c>; on hit, reverse direction
    ///      (<c>$EB47</c>).
    ///    - Range gate via <c>$EAB2</c>: ships outside the visible
    ///      32-byte window aren't ticked further.
    ///    - Fire a bullet via <c>$EBB2</c> gated by <c>$EB99</c>'s
    ///      <c>LD A,R; AND $0F; CP level; RET NC</c> random gate.
    /// 4. Dead slots: re-spawn at random world X if <see cref="Level"/>
    ///    permits (matches <c>$EADE</c>'s respawn).</summary>
    private byte OddFrameToggle;   // $EE73

    /// <summary>Bitmask of slot indices that just rammed the player
    /// during the last <see cref="TickAi"/> call.  Caller uses this to
    /// fire damage (port of <c>$EB7A → $DD4A</c> chain).</summary>
    public int LastTickHits { get; private set; }

    public void TickAi(int scrollCursor, int playerByteX, int playerY,
                        EnemyBullets bullets, Random rng, int level,
                        byte[]? levelTiles = null, bool modernRespawn = false)
    {
        LastTickHits = 0;
        // $E924: XOR $01; LD ($EE73),A; RET Z — only proceed every 2 frames.
        OddFrameToggle ^= 0x01;
        if (OddFrameToggle == 0) return;
        // $E92D: INC; AND $03; LD ($E48B),A
        Cycle = (Cycle + 1) & 0x03;

        for (int i = 0; i < SlotCount; i++)
        {
            ref var s = ref Slots[i];
            if ((s.Status & 0x80) == 0)
            {
                // Dead slot.  On the cassette a level's 7 ships come
                // only from the $E48D init block and NEVER respawn
                // (the $EADE re-init path is gated to level ≥ 6 at
                // $E97F — unreachable).  MODERN ONLY: laser-killed
                // ships come back after a countdown so endless pages
                // stay populated.
                if (!modernRespawn) continue;
                if (s.Sub > 0) { s.Sub--; }
                else if (level >= 1 && rng.Next(0, 256) < Math.Min(level, 16) * 8)
                {
                    s.X = (byte)((scrollCursor + rng.Next(32, 224)) & 0xFF);
                    s.Y = (byte)rng.Next(0x10, 0x70);
                    s.Status = 0x80;
                    s.Sub = (byte)(0x40 | (rng.Next(0, 4) << 5));
                }
                continue;
            }

            // $EAB2 range gate: ships outside the 32-byte scroll window
            // are NOT drawn (so their playfield sprite is hidden) but
            // their MOVEMENT keeps ticking so the mini-map dot moves
            // continuously.  (Earlier port skipped tick for off-screen
            // ships → dots stalled.)
            int offset = (s.X - scrollCursor) & 0xFF;
            bool inWindow = offset < 0x20;

            // $EB00 animation step: bit 5 of the Sub byte = Y direction.
            // Counter bounces between $04 and $70.
            int dy = (s.Sub & 0x20) != 0 ? +1 : -1;
            int newY = s.Y + dy;
            if (newY >= 0x70) { newY = 0x70; s.Sub &= 0xDF; }       // hit top → flip down
            else if (newY <= 0x04) { newY = 0x04; s.Sub |= 0x20; }  // hit bottom → flip up
            // Scenery probe on Y movement too — flip direction on wall.
            if (levelTiles != null && levelTiles.Length >= 4096)
            {
                int row = (newY >> 3) & 0x0F;
                byte tile = levelTiles[row * 256 + s.X];
                if (tile != 0)
                {
                    s.Sub ^= 0x20;        // reverse Y direction, don't move
                }
                else
                {
                    s.Y = (byte)newY;
                }
            }
            else
            {
                s.Y = (byte)newY;
            }

            // Bit 6 of Sub = X direction.  Move 1 byte per cycle.
            int dx = (s.Sub & 0x40) != 0 ? +1 : -1;
            int newX = (s.X + dx) & 0xFF;
            // Port of $EB5B → $EB62: probe scenery at the ship's NEW
            // (X, Y).  If tile is non-zero (= wall), reverse X-dir
            // via $EB47 (toggle bit 6 of Sub) and don't move this
            // frame.
            if (levelTiles != null && levelTiles.Length >= 4096)
            {
                int row = (s.Y >> 3) & 0x0F;
                byte tile = levelTiles[row * 256 + newX];
                if (tile != 0)
                {
                    s.Sub ^= 0x40;        // reverse X direction
                }
                else
                {
                    s.X = (byte)newX;
                }
            }
            else
            {
                s.X = (byte)newX;
            }

            // Fire-bullet + collision only apply when ship is in the
            // visible window (= player can actually see/interact).
            if (!inWindow) continue;

            // $EB99 fire-bullet gate: random gated by level.
            if (rng.Next(0, 16) < level)
            {
                bullets.TrySpawnAt(s.X, s.Y, playerByteX, playerY);
            }

            // Port of $DD8C (stride-4 ship test, see docs/disasm/collision.md):
            //   X: entity_X == p  OR  entity_X+1 == p   (= entity in {p, p-1})
            //   Y: |entity_Y - playerY| < 8
            // where p = playerByteX = ($E583)+$0F.
            int sdx = (playerByteX - s.X) & 0xFF;
            if ((sdx == 0 || sdx == 1) && Math.Abs(s.Y - playerY) < 8)
            {
                LastTickHits |= (1 << i);
            }
        }
    }

    /// <summary>Port of <c>$E9AC</c>'s sprite blit (simplified): draw
    /// the current cycle's 8x8 alien sprite at each alive ship's
    /// (X, Y).  Same gate as <see cref="DrawMiniMapDots"/>: only ships
    /// whose (X - scrollCursor) lands in the visible 32-byte window.</summary>
    public void DrawShipSprites(Framebuffer fb, int scrollCursor, byte levelAttr)
    {
        int spriteBase = Cycle * 8;
        if (spriteBase + 8 > SpriteBanks.Length) return;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!IsAlive(i)) continue;
            int offset = (Slots[i].X - scrollCursor) & 0xFF;
            if (offset >= 0x20) continue;
            int sx = offset * 8;
            int sy = Slots[i].Y;
            if ((uint)sx >= Framebuffer.Width || (uint)sy >= Framebuffer.Height) continue;
            // Sprite-byte plot, 8 scanlines.  XOR matches the cassette's
            // self-erase pattern at $E9D0 (though that has guards we skip).
            for (int row = 0; row < 8; row++)
            {
                int yy = sy + row;
                if ((uint)yy >= Framebuffer.Height) break;
                int addr = Framebuffer.BitmapAddress(sx, yy);
                fb.Bitmap[addr] ^= SpriteBanks[spriteBase + row];
            }
            // Attribute cell — single 8×8 paint with the level colour
            // (matches $E9BE LD A,($E57B); LD (HL),A).
            fb.Attributes[Framebuffer.AttributeAddress(sx, sy)] = levelAttr;
        }
    }
}

/// <summary>
/// Boss / special entity at <c>$EE7D..$EE91</c> (single slot).
/// Spawned by <c>$EC10</c> when scroll-progress (<c>$EE74</c>)
/// exceeds <c>$4A38</c> with a ~53% random gate, then ticked via
/// <c>$EC4C</c> — full port of the $EC82..$ECE6 movement body
/// including the mod-12 cycle counter, the $EE84 "speed table"
/// (which is in fact NEVER INITIALIZED — see Draw), and the
/// $EE7F/$EE80 direction-persistence pair.
/// </summary>
public sealed class BossEntity
{
    public byte X;                 // $EE7D — world byte
    public byte Y;                 // $EE7E — pixel Y

    public bool Active;            // $EE7C: 0 = not spawned, 1 = active
    public byte KillCount;         // $EE83 — SPAWN counter (INC'd at $EC29 on activation)
    public byte DirPersistence = 1;// $EE7F — resists rapid direction flips
    public sbyte LastDir = 1;      // $EE80 — cached X-direction sign
    public byte Cycle = 2;         // $EE81 — mod-12 movement cycle (init value from dumps)
    private byte _altGate;         // $EE82 — alternate-frame toggle

    public const int SpawnThreshold = 0x4A38;  // $EC1A

    /// <summary>The $EE84 "per-cycle speed table" — verified NEVER
    /// written by any instruction in the binary (the only reference
    /// is the read at $EC96).  Its content is leftover loader bytes,
    /// identical in the pre-game snapshot and every gameplay dump:
    /// B7 ED DB.  The byte feeds ONLY the draw mirror ($EC9A), so
    /// the boss's shifting bands are literally uninitialized memory.</summary>
    private static readonly byte[] SpeedTable = { 0xB7, 0xED, 0xDB };

    /// <summary>Per-level reset — $EE7D/$EE7E initial values observed
    /// in every dump ($20, $10); KillCount ($EE83) is NOT cleared per
    /// level (only the title loop's $F616 zeroes it).</summary>
    public void ResetForLevel()
    {
        X = 0x20; Y = 0x10;
        DirPersistence = 1; LastDir = 1; Cycle = 2; _altGate = 0;
        Active = false;
    }

    /// <summary>Full reset — new game (title $F616 clears $EE83).</summary>
    public void Reset()
    {
        ResetForLevel();
        KillCount = 0;
    }

    /// <summary>$E29B (respawn chain) — deactivate without the $EC6C
    /// randomize.</summary>
    public void Deactivate() => Active = false;

    /// <summary>Port of <c>$EC10</c>: spawn check + tick dispatcher.</summary>
    public void Tick(int scrollProgress, int scrollCursor, int playerByteX, int playerY, Random rng)
    {
        if (!Active)
        {
            // $EC1A: HL=$4A38; SBC HL,DE; RET NC — requires progress
            // STRICTLY greater than $4A38.
            if (scrollProgress <= SpawnThreshold) return;
            // $EC21: LD A,R; CP $78; RET C — spawn only when R ≥ $78
            // (≈ 53% of frames).
            if (rng.Next(0, 256) < 0x78) return;
            // $EC26 queues the $F8F9 boss-alert message (vestigial).
            // $EC29: $EE83++ (spawn count); $EE7C = 1.  NO coordinates
            // are written — the boss keeps its previous X/Y (initial
            // $20/$10, or wherever $EC6C randomized it after a kill).
            KillCount++;
            Active = true;
            // $EC32 falls through to the tick the same frame.
        }
        // $EC32..$EC41: until 10 spawns ($EE83 ≥ $0A) the boss ticks
        // every other frame; after that the throttle drops.
        if (KillCount < 0x0A)
        {
            _altGate ^= 1;
            if (_altGate == 0) return;
        }
        TickBody(scrollCursor, playerY);
        // $EC45: LD A,R; CP $16 — ~8.6% chance to double-tick.
        if (rng.Next(0, 256) < 0x16) TickBody(scrollCursor, playerY);
    }

    /// <summary>Port of <c>$EC6C</c> — laser-kill reset: randomize Y
    /// from the R register (7-bit, 0..127) and X from the chained RNG
    /// state, deactivate.  The boss respawns when the $EC10 gates pass
    /// again.</summary>
    public void Kill(Random rng)
    {
        Y = (byte)rng.Next(0, 0x80);       // LD A,R → 7-bit R register
        X = (byte)rng.Next(0, 256);        // R + ($EE7A) chained state
        Active = false;
    }

    /// <summary>Port of the <c>$EC81..$ECE6</c> movement body.</summary>
    private void TickBody(int scrollCursor, int playerY)
    {
        // $EC82: DEC the 1..12 cycle counter, reload 12 at zero.
        Cycle = (byte)(Cycle - 1);
        if (Cycle == 0) Cycle = 0x0C;
        // $EC8D..$EC99: speed byte = $EE84[(cycle-1)/4] → mirrored to
        // the draw slot ($EE8F/$EE90).  Movement does NOT use it.
        _drawSpeedByte = SpeedTable[(Cycle - 1) >> 2];

        // $ECA0..$ECCE: X chase toward ($E583)+$10 with persistence.
        int chase = (scrollCursor + 0x10) & 0xFF;
        if (chase != X)
        {
            sbyte newDir = (sbyte)(((chase - X) & 0xFF) < 0x80 ? +1 : -1);
            if (newDir != LastDir)
            {
                // $ECBD: DEC persistence; adopt the new direction only
                // when it hits zero.  No X move on this path.
                if (--DirPersistence == 0) LastDir = newDir;
            }
            else
            {
                // $ECC6: same direction — persistence++ (AND $3F),
                // then X += cached sign ($EE80).
                DirPersistence = (byte)((DirPersistence + 1) & 0x3F);
                X = (byte)((X + LastDir) & 0xFF);
            }
        }
        else
        {
            // $ECD7: same column — step Y one pixel toward the player.
            if (playerY > Y) Y++;
            else if (playerY < Y) Y--;
        }
    }

    private byte _drawSpeedByte = 0xB7;

    /// <summary>Port of <c>$EC4C</c>'s draw.  The boss has NO sprite
    /// bank: the blit source (alt-DE at <c>$EC50</c>) is <c>$EE8E</c>,
    /// the boss's own state-mirror block.  Dump-verified content:
    /// $EE8E=$7E, $EE8F/$EE90 = the current "speed" byte (written by
    /// $EC9A), $EE91=$7E, and everything after is zero.  So the boss
    /// renders as a 4-scanline band creature — [$7E, spd, spd, $7E] —
    /// whose middle bands cycle B7/ED/DB with the movement phase.
    /// Attribute = level colour ($E9BE), like every ship blit.</summary>
    public void Draw(Framebuffer fb, int scrollCursor, byte levelAttr)
    {
        if (!Active) return;
        int offset = (X - scrollCursor) & 0xFF;
        if (offset >= 0x20) return;
        int sx = offset * 8;
        int sy = Y;
        if ((uint)sx >= Framebuffer.Width || (uint)sy >= Framebuffer.Height) return;

        Span<byte> state = stackalloc byte[4] { 0x7E, _drawSpeedByte, _drawSpeedByte, 0x7E };
        for (int row = 0; row < 4; row++)
        {
            int yy = sy + row;
            if ((uint)yy >= Framebuffer.Height) break;
            fb.Bitmap[Framebuffer.BitmapAddress(sx, yy)] ^= state[row];
        }
        fb.Attributes[Framebuffer.AttributeAddress(sx, sy)] = levelAttr;
    }
}
