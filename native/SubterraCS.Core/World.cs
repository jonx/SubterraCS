namespace SubterraCS.Core;

/// <summary>
/// The whole game state — what the original kept in scattered RAM
/// locations ($E45F input, $E583 lock, $E584 altitude, $E587 level,
/// $E75D active schedule, $E881 bullet table, $E8A9 player buffer) all
/// gathered into one place.
///
/// The game loop calls <see cref="Tick"/> 50 times a second; the
/// renderer calls <see cref="Draw"/> from the same thread immediately
/// after.
/// </summary>
public sealed class World
{
    public const int MaxEntities = 16;
    public const int MaxBullets  = 8;

    // Loaded once at boot ------------------------------------------------
    public TileBank Tiles { get; }
    public UdgBank Udgs { get; }
    public EntityBank EntityBank { get; }
    public EntityTypeTable EntityTypes { get; }
    public byte[] PlayerSpriteRight { get; }   // 16 bytes
    public byte[] PlayerSpriteLeft  { get; }   // 16 bytes

    // Procedurally generated as the player descends --------------------
    private readonly ProceduralGenerator _gen;
    public int Depth { get; private set; }      // pages dived (= "level" in original)
    public SpawnSchedule Current { get; private set; }

    // Player ----------------------------------------------------------------
    public int PlayerX = 120;
    public int PlayerY = 96;
    public int Altitude;        // 0..120, $75-gate semantics from original
    public bool FacingLeft;
    public int Score;
    public int Fuel = 100;
    public int Shield = 100;
    public int Rescued;
    public bool Dead;

    // Active world ---------------------------------------------------------
    public readonly EntityInstance[] Entities = new EntityInstance[MaxEntities];
    public readonly Bullet[] Bullets = new Bullet[MaxBullets];

    private readonly Random _rng;
    private int _frameCounter;
    public int Spawned { get; private set; }
    public int Alive => Entities.Count(e => e.Alive);

    public World(
        TileBank tiles, UdgBank udgs, EntityBank entityBank,
        EntityTypeTable entityTypes,
        byte[] playerRight, byte[] playerLeft,
        int seed = 42)
    {
        Tiles = tiles;
        Udgs = udgs;
        EntityBank = entityBank;
        EntityTypes = entityTypes;
        PlayerSpriteRight = playerRight;
        PlayerSpriteLeft = playerLeft;
        _gen = new ProceduralGenerator(seed);
        _rng = new Random(seed);
        Current = _gen.Page(0);

        for (int i = 0; i < Entities.Length; i++) Entities[i] = new EntityInstance();
        for (int i = 0; i < Bullets.Length; i++)  Bullets[i] = new Bullet();
    }

    public void Tick(GameInput input)
    {
        if (Dead) return;
        _frameCounter++;

        // --- Vertical movement (mirrors $D95D logic) --------------------
        if (input.Down)
        {
            Altitude = Math.Min(120, Altitude + 1);
            PlayerY = Math.Min(160, PlayerY + 1);
            Fuel = Math.Max(0, Fuel - 1);
        }
        else if (input.Up)
        {
            Altitude = Math.Max(0, Altitude - 1);
            PlayerY = Math.Max(24, PlayerY - 1);
        }
        if (input.Horizontal)
        {
            // The original L key has a single horizontal bit — we toggle
            // facing each press here for visual effect.
            PlayerX += FacingLeft ? -2 : 2;
            if (PlayerX < 8 || PlayerX > 232)
            {
                FacingLeft = !FacingLeft;
                PlayerX = Math.Clamp(PlayerX, 8, 232);
            }
        }

        // --- Page advance ($F6F2) ---------------------------------------
        if (Altitude >= 0x75)
        {
            Altitude = 0;
            Depth++;
            Current = _gen.Page(Depth);
            Score += 100;
            Fuel = Math.Min(100, Fuel + 20);  // diving reward
        }

        // --- Spawn-schedule executor ($EF02) ----------------------------
        for (int i = 0; i < SpawnSchedule.Slots; i++)
        {
            var entry = Current.Entries[i];
            // Decrement; when it underflows, spawn.
            ushort newTimer = (ushort)(entry.Timer - 1);
            if (newTimer > entry.Timer)  // wrapped past 0
            {
                Spawn(entry.TypeId, entry.Flags);
                // Reset timer to a fresh procedural value so the page
                // keeps streaming entities (short — measured in frames,
                // not the original's much-slower 6502-ish budget).
                newTimer = (ushort)(0x0020 + _rng.Next(0, 0x0040));
            }
            Current.Entries[i] = new ScheduleEntry(newTimer, entry.TypeId, entry.Flags);
        }

        // --- Update entities --------------------------------------------
        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            e.X += e.DX;
            e.Y += e.DY;
            e.FrameTick++;
            if (e.FrameTick >= 4)
            {
                e.FrameTick = 0;
                int max = EntityTypes.Types[e.TypeId].MaxFrames;
                if (max > 0) e.Frame = (e.Frame + 1) % max;
            }
            // Off-screen?
            if (e.Y < -16 || e.Y > 200 || e.X < -16 || e.X > 272)
            {
                e.Alive = false;
                continue;
            }
            // Collision with player (cheap AABB).
            if (e.X > PlayerX - 14 && e.X < PlayerX + 14 &&
                e.Y > PlayerY - 14 && e.Y < PlayerY + 14)
            {
                Shield -= 5;
                e.Alive = false;
                if (Shield <= 0) Dead = true;
            }
        }

        // --- Bullets ----------------------------------------------------
        if (input.Fire && _frameCounter % 6 == 0)
        {
            FireBullet();
        }
        foreach (var b in Bullets)
        {
            if (!b.Alive) continue;
            b.X += b.DX;
            b.Y += b.DY;
            if ((uint)b.Y >= 192 || (uint)b.X >= 256)
            {
                b.Alive = false;
                continue;
            }
            // Bullet-vs-entity collision.
            foreach (var e in Entities)
            {
                if (!e.Alive) continue;
                if (Math.Abs(e.X - b.X) < 10 && Math.Abs(e.Y - b.Y) < 10)
                {
                    e.Alive = false;
                    b.Alive = false;
                    Score += 25;
                    break;
                }
            }
        }
    }

    private void Spawn(int typeId, byte flags)
    {
        if (typeId >= EntityTypes.Types.Length) return;
        var slot = NextFreeEntity();
        if (slot == null) return;
        slot.TypeId = typeId;
        slot.X = _rng.Next(16, 240);
        slot.Y = -16;
        slot.Frame = 0;
        slot.FrameTick = 0;
        slot.DX = (flags & 0x40) != 0 ? (_rng.Next(0, 2) == 0 ? -1 : 1) : 0;
        slot.DY = 1 + _rng.Next(0, 2);   // 1 or 2 px/frame downward
        slot.Alive = true;
        Spawned++;
    }

    private EntityInstance? NextFreeEntity()
    {
        foreach (var e in Entities) if (!e.Alive) return e;
        return null;
    }

    private void FireBullet()
    {
        foreach (var b in Bullets)
        {
            if (b.Alive) continue;
            b.X = PlayerX + (FacingLeft ? -8 : 8);
            b.Y = PlayerY;
            b.DX = FacingLeft ? -4 : 4;
            b.DY = 0;
            b.Pattern = 0x18;            // 2-pixel-wide dot
            b.Alive = true;
            return;
        }
    }

    public void Draw(Framebuffer fb)
    {
        fb.Clear();
        // Default attributes: bright white on black.
        fb.FillAttributes(0x47);

        // Side / ceiling decoration: line of UDG tiles every 8 px column
        // along the top and bottom, scrolling slowly based on Depth +
        // frame counter so the world feels alive even before entities
        // arrive.
        DrawCaveDecor(fb);

        // Entities first, so the player draws on top of them.
        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            var type = EntityTypes.Types[e.TypeId];
            var sprite = EntityBank.Frame(type.SpritePointer, e.Frame);
            if (sprite.IsEmpty) continue;
            Blitters.DrawSprite16x16(fb, e.X - 8, e.Y - 8, sprite, type.Attribute);
        }

        // Bullets.
        foreach (var b in Bullets)
        {
            if (!b.Alive) continue;
            Blitters.DrawBulletXor(fb, b.X & ~7, b.Y, b.Pattern, 0x46);  // yellow
        }

        // Player.  Bright magenta (attr $43).
        var playerSprite = FacingLeft ? PlayerSpriteLeft : PlayerSpriteRight;
        Blitters.DrawPlayerXor(fb, PlayerX - 8, PlayerY - 4, playerSprite, 0x43);

        // HUD.
        Hud.Draw(fb, this);
    }

    private void DrawCaveDecor(Framebuffer fb)
    {
        // Row 0 (top of screen) — repeating UDG tiles, animated by
        // (depth + frame) so the cave roof drifts as we descend.
        int phase = (_frameCounter / 8 + Depth) % Udgs.Count;
        for (int col = 0; col < 32; col++)
        {
            int idx = (col + phase) % Udgs.Count;
            Blitters.DrawTile8x8(fb, col * 8, 0, Udgs[idx], 0x44);    // green
            // Bottom row too.
            Blitters.DrawTile8x8(fb, col * 8, 184, Udgs[idx], 0x44);
        }
    }
}
