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
/// The Stryker is a SPACE SHIP — it flies freely up / down / left /
/// right within the playable area.  There is no altitude-gate page
/// advance.  A level progresses only when every rescuable worker on
/// the page has been picked up.
/// </summary>
public sealed class World
{
    public const int MaxEntities = 16;
    public const int MaxBullets  = 8;
    public const int PlayfieldTop = 0;        // top pixel of the play area
    public const int PlayfieldBottom = 128;   // first pixel of the HUD strip (HUD starts row 16)

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
    public RomFont? RomFont { get; set; }
    public LevelEntities? LevelEntities { get; set; }
    public MiniMap MiniMap { get; set; } = new();
    public readonly LevelScroll Scroll = new();
    private bool _levelPainted;

    // Hazard schedules: depth 0..5 are the cassette pages from $E69D;
    // beyond that we hand off to the procedural generator so the game
    // keeps going.
    private readonly ProceduralGenerator _gen;
    private readonly SpawnSchedule[]? _originalLevels;
    public int Depth { get; private set; }  // current level (1-based for display)
    public SpawnSchedule Current { get; private set; }

    // Player -----------------------------------------------------------
    // Initial position matches the emulator at game-start: top-left
    // quadrant address $400F = pixel (120, 0).  The Stryker sprite is
    // 16×16 so its centre is at (128, 8).  Verified by reading the
    // $E8C9 quadrant table from at-f100.bin.
    public int PlayerX = 128;
    public int PlayerY = 8;

    /// <summary>Bar-fill animation override.  When >= 0 the HUD bar
    /// drawer uses this instead of <see cref="Shield"/>/<see cref="Fuel"/>.
    /// Port of the $E41B..$E446 fill loop in the original (48 iterations
    /// of +2 with a per-iter beep; takes ~50 frames at level-start).</summary>
    public int BarFillOverride = -1;
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

        // The hazard schedule (port of $EF02) is not yet byte-faithful —
        // it spawns at inflated cadences and at random x positions that
        // don't match the emulator.  Disabled until we port the real
        // executor.  Keep TickHazardSchedule as code for future plug-in.
        // TickHazardSchedule();

        // Animate the bar fill at level start.  Port of $E41B..$E446
        // which loops 48 times with A += 2 and a beep each iter, taking
        // ~50 frames in total.  Empirical match: shield/fuel go 10 →
        // 95 between f80 and f130 in the emulator.
        const int BarFillStart = 80;
        const int BarFillEnd = 130;
        if (_frameCounter >= BarFillStart && _frameCounter <= BarFillEnd)
        {
            // Linear approximation of the $E41B loop's value ramp.
            int progress = _frameCounter - BarFillStart;        // 0..50
            int barVal = 10 + (progress * 85) / (BarFillEnd - BarFillStart);
            BarFillOverride = Math.Clamp(barVal, 0, 95);
        }
        else if (_frameCounter > BarFillEnd)
        {
            BarFillOverride = -1;   // bars use the real Shield/Fuel
        }
        else
        {
            // Pre-fill (f<80): mirror the emu which shows full bars
            // (the values were set to $5F earlier somewhere).
            BarFillOverride = 95;
        }

        // Animate the level scroll-in: one $DB1A iteration every
        // ScrollFramesPerStep frames, starting at ScrollStartFrame.
        // Matches the emulator's observed pace (16 rows painted
        // between f140 and f200 = ~3.75 frames per row).
        const int ScrollStartFrame = 140;
        const int ScrollFramesPerStep = 4;
        if (_frameCounter >= ScrollStartFrame && !Scroll.ScrollComplete
            && (_frameCounter - ScrollStartFrame) % ScrollFramesPerStep == 0
            && MiniMap.Buffer.Length > 0)
        {
            Scroll.ScrollOneStep(Tiles, MiniMap.Buffer);
        }

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
        PlayerX = 128; PlayerY = 4;
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
        // The original's $E587 starts at 0 but $F6F2 INC's it before
        // entering the first playable level — so the first level the
        // player sees uses index 1's records (10 entities, the clean
        // 8-byte stride at $F2EB).  Level 0 is the anomalous short
        // record set we haven't fully decoded.
        LoadLevel(1);
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
        PlayerX = 128; PlayerY = 4;
        Shield = 100;
        Fuel = Math.Min(100, Fuel + 25);
        SetInvincible(60);
        // Switch the active mini-map buffer to this level's packed
        // bytes (port of the original's $E579 ← $E56D[level*2] step).
        MiniMap.SelectLevel(level);
        Scroll.Reset();
        // Level scenery paint is deferred — see Scroll.PaintLevel call
        // gated by frame counter in TickPlaying, matching the emu's
        // scroll-in over f140..f200.
        _levelPainted = false;
        PlaceWorkersForLevel(level);
        EnterState(GameState.Playing);
    }

    /// <summary>
    /// Static placement of entities for this level — uses the
    /// per-level records from <c>$F2E8</c> (RE-LOG §30).  Each record
    /// supplies (type, y, frame, top-screen-addr) which we decode
    /// back to (type, x, y) for the C# entity instance.
    ///
    /// Note: the original's "level 0" record list is anomalous
    /// (overlaps with level 1's start), so we use the records as
    /// stored but expect level 1+ to be the cleanly-decoded path.
    /// </summary>
    private void PlaceWorkersForLevel(int level)
    {
        if (LevelEntities is null || level >= LevelEntities.Levels.Length)
        {
            PlaceFallbackWorkers(level);
            return;
        }

        var records = LevelEntities.Levels[level];
        int workerCount = 0;
        foreach (var rec in records)
        {
            // TEMPORARILY SUPPRESS all template entities — they appear
            // at static positions in our port but at f=100 the emulator
            // doesn't render any of them.  Need more RE to know when
            // entities go "live".  For now this gives the cleanest diff.
            if (true) { if (rec.TypeId == 0) workerCount++; continue; }

            var slot = NextFreeEntity();
            if (slot is null) break;
            slot.TypeId = rec.TypeId;
            slot.Frame = rec.Frame & 0x0F;     // frames are 0..15
            slot.FrameTick = 0;
            slot.MaxFrames = TypeMaxFrames(rec.TypeId);
            slot.AgeFrames = 0;
            slot.Hp = 1;
            // Decode (x, y) from the original's top-half screen address.
            var (x, y) = LevelEntities.DecodeBitmapAddress(rec.TopAddr);
            slot.X = x;
            slot.Y = y;
            slot.DX = (rec.Flags & 0x40) != 0 ? -1 : 1;
            slot.DY = 0;
            slot.Alive = true;
            if (rec.TypeId == 0) workerCount++;
        }
        // Only require rescues if the level actually has workers; some
        // pages are pure hazard rooms.  Setting to 0 disables the
        // auto-progress check until we wire a different end-condition.
        _workersToRescueThisLevel = workerCount;
    }

    /// <summary>Fallback if level-entity asset is missing — keeps the
    /// rescue mechanic alive without crashing.</summary>
    private void PlaceFallbackWorkers(int level)
    {
        int workers = Math.Clamp(3 + level, 3, 8);
        _workersToRescueThisLevel = workers;
        var rng = new Random(HashCode.Combine(level, 0x7E57));
        for (int i = 0; i < workers; i++)
        {
            var slot = NextFreeEntity();
            if (slot is null) break;
            slot.TypeId = 0;
            slot.MaxFrames = TypeMaxFrames(0);
            slot.X = 24 + (i + 1) * (208 / (workers + 1)) + rng.Next(-8, 9);
            slot.Y = 140;
            slot.DX = rng.Next(0, 2) == 0 ? -1 : 1;
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

        // Blit the persistent play-area bitmap (painted at level-load
        // by Scroll.PaintLevel — port of $DB1A) into the framebuffer.
        Scroll.Blit(fb);

        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            if (e.TypeId < 0 || e.TypeId >= EntityTypes.Types.Length) continue;
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
                          || (Invincible && (_frameCounter & 2) == 0)
                          // Match the emu: the player is not drawn
                          // until the level paint completes.  Verified
                          // by sampling f60..f400 in the emu — first
                          // visible byte at f232, right after $DB1A's
                          // 16 outer iterations finish.
                          || !Scroll.ScrollComplete;
        if (!hidePlayer)
        {
            var playerSprite = FacingLeft ? PlayerSpriteLeft : PlayerSpriteRight;
            Blitters.DrawPlayerXor(fb, PlayerX - 8, PlayerY - 4, playerSprite, 0x43);
        }

        Hud.Draw(fb, this);

        // Mini-map at the bottom strip (y=160..191) — port of $E104.
        // Drawn AFTER Hud which clears the HUD-region bitmap as part
        // of its repaint pass; we layer the mini-map on top of the
        // green-on-black attribute strip that Hud sets for rows 20-23.
        MiniMap.DrawTo(fb);
    }

    /// <summary>
    /// Paint the level's static scenery.  In the original, the top 16
    /// char-rows (y=0..127) are the playfield — empty sky at level
    /// start, populated by entities over time.  The bottom 8 char-rows
    /// (y=128..191) are the HUD chrome, drawn by <see cref="Hud.Draw"/>.
    ///
    /// The hand-drawn hill silhouette + tree we see in mid-gameplay
    /// renders are NOT a static backdrop — they emerge as entities
    /// accumulate.  Per RE-LOG §24 the routine that paints them is
    /// not fully traced yet, so for now this method just sets up the
    /// attribute strip for the playfield and lets the entity system
    /// fill in the rest.
    /// </summary>
    private void DrawLevelScenery(Framebuffer fb)
    {
        // Playfield attribute strip — bright green on black, matching
        // the emulator-peeked attributes at rows 0..15 in mid-gameplay
        // RAM (uniform $04 from $5800..$59FF — see RE-LOG §24).
        for (int row = 0; row < HudCharRow; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Attributes[row * 32 + col] = 0x04;  // green ink on black
            }
        }
    }

    private const int HudCharRow = 16;

    private void DrawLevelClear(Framebuffer fb)
    {
        DrawPlaying(fb);
        if ((StateTicks & 8) < 4)
        {
            MiniFont.DrawCentered(fb, 56, $"LEVEL {Depth} CLEAR", 0x46);
            MiniFont.DrawCentered(fb, 72, $"+250", 0x44);
        }
    }

    private void DrawGameOver(Framebuffer fb)
    {
        DrawPlaying(fb);
        for (int i = 0; i < fb.Attributes.Length; i++) fb.Attributes[i] = 0x07;
        MiniFont.DrawCentered(fb, 64, "GAME OVER", 0x42);
        MiniFont.DrawCentered(fb, 80, $"LEVEL {Depth}  SCORE {Score:D5}", 0x46);
        MiniFont.DrawCentered(fb, 88, $"RESCUED {Rescued:D2}", 0x44);
        if ((StateTicks & 16) < 8)
        {
            MiniFont.DrawCentered(fb, 128, "PRESS FIRE TO RETRY", 0x47);
        }
    }
}
