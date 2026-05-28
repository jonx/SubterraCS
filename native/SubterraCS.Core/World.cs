namespace SubterraCS.Core;

public enum GameState
{
    Splash,     // Loading screen (SUBSTRYK.SCR from the cassette)
    Title,      // Title menu — SELECT CONTROL OPTION (1..4)
    Playing,    // The actual game loop
    Dying,      // Brief death animation
    GameOver,   // Game-over screen — press FIRE to retry
    LevelClear, // Brief celebratory beat when all workers rescued
}

/// <summary>
/// The whole game.  Architecture mirrors how the original is structured:
///
///   load assets → splash → title → game loop:
///       LoadLevel(n) → place workers + start hazard schedule
///       per-frame:  update entities → player input → collisions
///       all workers rescued?  → LevelClear → LoadLevel(n+1)
///       player dies?          → Dying → respawn or GameOver
///
/// There is no "diving" or altitude-gate page advance.  The Stryker
/// flies freely within the playable area; the level progresses only
/// when every rescuable worker on this page has been picked up.
/// </summary>
public sealed class World
{
    public const int MaxEntities = 16;
    public const int MaxBullets  = 8;
    public const int PlayfieldTop = 0;       // top pixel of the play area
    public const int PlayfieldBottom = 128;  // first pixel of the HUD strip

    // Loaded once at boot ------------------------------------------------
    public TileBank Tiles { get; }
    public UdgBank Udgs { get; }
    public EntityBank EntityBank { get; }
    public EntityTypeTable EntityTypes { get; }
    public byte[] PlayerSpriteRight { get; }
    public byte[] PlayerSpriteLeft  { get; }

    // Per-game cassette assets ----------------------------------------
    public byte[] SplashScr { get; set; } = Array.Empty<byte>();
    public byte[] TitleMenuScr { get; set; } = Array.Empty<byte>();

    // Hazard schedules: depth 0..5 are the cassette pages from $E69D;
    // beyond that we hand off to the procedural generator so the game
    // keeps going.
    private readonly ProceduralGenerator _gen;
    private readonly SpawnSchedule[]? _originalLevels;
    public int Depth { get; private set; }  // current level (1-based for display)
    public SpawnSchedule Current { get; private set; }

    // Player -----------------------------------------------------------
    public int PlayerX = 120;
    public int PlayerY = 64;
    public bool FacingLeft;
    public int Score;
    public int Fuel = 100;
    public int Shield = 100;
    public int Rescued;
    public int Lives = 3;
    public bool Invincible;
    public int InvincibleTicks;

    // Game flow --------------------------------------------------------
    public GameState State { get; private set; } = GameState.Splash;
    public int StateTicks { get; private set; }
    public readonly SfxQueue Sfx = new();

    public readonly EntityInstance[] Entities = new EntityInstance[MaxEntities];
    public readonly Bullet[] Bullets = new Bullet[MaxBullets];

    // Level-local state ------------------------------------------------
    private int _workersToRescueThisLevel;
    public int WorkersRemaining =>
        Entities.Count(e => e.Alive && EntityAI.For(e.TypeId) == EntityAI.Kind.Worker);
    public int WorkersForThisLevel => _workersToRescueThisLevel;

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
        _gen = new ProceduralGenerator(seed);
        _rng = new Random(seed);
        _originalLevels = originalLevels;
        Current = ScheduleForLevel(0);

        for (int i = 0; i < Entities.Length; i++) Entities[i] = new EntityInstance();
        for (int i = 0; i < Bullets.Length; i++)  Bullets[i] = new Bullet();
    }

    private SpawnSchedule ScheduleForLevel(int level)
    {
        if (_originalLevels is { Length: > 0 } && level < _originalLevels.Length)
        {
            var src = _originalLevels[level].Entries;
            var copy = new ScheduleEntry[src.Length];
            Array.Copy(src, copy, src.Length);
            return new SpawnSchedule(copy);
        }
        return _gen.Page(level);
    }

    // ─── Tick dispatch ──────────────────────────────────────────────

    public void Tick(GameInput input)
    {
        _frameCounter++;
        StateTicks++;

        switch (State)
        {
            case GameState.Splash:     TickSplash(input); return;
            case GameState.Title:      TickTitle(input);  return;
            case GameState.GameOver:   TickGameOver(input); return;
            case GameState.Dying:      TickDying();        return;
            case GameState.LevelClear: TickLevelClear();   return;
        }

        TickPlaying(input);
    }

    private void TickSplash(GameInput input)
    {
        if ((input.Fire && StateTicks > 15) || StateTicks > 250)
            EnterState(GameState.Title);
    }

    private void TickTitle(GameInput input)
    {
        if (input.Fire && StateTicks > 10) StartNewGame();
    }

    private void TickGameOver(GameInput input)
    {
        if (input.Fire && StateTicks > 25) StartNewGame();
    }

    private void TickDying()
    {
        if (StateTicks < 40) return;
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

    private void TickLevelClear()
    {
        if (StateTicks >= 60)
        {
            LoadLevel(Depth + 1);
        }
    }

    // ─── Playing-state tick ─────────────────────────────────────────

    private void TickPlaying(GameInput input)
    {
        // Free flight — A/Q move up/down, L flips facing and translates.
        if (input.Down)
        {
            PlayerY = Math.Min(PlayfieldBottom - 8, PlayerY + 2);
            Fuel = Math.Max(0, Fuel - 1);
        }
        else if (input.Up)
        {
            PlayerY = Math.Max(PlayfieldTop + 8, PlayerY - 2);
            Fuel = Math.Max(0, Fuel - 1);
        }
        if (input.Horizontal)
        {
            PlayerX += FacingLeft ? -2 : 2;
            if (PlayerX < 16 || PlayerX > 232)
            {
                FacingLeft = !FacingLeft;
                PlayerX = Math.Clamp(PlayerX, 16, 232);
            }
            Fuel = Math.Max(0, Fuel - 1);
        }
        if ((_frameCounter & 31) == 0) Fuel = Math.Max(0, Fuel - 1);

        if (Fuel <= 0) { TriggerDeath(); return; }

        // Tick the hazard schedule (this only spawns hazards — workers
        // are placed statically at LoadLevel time, not via the schedule).
        TickHazardSchedule();

        // Update every live entity.
        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            var kind = EntityAI.For(e.TypeId);
            if (!EntityAI.Tick(e, kind, PlayerX, PlayerY, _rng))
            {
                e.Alive = false;
                continue;
            }
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
                if (Shield <= 0 || Fuel <= 0) { TriggerDeath(); return; }
            }
        }

        // Bullets.
        if (_fireCooldown > 0) _fireCooldown--;
        if (input.Fire && _fireCooldown == 0) FireBullet();
        foreach (var b in Bullets)
        {
            if (!b.Alive) continue;
            b.X += b.DX;
            b.Y += b.DY;
            if ((uint)b.Y >= 192 || (uint)b.X >= 256) { b.Alive = false; continue; }
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

        if (InvincibleTicks > 0 && --InvincibleTicks == 0) Invincible = false;

        // Level complete?
        if (WorkersRemaining == 0 && _workersToRescueThisLevel > 0)
        {
            Sfx.Trigger(SfxKind.LevelUp);
            Score += 250;
            EnterState(GameState.LevelClear);
        }
    }

    private void TickHazardSchedule()
    {
        for (int i = 0; i < SpawnSchedule.Slots; i++)
        {
            var entry = Current.Entries[i];
            // Skip worker entries in the schedule — workers are placed
            // statically by LoadLevel; we only stream hazards here.
            if (entry.TypeId == 0)
            {
                Current.Entries[i] = new ScheduleEntry(0xFFFF, entry.TypeId, entry.Flags);
                continue;
            }
            ushort newTimer = (ushort)(entry.Timer - 1);
            if (newTimer > entry.Timer)
            {
                Spawn(entry.TypeId, entry.Flags);
                newTimer = (ushort)(0x0030 + _rng.Next(0, 0x0040));
            }
            Current.Entries[i] = new ScheduleEntry(newTimer, entry.TypeId, entry.Flags);
        }
    }

    // ─── Lifecycle helpers ──────────────────────────────────────────

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
        slot.TypeId = 8;
        slot.X = x; slot.Y = y;
        slot.DX = 0; slot.DY = 0;
        slot.AgeFrames = 0;
        slot.Hp = 1;
        slot.MaxFrames = TypeMaxFrames(8);
        slot.Frame = 0;
        slot.FrameTick = 0;
        slot.Alive = true;
    }

    private void Respawn()
    {
        foreach (var b in Bullets) b.Alive = false;
        // Wipe hazards but KEEP workers — death shouldn't reset progress.
        foreach (var e in Entities)
        {
            if (e.Alive && EntityAI.For(e.TypeId) != EntityAI.Kind.Worker)
                e.Alive = false;
        }
        PlayerX = 120; PlayerY = 64;
        Shield = 100;
        Fuel = Math.Max(50, Fuel);
        SetInvincible(100);
        EnterState(GameState.Playing);
    }

    private void StartNewGame()
    {
        Lives = 3;
        Score = 0;
        Rescued = 0;
        LoadLevel(0);
    }

    /// <summary>
    /// Load a level: clear the world, reset the hazard schedule,
    /// place a fresh set of rescuable workers at scripted positions,
    /// and resume the play state.  This is the "load level" stage of
    /// the game loop — every new page enters here.
    /// </summary>
    public void LoadLevel(int level)
    {
        Depth = level;
        Current = ScheduleForLevel(level);
        foreach (var e in Entities) e.Alive = false;
        foreach (var b in Bullets) b.Alive = false;
        PlayerX = 120; PlayerY = 64;
        Shield = 100;
        Fuel = Math.Min(100, Fuel + 25);
        SetInvincible(60);
        PlaceWorkersForLevel(level);
        EnterState(GameState.Playing);
    }

    /// <summary>
    /// Static placement of the rescuable workers on this level.
    /// Count rises gently with depth so the rescue mission gets harder
    /// the further the player gets.  Positions are deterministic from
    /// the level number so a given playthrough is reproducible.
    /// </summary>
    private void PlaceWorkersForLevel(int level)
    {
        int workers = Math.Clamp(3 + level, 3, 8);
        _workersToRescueThisLevel = workers;
        var rng = new Random(HashCode.Combine(level, 0x7E57));
        for (int i = 0; i < workers; i++)
        {
            var slot = NextFreeEntity();
            if (slot is null) break;
            slot.TypeId = 0;       // worker bank
            slot.Frame = rng.Next(0, TypeMaxFrames(0));
            slot.FrameTick = rng.Next(0, 4);
            slot.MaxFrames = TypeMaxFrames(0);
            slot.AgeFrames = 0;
            slot.Hp = 1;
            slot.X = 24 + (i + 1) * (208 / (workers + 1)) + rng.Next(-8, 9);
            slot.Y = 112;          // walking on the surface
            slot.DX = rng.Next(0, 2) == 0 ? -1 : 1;
            slot.DY = 0;
            slot.Alive = true;
        }
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
            b.Pattern = 0x18;
            b.Alive = true;
            _fireCooldown = 8;
            Sfx.Trigger(SfxKind.Fire);
            return;
        }
    }

    // ─── Drawing ────────────────────────────────────────────────────

    public void Draw(Framebuffer fb)
    {
        fb.Clear();
        fb.FillAttributes(0x07);

        switch (State)
        {
            case GameState.Splash:     DrawSplash(fb);  break;
            case GameState.Title:      DrawTitle(fb);   break;
            case GameState.GameOver:   DrawGameOver(fb); break;
            case GameState.LevelClear: DrawLevelClear(fb); break;
            default:                   DrawPlaying(fb); break;
        }
    }

    private void DrawSplash(Framebuffer fb)
    {
        if (SplashScr.Length >= ScreenLoader.ScrSize)
        {
            ScreenLoader.OverwriteFramebuffer(fb, SplashScr);
        }
        else
        {
            MiniFont.DrawCentered(fb, 80, "LOADING", 0x47);
        }
    }

    private void DrawTitle(Framebuffer fb)
    {
        if (TitleMenuScr.Length >= ScreenLoader.ScrSize)
        {
            ScreenLoader.OverwriteFramebuffer(fb, TitleMenuScr);
            // Blink "PRESS FIRE" prompt at the bottom — the captured
            // SCR shows the SELECT CONTROL OPTION menu, but our port
            // only ships the keyboard handler.  This makes it
            // unmistakeable what to press.
            // Don't draw our own prompt — the captured menu already
            // says "SELECT CONTROL OPTION TO BEGIN".  Pressing FIRE
            // (or any of the 4 listed keys in practice) advances.
            return;
        }
        MiniFont.DrawCentered(fb, 80, "SUBTERRANEAN STRYKER", 0x46);
        MiniFont.DrawCentered(fb, 96, "PRESS FIRE", 0x47);
    }

    private void DrawPlaying(Framebuffer fb)
    {
        DrawLevelScenery(fb);

        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            var type = EntityTypes.Types[e.TypeId];
            var sprite = EntityBank.Frame(type.SpritePointer, e.Frame);
            if (sprite.IsEmpty) continue;
            Blitters.DrawSprite16x16(fb, e.X - 8, e.Y - 8, sprite, type.Attribute);
        }

        foreach (var b in Bullets)
        {
            if (!b.Alive) continue;
            Blitters.DrawBulletXor(fb, b.X & ~7, b.Y, b.Pattern, 0x46);
        }

        bool hidePlayer = State == GameState.Dying
                          || (Invincible && (_frameCounter & 2) == 0);
        if (!hidePlayer)
        {
            var playerSprite = FacingLeft ? PlayerSpriteLeft : PlayerSpriteRight;
            Blitters.DrawPlayerXor(fb, PlayerX - 8, PlayerY - 4, playerSprite, 0x43);
        }

        Hud.Draw(fb, this);
    }

    /// <summary>
    /// Paint the level's static scenery.  The original game composes
    /// each page from the master tile bank at <c>$B0F4</c> driven by
    /// per-level data tables; until we've fully reversed that path the
    /// native port draws a minimal but level-distinct skyline so the
    /// playfield isn't blank.
    /// </summary>
    private void DrawLevelScenery(Framebuffer fb)
    {
        // Sky attribute strip — black background with cyan ink, the
        // colour the original uses for the upper play area.
        for (int row = 0; row < 16; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Attributes[row * 32 + col] = 0x05;  // cyan on black
            }
        }

        // Ground line at row 16 (y = 128).  Use UDG tile 0 (cave-floor
        // pattern) repeated across the whole screen, painted green.
        int floorTile = Math.Min(Depth, Math.Max(0, Udgs.Count - 1));
        for (int col = 0; col < 32; col++)
        {
            Blitters.DrawTile8x8(fb, col * 8, 128, Udgs[floorTile], 0x44);
        }
        // Second row of cave-floor pattern, slightly shifted, gives a
        // grass-band silhouette.
        int secondTile = (floorTile + 1) % Math.Max(1, Udgs.Count);
        for (int col = 0; col < 32; col++)
        {
            Blitters.DrawTile8x8(fb, col * 8, 136, Udgs[secondTile], 0x44);
        }

        // Per-level accent tile sprinkled along the ground for variety.
        var accent = Udgs.Count > 0 ? Udgs[(Depth * 7) % Udgs.Count] : ReadOnlySpan<byte>.Empty;
        for (int col = 2; col < 32; col += 6)
        {
            Blitters.DrawTile8x8(fb, col * 8, 120, accent, 0x44);
        }
    }

    private void DrawLevelClear(Framebuffer fb)
    {
        DrawPlaying(fb);
        if ((StateTicks & 8) < 4)
        {
            MiniFont.DrawCentered(fb, 56, $"LEVEL {Depth + 1} CLEAR", 0x46);
            MiniFont.DrawCentered(fb, 72, $"+250", 0x44);
        }
    }

    private void DrawGameOver(Framebuffer fb)
    {
        DrawPlaying(fb);
        for (int i = 0; i < fb.Attributes.Length; i++) fb.Attributes[i] = 0x07;
        MiniFont.DrawCentered(fb, 64, "GAME OVER", 0x42);
        MiniFont.DrawCentered(fb, 80, $"LEVEL {Depth + 1}  SCORE {Score:D5}", 0x46);
        MiniFont.DrawCentered(fb, 88, $"RESCUED {Rescued:D2}", 0x44);
        if ((StateTicks & 16) < 8)
        {
            MiniFont.DrawCentered(fb, 128, "PRESS FIRE TO RETRY", 0x47);
        }
    }
}
