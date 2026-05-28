namespace SubterraCS.Core;

public enum GameState
{
    Title,      // Title screen — press FIRE to start
    Playing,    // The actual game loop
    Dying,      // Brief death animation
    GameOver,   // Game-over screen — press FIRE to retry
}

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
    public const int CaveTop = 16;     // pixel row where the cave roof ends
    public const int CaveBottom = 168; // first row of HUD

    // Loaded once at boot ------------------------------------------------
    public TileBank Tiles { get; }
    public UdgBank Udgs { get; }
    public EntityBank EntityBank { get; }
    public EntityTypeTable EntityTypes { get; }
    public byte[] PlayerSpriteRight { get; }   // 16 bytes
    public byte[] PlayerSpriteLeft  { get; }   // 16 bytes

    // Procedurally generated as the player descends --------------------
    private readonly ProceduralGenerator _gen;
    private readonly SpawnSchedule[]? _originalLevels;
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
    public int Lives = 3;
    public bool Invincible;     // a few frames after respawn
    public int InvincibleTicks;

    // Game flow ------------------------------------------------------------
    public GameState State { get; private set; } = GameState.Title;
    public int StateTicks { get; private set; }
    public readonly SfxQueue Sfx = new();

    // Active world ---------------------------------------------------------
    public readonly EntityInstance[] Entities = new EntityInstance[MaxEntities];
    public readonly Bullet[] Bullets = new Bullet[MaxBullets];

    private readonly int _seed;
    private readonly Random _rng;
    private int _frameCounter;
    private int _fireCooldown;
    public int Spawned { get; private set; }
    public int Alive => Entities.Count(e => e.Alive);
    public int FrameCounter => _frameCounter;

    public World(
        TileBank tiles, UdgBank udgs, EntityBank entityBank,
        EntityTypeTable entityTypes,
        byte[] playerRight, byte[] playerLeft,
        SpawnSchedule[]? originalLevels = null,
        int seed = 42)
    {
        Tiles = tiles;
        Udgs = udgs;
        EntityBank = entityBank;
        EntityTypes = entityTypes;
        PlayerSpriteRight = playerRight;
        PlayerSpriteLeft = playerLeft;
        _seed = seed;
        _gen = new ProceduralGenerator(seed);
        _rng = new Random(seed);
        _originalLevels = originalLevels;
        Current = ScheduleForDepth(0);

        for (int i = 0; i < Entities.Length; i++) Entities[i] = new EntityInstance();
        for (int i = 0; i < Bullets.Length; i++)  Bullets[i] = new Bullet();
    }

    private SpawnSchedule ScheduleForDepth(int depth)
    {
        if (_originalLevels is { Length: > 0 } && depth < _originalLevels.Length)
        {
            // Clone so per-page timer-decay doesn't mutate the shared table.
            var src = _originalLevels[depth].Entries;
            var copy = new ScheduleEntry[src.Length];
            Array.Copy(src, copy, src.Length);
            return new SpawnSchedule(copy);
        }
        return _gen.Page(depth);
    }

    public void Tick(GameInput input)
    {
        _frameCounter++;
        StateTicks++;

        switch (State)
        {
            case GameState.Title:
                TickTitle(input);
                return;
            case GameState.GameOver:
                TickGameOver(input);
                return;
            case GameState.Dying:
                TickDying();
                return;
        }

        TickPlaying(input);
    }

    private void TickTitle(GameInput input)
    {
        if (input.Fire && StateTicks > 10)
        {
            StartNewGame();
        }
    }

    private void TickGameOver(GameInput input)
    {
        if (input.Fire && StateTicks > 25)
        {
            StartNewGame();
        }
    }

    private void TickDying()
    {
        // Brief explosion animation before respawn / game-over.
        if (StateTicks >= 40)
        {
            if (Lives <= 0)
            {
                EnterState(GameState.GameOver);
                Sfx.Trigger(SfxKind.GameOver);
            }
            else
            {
                Respawn();
            }
        }
    }

    private void TickPlaying(GameInput input)
    {
        // --- Vertical movement (mirrors $D95D logic) --------------------
        if (input.Down)
        {
            Altitude = Math.Min(120, Altitude + 1);
            PlayerY = Math.Min(CaveBottom - 8, PlayerY + 1);
            Fuel = Math.Max(0, Fuel - 1);
        }
        else if (input.Up)
        {
            Altitude = Math.Max(0, Altitude - 1);
            PlayerY = Math.Max(CaveTop + 8, PlayerY - 1);
        }
        if (input.Horizontal)
        {
            PlayerX += FacingLeft ? -2 : 2;
            if (PlayerX < 16 || PlayerX > 232)
            {
                FacingLeft = !FacingLeft;
                PlayerX = Math.Clamp(PlayerX, 16, 232);
            }
        }

        // --- Page advance ($F6F2) ---------------------------------------
        if (Altitude >= 0x75)
        {
            Altitude = 0;
            Depth++;
            Current = ScheduleForDepth(Depth);
            Score += 100;
            Fuel = Math.Min(100, Fuel + 20);  // diving reward
            Sfx.Trigger(SfxKind.LevelUp);
        }

        // --- Spawn-schedule executor ($EF02) ----------------------------
        for (int i = 0; i < SpawnSchedule.Slots; i++)
        {
            var entry = Current.Entries[i];
            ushort newTimer = (ushort)(entry.Timer - 1);
            if (newTimer > entry.Timer)  // wrapped past 0
            {
                Spawn(entry.TypeId, entry.Flags);
                newTimer = (ushort)(0x0020 + _rng.Next(0, 0x0040));
            }
            Current.Entries[i] = new ScheduleEntry(newTimer, entry.TypeId, entry.Flags);
        }

        // --- Update entities --------------------------------------------
        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            var kind = EntityAI.For(e.TypeId);
            if (!EntityAI.Tick(e, kind, PlayerX, PlayerY, _rng))
            {
                e.Alive = false;
                continue;
            }
            // Collision with player.
            if (!Invincible && AabbHit(e.X, e.Y, PlayerX, PlayerY, 12))
            {
                var rule = EntityAI.Collision(kind);
                Shield = Math.Clamp(Shield + rule.ShieldDelta, 0, 100);
                Fuel = Math.Clamp(Fuel + rule.FuelDelta, 0, 100);
                Score += rule.ScoreOnContact;
                Rescued += rule.RescuedDelta;
                if (rule.ConsumedOnContact) e.Alive = false;
                if (rule.ShieldDelta < 0)
                {
                    Sfx.Trigger(SfxKind.Damage);
                    SetInvincible(20);
                }
                else if (rule.ShieldDelta > 0 || rule.RescuedDelta > 0)
                {
                    Sfx.Trigger(SfxKind.Pickup);
                }
                if (Shield <= 0 || Fuel <= 0)
                {
                    TriggerDeath();
                    return;
                }
            }
        }

        // --- Cave wall collision (graze damage) -------------------------
        int caveHalf = CaveHalfWidthAt(PlayerY);
        int caveCenter = 128;
        if (PlayerX < caveCenter - caveHalf + 8 || PlayerX > caveCenter + caveHalf - 8)
        {
            if (!Invincible && (_frameCounter & 7) == 0)
            {
                Shield = Math.Max(0, Shield - 1);
                Sfx.Trigger(SfxKind.Hit);
                if (Shield <= 0) { TriggerDeath(); return; }
            }
        }

        // --- Bullets ----------------------------------------------------
        if (_fireCooldown > 0) _fireCooldown--;
        if (input.Fire && _fireCooldown == 0)
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
                var kind = EntityAI.For(e.TypeId);
                if (EntityAI.IsBulletProof(kind)) continue;
                if (Math.Abs(e.X - b.X) < 10 && Math.Abs(e.Y - b.Y) < 10)
                {
                    e.Hp--;
                    b.Alive = false;
                    if (e.Hp <= 0)
                    {
                        e.Alive = false;
                        Score += EntityAI.ShootScore(kind);
                        Sfx.Trigger(SfxKind.Explode);
                        SpawnExplosionAt(e.X, e.Y);
                    }
                    else
                    {
                        Sfx.Trigger(SfxKind.Hit);
                    }
                    break;
                }
            }
        }

        if (InvincibleTicks > 0 && --InvincibleTicks == 0)
        {
            Invincible = false;
        }
    }

    private void SetInvincible(int ticks)
    {
        Invincible = true;
        InvincibleTicks = Math.Max(InvincibleTicks, ticks);
    }

    private void TriggerDeath()
    {
        Lives--;
        EnterState(GameState.Dying);
        Sfx.Trigger(SfxKind.Explode);
        SpawnExplosionAt(PlayerX, PlayerY);
        SpawnExplosionAt(PlayerX + 8, PlayerY - 4);
        SpawnExplosionAt(PlayerX - 8, PlayerY + 4);
    }

    private void SpawnExplosionAt(int x, int y)
    {
        var slot = NextFreeEntity();
        if (slot == null) return;
        slot.TypeId = 8;        // explosion bank
        slot.X = x;
        slot.Y = y;
        slot.DX = 0;
        slot.DY = 0;
        slot.AgeFrames = 0;
        slot.Hp = 1;
        slot.MaxFrames = TypeMaxFrames(8);
        slot.Frame = 0;
        slot.FrameTick = 0;
        slot.Alive = true;
    }

    private void Respawn()
    {
        // Wipe entities so the player isn't sandwiched, recenter, restore shield.
        foreach (var e in Entities) e.Alive = false;
        foreach (var b in Bullets) b.Alive = false;
        PlayerX = 120; PlayerY = 96;
        Shield = 100; Fuel = Math.Max(50, Fuel);
        Altitude = 0;
        SetInvincible(100);
        EnterState(GameState.Playing);
    }

    private void StartNewGame()
    {
        Lives = 3;
        Score = 0;
        Rescued = 0;
        Depth = 0;
        Shield = 100;
        Fuel = 100;
        Altitude = 0;
        PlayerX = 120; PlayerY = 96;
        Current = ScheduleForDepth(0);
        foreach (var e in Entities) e.Alive = false;
        foreach (var b in Bullets) b.Alive = false;
        SetInvincible(60);
        EnterState(GameState.Playing);
    }

    private void EnterState(GameState s)
    {
        State = s;
        StateTicks = 0;
    }

    private static bool AabbHit(int ax, int ay, int bx, int by, int half)
        => Math.Abs(ax - bx) < half && Math.Abs(ay - by) < half;

    private int TypeMaxFrames(int typeId)
    {
        if (typeId >= 0 && typeId < EntityTypes.Types.Length)
            return EntityTypes.Types[typeId].MaxFrames;
        return 8;
    }

    private void Spawn(int typeId, byte flags)
    {
        if (typeId >= EntityTypes.Types.Length) return;
        var slot = NextFreeEntity();
        if (slot == null) return;
        slot.TypeId = typeId;
        slot.Alive = true;
        slot.Frame = 0;
        slot.FrameTick = 0;
        slot.MaxFrames = EntityTypes.Types[typeId].MaxFrames;
        EntityAI.InitSpawn(slot, EntityAI.For(typeId), _rng, flags, PlayerX, PlayerY);
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
            _fireCooldown = 8;
            Sfx.Trigger(SfxKind.Fire);
            return;
        }
    }

    /// <summary>
    /// Half-width of the cave at the given pixel row, in pixels.
    /// The cave is a slow sine that breathes as depth grows so each
    /// dive feels different from the last.
    /// </summary>
    public int CaveHalfWidthAt(int y)
    {
        // Min ~64 px (very tight), max ~110 px (open).  The sine period
        // shrinks with depth so deeper caves twist more.
        double phase = (y + _frameCounter * 0.3 + Depth * 64) * (0.05 + Depth * 0.001);
        double w = 96 + 16 * Math.Sin(phase);
        return (int)Math.Clamp(w, 64, 110);
    }

    public void Draw(Framebuffer fb)
    {
        fb.Clear();
        fb.FillAttributes(0x47);

        switch (State)
        {
            case GameState.Title:
                DrawTitleScreen(fb);
                break;
            case GameState.GameOver:
                DrawGameOver(fb);
                break;
            default:
                DrawWorld(fb);
                break;
        }
    }

    private void DrawWorld(Framebuffer fb)
    {
        DrawCave(fb);

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
            Blitters.DrawBulletXor(fb, b.X & ~7, b.Y, b.Pattern, 0x46);
        }

        // Player — flicker between visible/hidden while invincible so
        // the respawn window is unmistakeable.
        bool hidePlayer = (State == GameState.Dying)
                          || (Invincible && (_frameCounter & 2) == 0);
        if (!hidePlayer)
        {
            var playerSprite = FacingLeft ? PlayerSpriteLeft : PlayerSpriteRight;
            Blitters.DrawPlayerXor(fb, PlayerX - 8, PlayerY - 4, playerSprite, 0x43);
        }

        Hud.Draw(fb, this);
    }

    private void DrawCave(Framebuffer fb)
    {
        // Top and bottom borders out of repeating UDG tiles.
        int phase = (_frameCounter / 8 + Depth) % Math.Max(1, Udgs.Count);
        for (int col = 0; col < 32; col++)
        {
            int idx = (col + phase) % Math.Max(1, Udgs.Count);
            Blitters.DrawTile8x8(fb, col * 8, 0, Udgs[idx], 0x44);    // green
            Blitters.DrawTile8x8(fb, col * 8, 8, Udgs[idx], 0x44);
        }

        // Side walls: at each 8-pixel row band, compute the cave's half-
        // width and stamp wall tiles outside the safe corridor.
        // Pick a UDG tile per row for visual texture.
        for (int y = CaveTop; y < CaveBottom; y += 8)
        {
            int half = CaveHalfWidthAt(y);
            int leftPx  = (128 - half);
            int rightPx = (128 + half);
            int leftCol = leftPx >> 3;
            int rightCol = rightPx >> 3;
            int tileIdx = ((y >> 3) + Depth + _frameCounter / 16) % Math.Max(1, Udgs.Count);
            byte attr = (byte)(0x44 + ((y >> 4 + Depth) & 1));   // alternate green/yellow-ish

            for (int col = 0; col < leftCol; col++)
            {
                Blitters.DrawTile8x8(fb, col * 8, y, Udgs[tileIdx], 0x44);
            }
            for (int col = rightCol + 1; col < 32; col++)
            {
                Blitters.DrawTile8x8(fb, col * 8, y, Udgs[tileIdx], 0x44);
            }
        }
    }

    private void DrawTitleScreen(Framebuffer fb)
    {
        fb.FillAttributes(0x07);  // dim white on black
        // Title.
        MiniFont.DrawCentered(fb, 32, "SUBTERRANEAN STRYKER", 0x46);
        MiniFont.DrawCentered(fb, 48, "NATIVE C# PORT", 0x45);
        MiniFont.DrawCentered(fb, 72, "BY MIKE FOLLIN  1985", 0x44);
        MiniFont.DrawCentered(fb, 96, "RE-PORT  2026", 0x44);
        // Controls.
        MiniFont.DrawCentered(fb, 120, "Q/UP A/DN  DIVE", 0x47);
        MiniFont.DrawCentered(fb, 128, "L/LEFT/RT  TURN", 0x47);
        MiniFont.DrawCentered(fb, 136, "ENTER  FIRE", 0x47);
        // Blink prompt.
        if ((StateTicks & 16) < 8)
        {
            MiniFont.DrawCentered(fb, 160, "PRESS FIRE", 0x46);
        }
    }

    private void DrawGameOver(Framebuffer fb)
    {
        DrawWorld(fb);
        // Dim the cave by repainting every attribute black-on-black except
        // for the game-over banner.
        for (int i = 0; i < fb.Attributes.Length; i++) fb.Attributes[i] = 0x07;
        MiniFont.DrawCentered(fb, 72, "GAME OVER", 0x42);
        MiniFont.DrawCentered(fb, 88, $"DEPTH {Depth:D3}  SCORE {Score:D5}", 0x46);
        MiniFont.DrawCentered(fb, 96, $"RESCUED {Rescued:D2}", 0x44);
        if ((StateTicks & 16) < 8)
        {
            MiniFont.DrawCentered(fb, 128, "PRESS FIRE TO RETRY", 0x47);
        }
    }
}
