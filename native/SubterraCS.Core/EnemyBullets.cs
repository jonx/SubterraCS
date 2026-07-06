namespace SubterraCS.Core;

/// <summary>
/// Port of the enemy-ship subsystem at <c>$EE9E</c> + <c>$EBB2</c>
/// + <c>$ED01</c> + <c>$EDC0</c> (see <c>docs/disasm/enemies.md</c>).
///
/// 6-slot live table of small chasing dots that fly toward the
/// player.  Spawned at random rate (gated by current level), each
/// born with DX/DY = sign(player − enemy) so the spawn aims at the
/// player's current position.  Drawn as single-byte attribute
/// flashes — the same technique as the death-particle effect.
/// On contact with the player, fires the hit-sound + shield-drain
/// chain just like the cassette's <c>$EDC0 → $DD4A</c> path.
/// </summary>
public sealed class EnemyBullets
{
    public const int SlotCount = 6;

    public struct Enemy
    {
        public byte X;        // world-byte position 0..255
        public byte Y;        // pixel Y, 0..127 playfield
        public sbyte Dx;      // -1, 0, +1 — toward player at spawn
        public sbyte Dy;
        public byte Status;   // bit 7 = alive, bit 5 = blink toggle
        public byte Lifetime; // tick counter (DEC per frame; expire at 0)
    }

    public readonly Enemy[] Slots = new Enemy[SlotCount];

    public bool IsAlive(int i) => (Slots[i].Status & 0x80) != 0;

    public void Reset()
    {
        for (int i = 0; i < SlotCount; i++) Slots[i] = default;
    }

    /// <summary>Port of <c>$EBB2</c>: spawn a bullet at (sourceX, sourceY)
    /// (= a ship's position).  Direction = sign-toward-player from
    /// $EBDE..$EBFB.  Returns the slot index or -1 if no free slot.</summary>
    public int TrySpawnAt(int sourceX, int sourceY, int playerByteX, int playerY)
    {
        int slot = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!IsAlive(i)) { slot = i; break; }
        }
        if (slot < 0) return -1;

        ref var e = ref Slots[slot];
        e.X = (byte)(sourceX & 0xFF);
        e.Y = (byte)(sourceY & 0xFF);
        e.Dx = (sbyte)Sign(playerByteX - sourceX);
        e.Dy = (sbyte)Sign(playerY - sourceY);
        e.Status = 0x80;
        e.Lifetime = 0x40;
        return slot;
    }

    /// <summary>Port of <c>$ED01</c>: per-frame tick.  Moves each
    /// alive enemy, decrements its lifetime, expires on Y wrap or
    /// when the lifetime hits zero.  Returns the bitmask of slots
    /// that just collided with the player (used by the caller to
    /// fire the hit chain).</summary>
    public int Tick(int scrollCursor, int playerByteX, int playerY, byte[]? levelTiles = null)
    {
        int hitMask = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!IsAlive(i)) continue;
            ref var e = ref Slots[i];

            // Toggle blink (bit 5 of status) — used by $ED95 to
            // alternate attribute colour, but we always draw bright
            // white so it's just a state bit for now.
            e.Status ^= 0x20;

            // Lifetime --; expire on 0.
            if (--e.Lifetime == 0) { e.Status = 0; continue; }

            // Move toward where the player was at spawn time.
            int newX = e.X + e.Dx;
            int newY = e.Y + e.Dy;
            if (newY < 0 || newY > 127) { e.Status = 0; continue; }
            e.X = (byte)(newX & 0xFF);
            e.Y = (byte)newY;

            // Out-of-window cull: $ED8A checks (X - $E583) < $20.
            int offset = (e.X - scrollCursor) & 0xFF;
            if (offset >= 0x20) { e.Status = 0; continue; }

            // Port of $EB62 scenery probe (called from $ED01 at $ED62):
            // if the bullet entered a wall tile, expire.
            if (levelTiles != null && levelTiles.Length >= 4096)
            {
                int row = (e.Y >> 3) & 0x0F;
                byte tile = levelTiles[row * 256 + e.X];
                if (tile != 0) { e.Status = 0; continue; }
            }

            // Port of $DDAA (stride-6 bullet test, collision.md):
            //   $DDAA LD A,(HL) = p; CP (IX+0); INC A; CP (IX+0)
            //   → hit when entity_X ∈ {p, p+1}  (note: OPPOSITE side
            //   from the $DD8C ship window {p, p-1})
            //   Y: 0 <= entity_Y - playerY < 8 (only at-or-below player;
            //   the cassette's `RET M` rejects entity-above-player).
            int bdx = (e.X - playerByteX) & 0xFF;
            int ydiff = e.Y - playerY;
            if ((bdx == 0 || bdx == 1) && ydiff >= 0 && ydiff < 8)
            {
                hitMask |= (1 << i);
            }
        }
        return hitMask;
    }

    /// <summary>Port of <c>$ED95</c>: bullets are ATTRIBUTE-ONLY
    /// flashes — a single attribute write per bullet ($ED71 LD D,$07,
    /// white), no bitmap pixels.  In empty sky that makes them nearly
    /// invisible (only cells containing scenery/sprite pixels flash),
    /// exactly like the cassette.  MODERN ONLY: also XOR a small
    /// bitmap dot for visibility.</summary>
    public void Draw(Framebuffer fb, int scrollCursor, bool modernPixels = false)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (!IsAlive(i)) continue;
            ref var e = ref Slots[i];

            int offset = (e.X - scrollCursor) & 0xFF;
            if (offset >= 0x20) continue;

            int screenX = offset * 8;
            int screenY = e.Y;
            if ((uint)screenX >= 256 || (uint)screenY >= 192) continue;

            if (modernPixels)
                fb.Bitmap[Framebuffer.BitmapAddress(screenX, screenY)] ^= 0x3C;
            // White attribute — the documented $07, without the
            // invented BRIGHT bit.
            fb.Attributes[Framebuffer.AttributeAddress(screenX, screenY)] = 0x07;
        }
    }

    private static int Sign(int v) => v == 0 ? 0 : (v < 0 ? -1 : 1);
}
