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
            // OR the pixel (not XOR) so it stays visible even if the
            // mini-map underneath already had this bit set.
            int addr = Framebuffer.BitmapAddress(pixelX, pixelY);
            byte bit = (byte)(0x80 >> (pixelX & 7));
            fb.Bitmap[addr] |= bit;
            // Bright RED attribute on this cell so the dot stands out
            // against the green mini-map background.  (The cassette
            // uses the bottom-strip's existing attribute, which makes
            // the dot same-colour-as-background — not great visibility
            // in our port either, so we override.)
            fb.Attributes[Framebuffer.AttributeAddress(pixelX, pixelY)] = 0x42;  // bright red
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
                        byte[]? levelTiles = null)
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
                // Dead slot — port of $EADE respawn behaviour.
                // The cassette respawns when the spawn counter at
                // $E8F9/$E8FA reaches threshold; we simulate with
                // a per-slot countdown via Sub (Sub > 0 = waiting).
                if (s.Sub > 0) { s.Sub--; }
                else if (level >= 1 && rng.Next(0, 256) < (level * 8))
                {
                    // Respawn at a random world X (off-screen so the
                    // ship sails in) with random Y in playfield range.
                    s.X = (byte)((scrollCursor + rng.Next(32, 224)) & 0xFF);
                    s.Y = (byte)rng.Next(0x10, 0x70);
                    s.Status = 0x80;
                    s.Sub = (byte)(0x40 | (rng.Next(0, 4) << 5));  // bit 6/5 → init direction
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

    /// <summary>Port of <c>$EC10</c>: spawn check + per-frame
    /// processing.  Triggers boss spawn after enough scroll progress
    /// + random gate; once active, ticks via <see cref="TickActive"/>.
    /// </summary>
    public void Tick(int scrollProgress, int scrollCursor, int playerByteX, int playerY, Random rng)
    {
        if (!Active)
        {
            // $EC1A: HL = $4A38; SBC HL,DE; RET NC if not yet far enough
            if (scrollProgress < SpawnThreshold) return;
            // $EC21: LD A,R; CP $78; RET C — ~50% random gate
            if (rng.Next(0, 256) < 0x78) return;
            // $EC26: CALL $F8F9 = print "boss alert" message — skipped.
            // $EC29: $EE83++; $EE7C = 1
            KillCount++;
            Active = true;
            // Place the boss off the right edge of the screen so it
            // enters the visible window as the player keeps scrolling.
            X = (byte)((scrollCursor + 0x20) & 0xFF);
            Y = 0x40;
            Frame = 0;
            Reserved5 = 0; Reserved6 = 0;
            Sub = 0;
            return;
        }
        TickActive(scrollCursor, playerByteX, playerY, rng);
    }

    /// <summary>Port of <c>$EC4C</c>: boss movement + draw.  The
    /// cassette uses a cycle-rotated speed table at <c>$EE84</c>
    /// and a direction-chase algorithm based on
    /// <c>($E583)+16</c> vs boss.X (`$ECA5..$ECCE`).  Our port
    /// approximates by moving the boss 1 byte/frame toward the
    /// player and tracking lifetime via <see cref="LifetimeCheck"/>.</summary>
    public void TickActive(int scrollCursor, int playerByteX, int playerY, Random rng)
    {
        // $EC82: cycle EE81 in 1..12 (mod-12 counter)
        AltFrame = (byte)((AltFrame + 1) % 12);

        // $ECA5: A = ($E583) + $10; CP boss.X
        int chase = (scrollCursor + 0x10) & 0xFF;
        if (chase != X)
        {
            // $ECAD: chase via $EBFF/$EC06 (sign helpers — return ±1)
            int dxSign = ((chase - X) & 0xFF) < 0x80 ? +1 : -1;
            X = (byte)((X + dxSign) & 0xFF);
        }
        else
        {
            // $ECD7..: same column, track Y
            int dySign = playerY > Y ? +1 : (playerY < Y ? -1 : 0);
            Y = (byte)Math.Clamp(Y + dySign, 0, 120);
        }

        // Out-of-window cull (== EAB2)
        int offset = (X - scrollCursor) & 0xFF;
        if (offset >= 0x40)
        {
            // Boss strayed too far — keep active but won't draw this frame.
        }
    }

    /// <summary>Port of <c>$EC4C</c>'s draw section: blit the alien-ship
    /// sprite (same as regular ships per <c>$E5DB</c>, since the
    /// boss tick's <c>$E9AC</c> call inherits DE from the surrounding
    /// $E920 chain).  Twice as wide as a regular ship — 16×8 instead
    /// of 8×8 — since $EC60 calls $E9AC TWICE (left + right cells).
    /// Bright RED attribute so it stands out as the boss.</summary>
    public void Draw(Framebuffer fb, int scrollCursor, byte[] shipSpriteBanks, int cycle)
    {
        if (!Active) return;
        int offset = (X - scrollCursor) & 0xFF;
        if (offset >= 0x20) return;
        int sx = offset * 8;
        int sy = Y;
        if ((uint)sx >= Framebuffer.Width || (uint)sy >= Framebuffer.Height) return;
        int spriteBase = cycle * 8;
        if (spriteBase + 8 > shipSpriteBanks.Length) return;
        // Two 8x8 cells side-by-side (=16x8 boss).
        for (int cell = 0; cell < 2; cell++)
        {
            int cellX = sx + cell * 8;
            if (cellX >= Framebuffer.Width) break;
            for (int row = 0; row < 8; row++)
            {
                int yy = sy + row;
                if ((uint)yy >= Framebuffer.Height) break;
                fb.Bitmap[Framebuffer.BitmapAddress(cellX, yy)] ^= shipSpriteBanks[spriteBase + row];
            }
            fb.Attributes[Framebuffer.AttributeAddress(cellX, sy)] = 0x42;  // bright red = boss
        }
    }
}
