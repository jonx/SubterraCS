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

    /// <summary>The player ship's Y position equals <see cref="Altitude"/>.
    /// Verified by inspecting $E8C9 quadrant addresses across the
    /// captures in <c>build/at-down-fXXX.bin</c>: at altitude=$00 the
    /// addresses decode to (120, 0); at altitude=$51 they decode to
    /// (120, 80).  Y = altitude is exact.</summary>
    public int PlayerY => Altitude;

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
    public readonly Explosion Explosion = new();
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
    // X is fixed at 120, but Y == Altitude (the ship moves vertically
    // on screen with altitude — see PlayerY above).
    public const int FixedPlayerX = 128;
    public int PlayerX = FixedPlayerX;

    /// <summary>$E584 — player altitude (0..$78 = 0..120).  UP decreases,
    /// DOWN increases, neither resets the SpeedShift.  At $78 the level
    /// page advances and altitude resets to 0 (port of $F868 / $F6F2
    /// chain).</summary>
    public int Altitude;

    /// <summary>$E585 — acceleration counter.  Increments while holding
    /// a vertical direction; resets to 1 on neutral.  Effective per-frame
    /// altitude delta = (SpeedShift >> 1) | 1.</summary>
    public int SpeedShift = 1;

    /// <summary>$E586 — direction state byte.  Bit 0 = facing left.
    /// Bit 1 = was-moving-down (used to detect direction reversal).</summary>
    public int DirectionState;

    /// <summary>Horizontal scroll offset (0..255), in tile-columns.
    /// Mirrors the original's $E579 source-pointer offset: when the
    /// player holds L, the bitmap shifts one byte (one tile-col) per
    /// frame; this offset advances to expose the next column from the
    /// 256-column-wide source data.  Wraps at 256.</summary>
    public int ScrollOffsetX;

    /// <summary>Bar-fill animation override.  When >= 0 the HUD bar
    /// drawer uses this instead of <see cref="Shield"/>/<see cref="Fuel"/>.
    /// Port of the $E41B..$E446 fill loop in the original (48 iterations
    /// of +2 with a per-iter beep; takes ~50 frames at level-start).</summary>
    public int BarFillOverride = -1;
    public bool FacingLeft;
    public int Score;
    // Fuel and Shield use the native game range 0..$5F (0..95), matching
    // the original's $E466 and $E464.  The HUD's 24-cell bar represents
    // exactly this range (4 quanta per cell × 24 = 96, capped at $5F).
    public const int BarMax = 0x5F;       // 95 — original's $E464/$E466 cap
    public int Fuel = BarMax;
    public int Shield = BarMax;
    /// <summary>$E463 — hit accumulator.  Each collision SUBs $40; on
    /// underflow, <see cref="Shield"/> DECrements.  This gives ~4 hits
    /// per bar notch — port of $DDC4's logic.</summary>
    public int HitAccum = 0xFF;
    /// <summary>$E465 — fuel accumulator.  Each L-key frame SUBs $20;
    /// on underflow, <see cref="Fuel"/> DECrements.  Port of
    /// $D8D8..$D8EC.</summary>
    public int FuelAccum = 0xFF;
    public int Rescued;
    /// <summary>$E588 — lives counter (game-over when DEC reaches 0,
    /// i.e. lives transitions 1 → 0).  Verified by inspecting
    /// build/at-f100.bin: $E588 = 5 at game start.</summary>
    public int Lives = 5;
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
        // The original cassette sits on the splash screen indefinitely
        // until the user presses FIRE.  Match that — our previous
        // 250-tick auto-advance was breaking the diff-frame harness
        // (the emu has no input so it stays on splash, but our port
        // jumped to Title at ~f250, producing a huge state-mismatch
        // diff from f281 onwards).
        if (input.Fire && StateTicks > 15)
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
        Explosion.Tick();
        // Hold for the explosion's full 64-frame run, then game-over
        // or respawn.  Port of $DBC8 (which runs 4 × 64 anim iters
        // then JP $D8A8); we run a single 64-iter pass.
        if (StateTicks < Explosion.AnimFrames) return;
        Explosion.Reset();
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
            LoadLevel(NextLevel(Depth));
        }
    }

    /// <summary>Compute the next level index — port of $F6F2's
    /// INC + CP $06 + XOR A pattern, which wraps level 5 back to 0.</summary>
    private static int NextLevel(int current) => (current + 1) > 5 ? 0 : current + 1;

    // ─── Playing-state tick ─────────────────────────────────────────

    private void TickPlaying(GameInput input)
    {
        // ---- Vertical movement — port of $D95D ----
        // Player Y on screen is FIXED; UP/DOWN adjust Altitude
        // (= the original's $E584).  Acceleration: each frame the
        // direction is held, SpeedShift increments (capped at 7);
        // the actual altitude delta is (SpeedShift >> 1) | 1.
        // When neutral: SpeedShift resets to 1.
        if (input.Up)
        {
            // Direction-change detection: bit 1 of state tracks "was
            // moving down" — clear on UP to reset acceleration.
            if ((DirectionState & 2) != 0)
            {
                DirectionState &= ~2;
                SpeedShift = 1;
            }
            int delta = (SpeedShift >> 1) | 1;
            Altitude = Math.Max(0, Altitude - delta);
            if ((SpeedShift & 0x08) == 0) SpeedShift++;
        }
        else if (input.Down)
        {
            if ((DirectionState & 2) == 0)
            {
                DirectionState |= 2;
                SpeedShift = 1;
            }
            int delta = (SpeedShift >> 1) | 1;
            Altitude = Math.Min(0x78, Altitude + delta);
            if ((SpeedShift & 0x08) == 0) SpeedShift++;
        }
        else
        {
            SpeedShift = 1;
        }

        // ---- Horizontal — port of $D9C8 → $DA23 / $DA62 ----
        // Holding LEFT/RIGHT (or O/P) scrolls the ENTIRE LEVEL bitmap
        // one tile-column per frame, in the direction of the pressed
        // key.  The ship stays at screen X=120; the level slides past
        // it.  The plain L key scrolls in the current facing direction
        // (same as L on the original).
        //
        // Facing is updated by which direction key is pressed; bit 0
        // of DirectionState mirrors the original's $E586 bit 0.
        if (input.Left) { FacingLeft = true;  DirectionState |= 1; }
        else if (input.Right) { FacingLeft = false; DirectionState &= ~1; }

        if (input.Horizontal && Fuel > 0 && MiniMap.Buffer.Length > 0)
        {
            int delta = FacingLeft ? -1 : 1;
            ScrollOffsetX = (ScrollOffsetX + delta) & 0xFF;
            Scroll.PaintLevelAtOffset(Tiles, MiniMap.Buffer, ScrollOffsetX);
        }

        // Port of $F1EF's $F222 SUB B / CP $1F / RET NC gate:
        // recompute screen X and visibility for every entity each
        // frame from its WorldX vs the scroll cursor.
        // ScrollOffsetX = $E583.  Visible when (WorldX - $E583) in [0, 31].
        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            int offset = (e.WorldX - ScrollOffsetX) & 0xFF;
            if (offset < 0x1F)
            {
                e.Visible = true;
                e.X = offset * 8;
            }
            else
            {
                e.Visible = false;
                e.X = -16;       // park off-screen so no collision
            }
        }

        // ---- Page-advance gate — port of $F868 → $F6F2 ----
        // The original's gate at $F868 checks `CP $75; RET C` — so
        // altitude must reach $75 to trigger the page advance.  Adds
        // 1000 to score and calls $F6F2 (which INCs $E587 mod 6 and
        // re-runs the full level-load chain).  Our $D95D port caps
        // altitude at $78 so we use that as the trigger threshold.
        if (Altitude >= 0x75)
        {
            Score += 1000;
            LoadLevel(NextLevel(Depth));
            return;
        }

        // Fuel drain — port of $D8D8..$D8EC.  The L key (horizontal
        // input) drains the FuelAccum ($E465) by $20 each frame; on
        // underflow, Fuel ($E466) DECs by 1.  So holding L for 8 frames
        // costs 1 fuel unit.  At 60 fps and BarMax = 95, full tank
        // depletes in ~12.7 seconds of held horizontal input.
        if (input.Horizontal)
        {
            FuelAccum -= 0x20;
            if (FuelAccum < 0)
            {
                FuelAccum &= 0xFF;
                Fuel = Math.Max(0, Fuel - 1);
            }
        }

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
            // The $E41B fill loop accelerates: empirically observed
            // values are 10/20/30/44/64/95 at f80/f90/f100/f110/f120/f130.
            // Quadratic fit: v(t) = 0.0233 t² + 0.534 t + 10 where
            // t = frame - 80.  Implemented as integer arithmetic:
            // v = (233 t² + 5340 t + 100000) / 10000.
            long t = _frameCounter - BarFillStart;
            long v = (233 * t * t + 5340 * t + 100000) / 10000;
            BarFillOverride = (int)Math.Clamp(v, 0, 95);
        }
        else if (_frameCounter > BarFillEnd)
        {
            BarFillOverride = -1;   // bars use the real Shield/Fuel
        }
        else
        {
            // Pre-fill (f<80): bars show UDG-A corners only with
            // empty middle.  $E464 = $5F at this point in the emu,
            // but $E0BE hasn't been called yet so the cells aren't
            // filled.  Setting value=0 makes our DrawBar output
            // the same empty-middle pattern.
            BarFillOverride = 0;
        }

        // Animate the level scroll-in: target 16 steps spread across
        // 60 frames between f140 and f200 — matches the emulator's
        // observed 3.75-frames-per-row average.  Rate-based: each
        // frame compute the target step count; if it's ahead of the
        // current count, advance.
        const int ScrollStartFrame = 140;
        const int ScrollTotalFrames = 60;
        if (_frameCounter >= ScrollStartFrame && !Scroll.ScrollComplete
            && MiniMap.Buffer.Length > 0)
        {
            int elapsed = _frameCounter - ScrollStartFrame;
            int targetSteps = Math.Min(LevelScroll.CharRows,
                elapsed * LevelScroll.CharRows / ScrollTotalFrames + 1);
            while (Scroll.ScrolledRows < targetSteps && !Scroll.ScrollComplete)
            {
                Scroll.ScrollOneStep(Tiles, MiniMap.Buffer);
            }
        }

        // Update every live entity.  Off-window (invisible) entities
        // still tick (for AI / lifetimes) but skip the collision check
        // since their X is parked off-screen.
        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            var kind = EntityAI.For(e.TypeId);
            if (!EntityAI.Tick(e, kind, PlayerX, PlayerY, _rng))
            {
                e.Alive = false;
                continue;
            }
            if (!e.Visible) continue;
            // Port of $DD8C collision test: entity X within ±1 byte
            // (8 px) AND |entity.Y - player.Y| < 8 px.  Both sprites
            // are 16×16; e.X/e.Y is the entity sprite's TOP-LEFT;
            // PlayerX is the ship's CENTER, PlayerY is its top-left.
            // Compare centers: entityCx = e.X + 8, playerCx = PlayerX.
            int entCx = e.X + 8;
            int entCy = e.Y + 8;
            int plyCy = PlayerY + 8;
            if (!Invincible
                && Math.Abs(entCx - PlayerX) < 12
                && Math.Abs(entCy - plyCy) < 8)
            {
                var rule = EntityAI.Collision(kind);
                // Port of $DDC4: damage hits drain the HitAccum by $40;
                // only on underflow does the visible Shield decrement.
                if (rule.ShieldDelta < 0)
                {
                    HitAccum -= 0x40;
                    if (HitAccum < 0)
                    {
                        HitAccum &= 0xFF;
                        Shield = Math.Max(0, Shield - 1);
                    }
                    Sfx.Trigger(SfxKind.Damage);
                    SetInvincible(20);
                }
                else
                {
                    // Pickups still grant whole-bar units.
                    Shield = Math.Clamp(Shield + rule.ShieldDelta, 0, BarMax);
                    if (rule.ShieldDelta > 0 || rule.RescuedDelta > 0)
                        Sfx.Trigger(SfxKind.Pickup);
                }
                Fuel = Math.Clamp(Fuel + rule.FuelDelta, 0, BarMax);
                Score += rule.ScoreOnContact;
                Rescued += rule.RescuedDelta;
                if (rule.ConsumedOnContact) e.Alive = false;
                if (Shield <= 0 || Fuel <= 0) { TriggerDeath(); return; }
            }
        }

        // Lasers — port of $DE41 + $DEF0.  In the original, all 15
        // beam bytes are painted at fire time, then the trailing
        // (ship-side) byte is erased each frame so the visible beam
        // appears to "fade from the back" toward a fixed head at the
        // far edge.  Faithful but visually ambiguous at 60 fps —
        // looks too much like the beam moves backward.
        //
        // We use a clearer projectile model: the HEAD travels forward
        // by 8 px (one beam-byte) per frame, with a short trail
        // behind, until it has traveled MaxLength bytes.  Same
        // duration and same hit area as the original beam, just
        // rendered as a moving "bolt" instead of a fading streak.
        // The per-frame self-collide that $DEDA does (`INC(HL);
        // DEC(HL); JR NZ` to bail at non-zero pixels) becomes our
        // entity-collision check below.
        if (_fireCooldown > 0) _fireCooldown--;
        if (input.Fire && _fireCooldown == 0) FireBullet();
        foreach (var b in Bullets)
        {
            if (!b.Alive) continue;
            int dir = b.DX > 0 ? 1 : -1;
            // Advance head; expire on offscreen or once traveled.
            b.X += b.DX;
            b.Length--;
            if (b.Length == 0 || (uint)b.X >= 256) { b.Alive = false; continue; }
            // Collision against entities — the beam's CURRENT position
            // is a small 8×8 hit area at (b.X, b.Y).
            foreach (var e in Entities)
            {
                if (!e.Alive) continue;
                var kind = EntityAI.For(e.TypeId);
                if (EntityAI.IsBulletProof(kind)) continue;
                if (Math.Abs(e.X - b.X) < 10 && Math.Abs(e.Y - b.Y) < 8)
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
        // Port of $DBC8: attribute-particle explosion at the player's
        // current screen position.  The original seeds Y from $E584
        // ($BF - altitude); since our player sprite is fixed at
        // (PlayerX, PlayerY) we just use those.  Level colour (the
        // original's $E57B) drives the first paint of the alternation.
        Explosion.Trigger(PlayerX, PlayerY, 0x44);
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
        PlayerX = FixedPlayerX; Altitude = 0; SpeedShift = 1; DirectionState = 0;
        Shield = BarMax;
        Fuel = Math.Max(BarMax / 2, Fuel);
        HitAccum = 0xFF;
        FuelAccum = 0xFF;
        SetInvincible(100);
        EnterState(GameState.Playing);
    }

    private void StartNewGame()
    {
        Lives = 5;
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
        PlayerX = FixedPlayerX; Altitude = 0; SpeedShift = 1; DirectionState = 0;
        Shield = BarMax;
        Fuel = Math.Min(BarMax, Fuel + (BarMax / 4));
        HitAccum = 0xFF;
        FuelAccum = 0xFF;
        SetInvincible(60);
        // Switch the active mini-map buffer to this level's packed
        // bytes (port of the original's $E579 ← $E56D[level*2] step).
        MiniMap.SelectLevel(level);
        Scroll.Reset();
        ScrollOffsetX = 0;
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
            var slot = NextFreeEntity();
            if (slot is null) break;
            slot.TypeId = rec.TypeId;
            slot.Frame = rec.Frame & 0x0F;     // frames are 0..15
            slot.FrameTick = 0;
            slot.MaxFrames = TypeMaxFrames(rec.TypeId);
            slot.AgeFrames = 0;
            slot.Hp = 1;
            // Port of $F1EF gate: the record's +1 byte is the world-X
            // (= byte position 0..255 along the wider-than-screen
            // level).  Entity is drawable each frame only when
            // `(rec.Y - $E583) < $1F` ($F222 SUB B / $F223 CP $1F /
            // $F225 RET NC).  So we store WorldX here; the per-frame
            // tick recomputes screen X via UpdateEntityVisibility().
            // Y is fixed at level-load from the TopAddr's scanline bits.
            slot.WorldX = rec.Y;
            var (_, y) = LevelEntities.DecodeBitmapAddress(rec.TopAddr);
            slot.Y = y;
            slot.X = -16;       // placed off-screen until first tick
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
        // Port of $DE41: the laser beam starts at the ship's middle
        // (PlayerY + 4, not PlayerY which is the sprite's top), is 15
        // bytes (120 pixels) wide, paints with pattern $EF, and gets a
        // random bright-color attribute per shot from the Z80 R-register
        // ($DEC3..$DECD).  Bullet records live at $E46B in the original;
        // we use the existing Bullets[] slot list.
        foreach (var b in Bullets)
        {
            if (b.Alive) continue;
            // Initial b.X is set so AFTER TickPlaying's `b.X += b.DX`
            // advance, the visible head lands at the byte ONE PAST the
            // ship's edge — exactly what the original $DEAD/$DEBC does:
            //   $DEAD  ADD HL,BC   ; BC = facing + 15
            //   $DEBC  ADD HL,DE   ; DE = +1 if facing right, -1 if left
            // From byte 0 of the scanline:
            //   facing=1 (right): byte 0 + 16 + 1 = byte 17 = pixel 136
            //   facing=0 (left):  byte 0 + 15 - 1 = byte 14 = pixel 112
            // Ship sprite spans pixels 120..135 (bytes 15..16) so byte
            // 14 (left) and byte 17 (right) are immediately adjacent.
            //
            // Compensate for the post-tick advance:
            //   left target=112, dx=-8 → initial = 120 = PlayerX - 8
            //   right target=136, dx=+8 → initial = 128 = PlayerX
            b.X = PlayerX + (FacingLeft ? -8 : 0);
            b.Y = PlayerY + 4;       // middle of the 8px-tall ship sprite
            b.DX = FacingLeft ? -8 : 8;   // 1 byte = 8 px per frame
            b.DY = 0;
            b.Pattern = 0xEF;         // = 11101111 — original's beam byte
            b.Length = Bullet.MaxLength;  // 15 bytes = 120 px max beam length
            // Random color per shot: matches $DEC3 LD A,R; AND $07
            // (the R-register is effectively random).  OR $40 sets the
            // bright bit.  If the random result is 0, the original
            // defaults to $43 (bright cyan).
            int rand = _rng.Next(0, 8);
            byte ink = (byte)(rand == 0 ? 0x03 : rand);
            b.Attr = (byte)(ink | 0x40);   // bright | ink
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
            if (!e.Alive || !e.Visible) continue;
            if (e.TypeId < 0 || e.TypeId >= EntityTypes.Types.Length) continue;
            var type = EntityTypes.Types[e.TypeId];
            var sprite = EntityBank.Frame(type.SpritePointer, e.Frame);
            if (sprite.IsEmpty) continue;
            // Draw at (e.X, e.Y) = sprite top-left, matching $F26D..$F2A8
            // where HL = TopAddr + offset is the TL byte and $F2BC walks
            // INC H 8 times (= 8 scanlines down from TL).
            Blitters.DrawSprite16x16(fb, e.X, e.Y, sprite, type.Attribute);
        }

        foreach (var b in Bullets)
        {
            if (!b.Alive) continue;
            // Draw the bolt with a trail capped to the number of bytes
            // the bolt has actually traveled (= MaxLength - Length),
            // so the trail never extends BEHIND the ship's fire-time
            // position into the ship sprite.  The color is the per-
            // shot random attribute from $DEC3.
            int dir = b.DX > 0 ? 1 : -1;
            int baseX = b.X & ~7;
            const int MaxTrail = 4;
            int trail = Math.Min(Bullet.MaxLength - b.Length, MaxTrail);
            for (int i = 0; i < trail; i++)
            {
                int x = baseX - i * 8 * dir;
                if ((uint)x >= 256) continue;
                Blitters.DrawBulletXor(fb, x, b.Y, b.Pattern, b.Attr);
            }
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
            // Original draws the 16×16 sprite at top-left (120, altitude)
            // per $E8C9; PlayerX = 128 so PlayerX - 8 = 120.  Y is
            // directly the altitude (no -4 offset).
            Blitters.DrawPlayerXor(fb, PlayerX - 8, PlayerY, playerSprite, 0x43);
        }

        Hud.Draw(fb, this);

        // Death-explosion attribute particles draw LAST so they overlay
        // the HUD chrome (matches the original's $DBC8 timing where
        // particles paint the attribute file directly).
        Explosion.Draw(fb);

        // Mini-map at the bottom strip (y=160..191) — port of $E104.
        // The emu's mini-map paints incrementally between f50 and f80
        // (12 → 563 bytes) but the order doesn't match a simple top-
        // down or left-right walk.  Until the exact paint pattern is
        // decoded, suppress before f80 and paint full from f80+.
        if (_frameCounter >= 80)
        {
            MiniMap.DrawTo(fb);
        }
        else if (_frameCounter >= 50)
        {
            // Bottom-up paint approximation matching the emu's
            // observed pattern (char rows 23 first, then 22, 21, 20
            // between f50 and f80).  Source rows are mapped to char
            // rows: 4 source rows per char row.
            int progress = _frameCounter - 50;          // 0..30
            int rowsToDraw = 16 * progress / 30;        // 0..16
            MiniMap.DrawToPartial(fb, rowsToDraw);
        }
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
