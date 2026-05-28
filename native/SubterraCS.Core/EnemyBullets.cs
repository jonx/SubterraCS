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

    /// <summary>Port of <c>$EBB2</c>: spawn one enemy if the random
    /// gate fires and there's a free slot.  Returns the slot index
    /// or -1 if the spawn was dropped.
    ///
    /// Spawn coordinates: the original reads X/Y from a caller-
    /// supplied pointer; we generate them here at a world position
    /// ahead of the player (within the scroll window's right half)
    /// since the precise caller logic is TBD.  Direction is set to
    /// the sign-of-difference toward the player, matching
    /// $EBDE..$EBFB.</summary>
    public int TrySpawn(int level, int scrollCursor, int playerByteX, int playerY, Random rng)
    {
        // $EBAC: LD A,R; AND $0F; CP B(level); RET NC
        if (rng.Next(0, 16) >= level) return -1;

        int slot = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!IsAlive(i)) { slot = i; break; }
        }
        if (slot < 0) return -1;

        // Place the enemy ahead-of-player in world coords.  Player
        // world byte = scrollCursor + 15; enemy spawns 16..31 bytes
        // ahead so it's just off the right edge of the screen.
        int worldX = (scrollCursor + 15 + rng.Next(16, 32)) & 0xFF;
        int y = rng.Next(8, 100);

        ref var e = ref Slots[slot];
        e.X = (byte)worldX;
        e.Y = (byte)y;
        e.Dx = (sbyte)Sign(playerByteX - worldX);
        e.Dy = (sbyte)Sign(playerY - y);
        e.Status = 0x80;     // alive
        e.Lifetime = 0x40;   // matches $ED85 LD (IX+$05),$40

        return slot;
    }

    /// <summary>Port of <c>$ED01</c>: per-frame tick.  Moves each
    /// alive enemy, decrements its lifetime, expires on Y wrap or
    /// when the lifetime hits zero.  Returns the bitmask of slots
    /// that just collided with the player (used by the caller to
    /// fire the hit chain).</summary>
    public int Tick(int scrollCursor, int playerByteX, int playerY)
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

            // Player collision — port of $EDC0.  Player's world byte
            // = scrollCursor + 15..16 (the 2 bytes the ship sprite
            // covers); collide if enemy's world byte matches AND Y
            // is within the player sprite (16 px tall).
            if ((e.X == playerByteX || e.X == playerByteX + 1)
                && Math.Abs(e.Y - playerY) < 16)
            {
                hitMask |= (1 << i);
            }
        }
        return hitMask;
    }

    /// <summary>Port of <c>$ED95</c>: paint a single attribute byte
    /// at the resolved screen address.  We draw an 8×8 attribute
    /// flash + a small dot in the bitmap so the enemy is visible
    /// against the cave scenery.</summary>
    public void Draw(Framebuffer fb, int scrollCursor)
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

            // Single-byte bitmap dot — matches $ED95's single write.
            fb.Bitmap[Framebuffer.BitmapAddress(screenX, screenY)] ^= 0x3C;
            // Bright white attribute (the $07 from $ED71 LD D,$07).
            fb.Attributes[Framebuffer.AttributeAddress(screenX, screenY)] = 0x47;
        }
    }

    private static int Sign(int v) => v == 0 ? 0 : (v < 0 ? -1 : 1);
}
