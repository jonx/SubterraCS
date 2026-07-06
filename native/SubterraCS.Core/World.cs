namespace SubterraCS.Core;

public enum GameState
{
    Splash,     // Loading screen (SUBSTRYK.SCR from the cassette)
    Title,      // Title menu — SELECT CONTROL OPTION (1..4)
    HallOfFame, // Idle-title high-score screen (port of $FCDB)
    NameEntry,  // Modern-only: type a name for a Hall of Fame insert
    Playing,    // The actual game loop
    Dying,      // Brief death animation
    GameOver,   // Game-over screen — press FIRE to retry
}

/// <summary>
/// The whole game.  Architecture mirrors how the original is structured:
///
///   load assets → splash → title → game loop:
///       LoadLevel(n) → load ships/workers/entity records
///       per-frame:  input → scroll → entities → collisions
///       all 8 workers rescued AND altitude ≥ $75?  → +1000,
///           LoadLevel(n+1)   (port of the $F868 page-advance gate)
///       player dies?  → Dying → respawn or GameOver
///
/// Rescuing every worker only UNLOCKS the exit ($E77D[level] flag);
/// the player must still dive to the bottom of the playfield to
/// advance — exactly the cassette's progression rule.
/// </summary>
public sealed class World
{
    public const int MaxEntities = 16;
    /// <summary>4 beam slots — the cassette's laser table at $E46B
    /// holds exactly 4 records (laser.md).</summary>
    public const int MaxBullets  = 4;

    /// <summary>The historic/modern switch.  OFF (default) = historic:
    /// every rule is the cassette's, as reverse-engineered in
    /// docs/disasm/.  ON = the port-only modernities: procedural
    /// levels past depth 5, laser-vs-decor with scores, enemy-ship
    /// respawns, ambient fuel drain + fuel-death, respawn
    /// invincibility grace, low-fuel/low-shield warnings, in-game
    /// music, and Hall-of-Fame name entry.  The two always-accepted
    /// extras — the Shift pixel-precision moves and the N-key sound
    /// modes — are NOT gated by this flag.</summary>
    public bool ModernMode;
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
    /// <summary>Per-level enemy-ship init data ($E48D, 6 × 32 bytes).</summary>
    public byte[] EnemyShipInitData { get; set; } = Array.Empty<byte>();
    /// <summary>Per-level worker schedule raw ($E69D, 6 × 32 bytes).</summary>
    public byte[] WorkerScheduleData { get; set; } = Array.Empty<byte>();
    /// <summary>Per-level fuel-station positions ($E58B, 6 × 2 bytes).</summary>
    public byte[] FuelStationData { get; set; } = Array.Empty<byte>();
    /// <summary>Per-level cave-colour byte ($E57C..$E581).  Applied to
    /// <see cref="LevelScroll.LevelColour"/> at level-load — port of
    /// <c>$F706 LD A,(HL); LD ($E57B),A</c>.</summary>
    public byte[] LevelColourData { get; set; } = Array.Empty<byte>();
    public readonly LevelScroll Scroll = new();
    public readonly Explosion Explosion = new();
    public readonly EnemyBullets EnemyShots = new();
    /// <summary>STUB — enemy ships at $E597.  See EnemyShips.cs.</summary>
    public readonly EnemyShips EnemyShipTable = new();
    /// <summary>STUB — boss entity at $EE7D.  See BossEntity in EnemyShips.cs.</summary>
    public readonly BossEntity Boss = new();
    /// <summary>Workers at $E75D.  See WorkerSchedule.cs.</summary>
    public readonly WorkerSchedule Workers = new();

    /// <summary>Latched at draw time (port of <c>$DCF5</c>'s shadow-carry
    /// SCF at <c>$DD2A</c>): true if the player's last XOR-draw overlapped
    /// a non-zero screen byte.  Consumed (and cleared) by the next
    /// <see cref="TickPlaying"/> to fire the damage chain (port of
    /// <c>$DD3B CALL C,$DD4A</c>).  This is the cassette's PRIMARY
    /// collision trigger; the <c>$EB7A</c>/<c>$EDC0</c> address-match
    /// paths in <see cref="EnemyShipTable"/> are a backup.</summary>
    private bool _playerXorOverlap;

    // Levels 1..5 are the cassette pages.  In ModernMode, depth 6+ is
    // served by the procedural generator, which emits data in the SAME
    // formats the cassette assets use (mini-map tile buffer, worker
    // schedule, ship init block, station bytes) so every faithful
    // subsystem runs unchanged on generated pages.
    private readonly ProceduralGenerator _gen;
    public int Depth { get; private set; }  // current level (1-based for display)
    private GeneratedLevel? _genLevel;      // active generated page (depth ≥ 6)

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

    /// <summary>Port-only sub-byte scroll offset (0..7 pixels).  The
    /// cassette's $DA23/$DA62 only scroll in whole-byte (8 px) chunks,
    /// but the Shift precision modifier exposes 1-pixel-per-edge
    /// horizontal nudges.  Total world-pixel X = ScrollOffsetX * 8 +
    /// SubPixelScroll.  When SubPixelScroll crosses 0 or 8, the byte
    /// offset wraps and SubPixelScroll resets to the modulus.</summary>
    public int SubPixelScroll;

    /// <summary>$EE74 — scroll-progress counter (16-bit).  Incremented
    /// each frame by <c>$D827</c> at level-scaled step.  Boss at
    /// <c>$EC10</c> spawns when this reaches <c>$4A38</c>.</summary>
    public int ScrollProgress;

    /// <summary>Bar-fill animation override.  When >= 0 the HUD bar
    /// drawer uses this instead of <see cref="Shield"/>/<see cref="Fuel"/>.
    /// Port of the $E41B..$E446 fill loop in the original (48 iterations
    /// of +2 with a per-iter beep; takes ~50 frames at level-start).</summary>
    public int BarFillOverride = -1;
    /// <summary>Current $E41B fill value (-1 = no fill running).</summary>
    private int _barFill = -1;

    /// <summary>Port of $E419: reset both accumulators, then run the
    /// 48-step fill animation; both bars land at $5F when it ends.</summary>
    private void StartBarRefill()
    {
        HitAccum = 0xFF;
        FuelAccum = 0xFF;
        _barFill = 0;
        Sfx.Trigger(SfxKind.BarFill);
    }
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

    // Edge-detection for the Shift precision mode (port-only).  Holds
    // the previous frame's Up/Down/Horizontal state so we can fire the
    // single-step move only on a false→true transition.
    private bool _prevUp, _prevDown, _prevHorizontal;

    // Game flow --------------------------------------------------------
    public GameState State { get; private set; } = GameState.Splash;
    public int StateTicks { get; private set; }
    public readonly SfxQueue Sfx = new();

    public readonly EntityInstance[] Entities = new EntityInstance[MaxEntities];
    public readonly Bullet[] Bullets = new Bullet[MaxBullets];

    private readonly Random _rng;
    private int _frameCounter;
    public int Alive => Entities.Count(e => e.Alive);
    public int FrameCounter => _frameCounter;

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

        for (int i = 0; i < Entities.Length; i++) Entities[i] = new EntityInstance();
        for (int i = 0; i < Bullets.Length; i++)  Bullets[i] = new Bullet();
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
            case GameState.HallOfFame: TickHallOfFame(input); return;
            case GameState.NameEntry:  TickNameEntry(input); return;
            case GameState.GameOver:   TickGameOver(input); return;
            case GameState.Dying:      TickDying();        return;
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

    /// <summary>Control scheme picked on the title menu (1..5, 0 =
    /// FIRE-started).  Cosmetic in the port — host keys map directly
    /// to GameInput regardless — but recorded so the HUD/debug can
    /// show it and the menu behaves like the cassette's $F672 poll.</summary>
    public int SelectedControlScheme { get; private set; }

    /// <summary>Hall of Fame — loaded by Program.cs with a persistence
    /// path; defaults to the cassette's $FDF5/$FE0F table.</summary>
    public HallOfFame HallOfFame { get; set; } = HallOfFame.Load("");

    /// <summary>0-based rank the last finished run reached in the
    /// Hall of Fame, or -1.  Shown on the game-over screen.</summary>
    public int LastRunRank { get; private set; } = -1;

    private void TickTitle(GameInput input)
    {
        // Port of the cassette's title poll: $F672 reads keys 1..5
        // ($F7FE row) and starts the game with the matching scheme
        // from the $F741 table.  FIRE also accepted (port-friendly
        // default, used by the headless harness).
        if (StateTicks <= 10) return;
        if (input.MenuDigit is >= 1 and <= 5)
        {
            SelectedControlScheme = input.MenuDigit;
            StartNewGame();
        }
        else if (input.Fire)
        {
            SelectedControlScheme = 0;
            StartNewGame();
        }
        // Idle attract: after ~10 s show the HALL OF FAME, like the
        // cassette's $FCDB idle-title screen.
        else if (StateTicks > 500)
        {
            EnterState(GameState.HallOfFame);
        }
    }

    // ─── Name entry (port-only — see HallOfFame.cs) ─────────────────
    private char[] _nameChars = "PLAYER  ".ToCharArray();
    private int _nameCursor;
    private bool _nePrevUp, _nePrevDown, _nePrevLeft, _nePrevRight, _nePrevFire;
    private const string NameAlphabet = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>On-screen name entry for a Hall of Fame insert:
    /// Up/Down cycle the letter under the cursor, Left/Right move the
    /// cursor, Fire confirms.  All edge-triggered.</summary>
    private void TickNameEntry(GameInput input)
    {
        bool up    = input.Up    && !_nePrevUp;
        bool down  = input.Down  && !_nePrevDown;
        bool left  = input.Left  && !_nePrevLeft;
        bool right = input.Right && !_nePrevRight;
        bool fire  = input.Fire  && !_nePrevFire;
        _nePrevUp = input.Up; _nePrevDown = input.Down;
        _nePrevLeft = input.Left; _nePrevRight = input.Right;
        _nePrevFire = input.Fire;

        if (up || down)
        {
            int idx = NameAlphabet.IndexOf(_nameChars[_nameCursor]);
            if (idx < 0) idx = 0;
            idx = (idx + (up ? 1 : NameAlphabet.Length - 1)) % NameAlphabet.Length;
            _nameChars[_nameCursor] = NameAlphabet[idx];
        }
        else if (left && _nameCursor > 0) _nameCursor--;
        else if (right && _nameCursor < _nameChars.Length - 1) _nameCursor++;
        else if (fire && StateTicks > 10)
        {
            string name = new string(_nameChars).TrimEnd();
            if (name.Length == 0) name = "PLAYER";
            LastRunRank = HallOfFame.Submit(name, Score);
            EnterState(GameState.GameOver);
        }
    }

    private void DrawNameEntry(Framebuffer fb)
    {
        MiniFont.DrawCentered(fb, 32, "NEW HIGH SCORE", 0x46);
        MiniFont.DrawCentered(fb, 48, $"SCORE {Score:D5}", 0x47);
        MiniFont.DrawCentered(fb, 72, "ENTER YOUR NAME", 0x45);
        // The 8 name cells, centered; cursor cell highlighted.
        int x0 = (Framebuffer.Width - _nameChars.Length * 8) / 2;
        for (int i = 0; i < _nameChars.Length; i++)
        {
            byte attr = i == _nameCursor
                ? (byte)((StateTicks & 16) < 8 ? 0x68 : 0x47)   // blink: bright paper
                : (byte)0x47;
            MiniFont.Draw(fb, x0 + i * 8, 96, _nameChars[i].ToString(), attr);
        }
        MiniFont.DrawCentered(fb, 128, "Q/A LETTER  L/R MOVE", 0x44);
        MiniFont.DrawCentered(fb, 140, "FIRE TO CONFIRM", 0x44);
    }

    private void TickHallOfFame(GameInput input)
    {
        if (StateTicks <= 10) return;
        if (input.MenuDigit is >= 1 and <= 5)
        {
            SelectedControlScheme = input.MenuDigit;
            StartNewGame();
        }
        else if (input.Fire)
        {
            SelectedControlScheme = 0;
            StartNewGame();
        }
        // Cycle back to the menu after ~12 s (attract loop).
        else if (StateTicks > 600)
        {
            EnterState(GameState.Title);
        }
    }

    private void TickGameOver(GameInput input)
    {
        if (input.Fire && StateTicks > 25) StartNewGame();
    }

    /// <summary>Death phases — port of $DBC8, which runs FOUR 64-iter
    /// particle passes, then the $DC43 screen dim, then JP $D8A8.
    /// We run the explosion 4 times, then an 8-frame bitmap dim.</summary>
    private int _deathPasses;
    private int _dimFrames;

    private void TickDying()
    {
        // Phase 1: 4 × 64-frame particle passes (port of $DBC8's
        // outer LD B,$04 loop around $DBDA).
        if (_deathPasses < 4)
        {
            Explosion.Tick();
            if (!Explosion.Active)
            {
                _deathPasses++;
                if (_deathPasses < 4)
                    Explosion.Trigger(PlayerX, PlayerY, Scroll.LevelColour);
            }
            return;
        }
        // Phase 2: $DC43 screen dim — 8 passes of SRL over the
        // playfield bitmap (applied in DrawPlaying via _dimFrames).
        if (_dimFrames < 8) { _dimFrames++; return; }

        Explosion.Reset();
        if (Lives <= 0)
        {
            // Hall of Fame persistence + name entry are modern-only
            // (the cassette's $FCDB table is read-only; it had no
            // writable storage).  Historic mode goes straight to the
            // game-over screen.
            if (ModernMode && HallOfFame.WouldPlace(Score) >= 0)
            {
                _nameChars = "PLAYER  ".ToCharArray();
                _nameCursor = 0;
                EnterState(GameState.NameEntry);
            }
            else
            {
                LastRunRank = -1;
                EnterState(GameState.GameOver);
            }
            Sfx.Trigger(SfxKind.GameOver);
        }
        else
        {
            Respawn();
        }
    }

    /// <summary>Compute the next level index.  The cassette's $F6F2
    /// (INC + CP $06 + XOR A) wraps level 5 back to 0 — but level 0's
    /// data is a BUG in the original: its record pointer ($F594[0] =
    /// $F2E8) sits 3 bytes before level 1's records ($F2EB), so its six
    /// 8-byte records are level 1's bytes read out of alignment — the
    /// cassette would draw garbage AND corrupt memory.  Level 0 is
    /// unreachable in normal play; only the 5→0 wrap exposes it.
    /// Historic mode wraps 5 → 1 so post-level-5 play cycles the real
    /// pages (see docs/disasm/entities.md §Level 0).  Modern mode
    /// keeps counting — depth 6+ pages come from the procedural
    /// generator.</summary>
    private int NextLevel(int current)
    {
        if (ModernMode) return current + 1;
        return (current + 1) > 5 ? 1 : current + 1;
    }

    // ─── Playing-state tick ─────────────────────────────────────────

    private void TickPlaying(GameInput input)
    {
        // Advance the spawn-in/explosion particle animation if active.
        // (Death state runs its own TickDying tick; this catches the
        // level-start spawn-in.)
        Explosion.Tick();
        // ---- Scroll-progress counter — port of $D827 ----
        // Saturating increment by ((level + 3) >> 3) + 1.
        // At level 1: +1/frame; reaches $4A38 (boss trigger) in ~19000 frames.
        int step = (((Depth + 3) >> 3) + 1) & 0xFF;
        ScrollProgress = Math.Min(0xFFFF, ScrollProgress + step);

        // ---- Player vs scenery — port of $DFAF + $DFC5/$DFEE ----
        // The cassette probes the tile at BOTH ship columns (scroll+15
        // AND scroll+16) and only jumps to $DBC8 when BOTH probes
        // return $01 (the $DFEE re-check of the first probe).  A
        // single-column graze is survivable.
        if (MiniMap.Buffer.Length >= 4096)
        {
            int playerRow = (PlayerY >> 3) & 0x0F;
            byte tileL = MiniMap.Buffer[playerRow * 256 + ((ScrollOffsetX + 0x0F) & 0xFF)];
            byte tileR = MiniMap.Buffer[playerRow * 256 + ((ScrollOffsetX + 0x10) & 0xFF)];
            if (tileL == 0x01 && tileR == 0x01 && !Invincible) { TriggerDeath(); return; }
        }

        // ---- Fuel-station pickup — port of $DFCD..$DFEB ----
        // $DFD0 compares RAW ($E583) — the scroll cursor itself, not
        // the ship column — against the station X at $E589, and the
        // altitude against {stationY, stationY-1} ($DFD7..$DFE0).
        // Refill only fires when fuel < $5F ($DFE2 CP $5F; RET NC),
        // then JP $E419: the animated fill that resets BOTH
        // accumulators and refills BOTH bars.
        {
            byte stationX = 0xFF, stationY = 0xFF;
            if (_genLevel != null) { stationX = _genLevel.StationX; stationY = _genLevel.StationY; }
            else if (FuelStationData.Length >= (Depth + 1) * 2)
            {
                stationX = FuelStationData[Depth * 2];
                stationY = FuelStationData[Depth * 2 + 1];
            }
            if (ScrollOffsetX == stationX
                && (Altitude == stationY || Altitude == stationY - 1)
                && Fuel < BarMax && _barFill < 0)
            {
                StartBarRefill();
            }
        }

        // ---- Vertical movement ----
        // Port-only precision mode: holding Shift overrides the
        // cassette's $D95D acceleration ramp with edge-triggered
        // single-pixel steps — release+repress to step again.  Lets
        // the user nudge altitude 1 px at a time.  When Shift is NOT
        // held, the original $D95D behaviour applies: SpeedShift
        // accumulates per frame, delta = (SpeedShift >> 1) | 1.
        if (input.Shift)
        {
            bool upEdge   = input.Up   && !_prevUp;
            bool downEdge = input.Down && !_prevDown;
            if (upEdge)   { Altitude = Math.Max(0,    Altitude - 1); DirectionState &= ~2; }
            if (downEdge) { Altitude = Math.Min(0x78, Altitude + 1); DirectionState |= 2; }
            SpeedShift = 1;   // keep ramp idle so a Shift-release
                              // doesn't kick into mid-acceleration
        }
        else if (input.Up)
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
            // $D984 INC A; BIT 3,A; JR NZ — the cassette tests bit 3
            // of the INCREMENTED value, so the ramp caps at 7 (max
            // delta 3 px/frame), never 8.
            if (SpeedShift < 7) SpeedShift++;
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
            if (SpeedShift < 7) SpeedShift++;
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

        // Horizontal scroll:
        //   - Non-Shift: every frame Horizontal is held, advance
        //     ScrollOffsetX by 1 byte = 8 px (faithful $D9C8 → $DA23).
        //   - Shift: on each Horizontal press-edge, advance by 1 PIXEL
        //     by bumping SubPixelScroll (0..7).  When it wraps it bumps
        //     ScrollOffsetX by 1 byte.  The level is always painted
        //     byte-aligned; the sub-byte shift is applied in
        //     DrawPlaying as a post-process over the WHOLE playfield
        //     (after entities draw, before player) so workers / ships /
        //     bullets stay anchored to the cave instead of lagging by
        //     SubPixelScroll pixels.
        if (Fuel > 0 && MiniMap.Buffer.Length > 0)
        {
            int dir = FacingLeft ? -1 : 1;
            bool repaintLevel = false;
            if (input.Shift)
            {
                if (input.Horizontal && !_prevHorizontal)
                {
                    SubPixelScroll += dir;
                    if (SubPixelScroll >= 8)
                    {
                        SubPixelScroll -= 8;
                        ScrollOffsetX = (ScrollOffsetX + 1) & 0xFF;
                        repaintLevel = true;
                    }
                    else if (SubPixelScroll < 0)
                    {
                        SubPixelScroll += 8;
                        ScrollOffsetX = (ScrollOffsetX - 1) & 0xFF;
                        repaintLevel = true;
                    }
                    // Sub-pixel-only step: no level repaint needed;
                    // post-shift in DrawPlaying does the visible move.
                }
            }
            else if (input.Horizontal)
            {
                ScrollOffsetX = (ScrollOffsetX + dir) & 0xFF;
                repaintLevel = true;
            }
            if (repaintLevel)
            {
                Scroll.PaintLevelAtOffset(Tiles, MiniMap.Buffer, ScrollOffsetX);
            }
        }

        // Snapshot direction-key state for next frame's edge detection.
        _prevUp = input.Up;
        _prevDown = input.Down;
        _prevHorizontal = input.Horizontal;

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
        // Page-advance gate — port of $F868.  Original requires BOTH:
        //   altitude >= $75    (player at bottom of playfield)
        //   $E77D[level] != 0  (all 8 workers picked → level cleared)
        // We model the cleared flag as Workers.RemainingThisLevel == 0.
        // On pass: +1000 score and load next level (= $F6F2).
        if (Altitude >= 0x75 && Workers.RemainingThisLevel == 0)
        {
            Score += 1000;
            LoadLevel(NextLevel(Depth));
            return;
        }

        // L-key drain — port of $D8D8..$D8EC: holding horizontal
        // drains FuelAccum by $20 per frame.  This is the ONLY fuel
        // drain the cassette has ($E465/$E466 are untouched while L
        // is up), and empty fuel merely blocks the horizontal scroll
        // ($D9F3) — it does not kill.
        if (input.Horizontal)
        {
            FuelAccum -= 0x20;
            if (FuelAccum < 0)
            {
                FuelAccum &= 0xFF;
                Fuel = Math.Max(0, Fuel - 1);
            }
        }

        if (ModernMode)
        {
            // Modern-only fuel economy: ambient per-frame drain plus
            // fuel-0 = death, giving the endless mode a survival
            // pressure the cassette never had.  (No cassette
            // counterpart — see RE-LOG §66.)
            FuelAccum = (FuelAccum - 1) & 0xFF;
            if (FuelAccum == 0xFF) Fuel = Math.Max(0, Fuel - 1);
            if (Fuel <= 0) { TriggerDeath(); return; }

            // Modern-only low-fuel / low-shield warnings (the
            // cassette's $F8B4/$F8D8 warning messages have no
            // callers — sound.md).
            if (Fuel < 0x20 && _rng.Next(0, 256) == 0x7E)
                Sfx.Trigger(SfxKind.FuelLow);
            if (Shield < 0x20 && _rng.Next(0, 256) == 0x7E)
                Sfx.Trigger(SfxKind.ShieldLow);
        }

        // Bar-fill animation — port of $E419/$E41B: 48 iterations of
        // A += 2 with a beep each iter.  Runs on every level start and
        // respawn (the $E347 HUD-repaint chain ends in CALL $E419) and
        // on fuel-station pickup ($DFEB JP $E419).  Both bars land at
        // $5F and both accumulators reset when it completes.
        if (_barFill >= 0)
        {
            BarFillOverride = Math.Min(_barFill, BarMax);
            _barFill += 2;
            if (_barFill > BarMax + 2)
            {
                _barFill = -1;
                BarFillOverride = -1;
                Fuel = BarMax;
                Shield = BarMax;
                HitAccum = 0xFF;
                FuelAccum = 0xFF;
            }
        }

        // Level slide-in animation — port of $DB1A's 16-iteration
        // outer loop (scroll-up + paint-new-bottom-row).  Cassette
        // runs synchronously inside $F731 (level start) and $F6EC
        // (every respawn), so the user always sees the cave slide UP
        // before play begins.  We drive it off StateTicks (per-state
        // entry) so it restarts on every Playing-state entry —
        // matching the cassette's $F6F2 / $F6EC fall-through.
        // 16 rows over 60 frames = ~3.75 frames/row (the cadence the
        // emu's $DB1A produces given its inline sound delays).
        const int ScrollTotalFrames = 60;
        if (!Scroll.ScrollComplete && MiniMap.Buffer.Length > 0)
        {
            int targetSteps = Math.Min(LevelScroll.CharRows,
                (int)(StateTicks * LevelScroll.CharRows / ScrollTotalFrames) + 1);
            while (Scroll.ScrolledRows < targetSteps && !Scroll.ScrollComplete)
            {
                Scroll.ScrollOneStep(Tiles, MiniMap.Buffer);
                // On the iteration that completes the slide-in, fire
                // the spawn-in animation — port of $F6C8 CALL $E135
                // which runs immediately after $DB1A returns.  The
                // $E17F beeper loop inside $E135 is the spawnin.wav
                // capture.
                if (Scroll.ScrollComplete)
                {
                    Explosion.TriggerSpawnIn(Scroll.LevelColour);
                    Sfx.Trigger(SfxKind.SpawnIn);
                }
            }
        }

        // Advance every live entity's ANIMATION.  Full $F1EF disasm
        // (docs/disasm/entities.md): the cassette never moves System-A
        // entities — the frame byte (+2) is the only field ever
        // written back; all apparent motion is the 16-frame animation
        // inside a fixed 16×16 box.  Likewise there is NO coord-based
        // entity-vs-player damage in the original ($DD4D walks only
        // ships/bullets/boss): decor entities hurt the player solely
        // through the $DCF5 XOR pixel-overlap, which our
        // _playerXorOverlap path already implements.  An earlier port
        // had invented AABB touch-damage with per-type pickup rules
        // and ConsumedOnContact — removed for faithfulness; entities
        // are eternal like the cassette's records.
        foreach (var e in Entities)
        {
            if (!e.Alive) continue;
            EntityAI.Tick(e);
        }

        // Per-frame entity supercaller — port of $E8FD.
        // Order matches the cassette: ship AI → boss tick → bullet tick.
        // Mini-map ship dots ($E213) are drawn in DrawPlaying.
        int playerByteX = (ScrollOffsetX + 15) & 0xFF;
        EnemyShipTable.TickAi(ScrollOffsetX, playerByteX, PlayerY, EnemyShots, _rng, Depth,
                              MiniMap.Buffer, modernRespawn: ModernMode);  // $E920
        bool bossWasActive = Boss.Active;
        Boss.Tick(ScrollProgress, ScrollOffsetX, playerByteX, PlayerY, _rng);                  // $EC10
        // Boss-spawn alert — the cassette QUEUES its $F8F9 message
        // here ($EC26) but never plays it; the kind is silent in
        // faithful mode and maps to lost-bossalert.wav in Lost Sounds.
        if (!bossWasActive && Boss.Active) Sfx.Trigger(SfxKind.BossAlert);

        // Workers — port of $EF08.  Tick returns # rescued this frame;
        // each rescue gives +50 score, RESCUED++.  The 8th rescue sets
        // the level-cleared flag ($E77D[level], modelled as
        // RemainingThisLevel == 0) — which only UNLOCKS the $F868
        // page-advance gate above; the player must still dive to
        // altitude ≥ $75 to leave the level.  $F011 queues the
        // all-rescued jingle ($F922 — vestigial on the cassette).
        int newRescues = Workers.Tick(ScrollOffsetX, PlayerY);
        if (newRescues > 0)
        {
            Score += newRescues * 50;
            Rescued += newRescues;
            Sfx.Trigger(SfxKind.Pickup);
            if (Workers.RemainingThisLevel == 0)
                Sfx.Trigger(SfxKind.LevelUp);
        }

        // Enemy BULLETS — $ED01 per-frame tick.  Bullets are spawned by
        // ships above via $EBB2 (= EnemyShots.TrySpawnAt), not random.
        // The return value is the coord-overlap bitmask (port of
        // $DDAA's `JP C,$DBC8` instant-death detection).
        int deathHits = EnemyShipTable.LastTickHits
                      | EnemyShots.Tick(ScrollOffsetX, playerByteX, PlayerY, MiniMap.Buffer);

        // Boss-vs-player — port of $DD58..$DD62: when $EE7C is set the
        // walker tests the boss slot with the same $DD8C box as ships
        // (entity_X ∈ {p, p-1}, |ΔY| < 8) and jumps to $DBC8 on
        // overlap.  Touching the boss is instant death.
        if (Boss.Active)
        {
            int bdxBoss = (playerByteX - Boss.X) & 0xFF;
            if ((bdxBoss == 0 || bdxBoss == 1) && Math.Abs(Boss.Y - PlayerY) < 8)
                deathHits |= 0x100;
        }

        // $DD4D per-frame DEATH walker (called from $E8FD at $E90C).
        // Walks ships ($E597) + bullets ($EE9E) + boss with $DD8C /
        // $DDAA and jumps to $DBC8 (instant death) on any coord
        // overlap.  Invincible is a modern-only respawn grace; in
        // historic mode it is never set (the cassette has none —
        // damages.md).
        if (deathHits != 0 && !Invincible) { TriggerDeath(); return; }

        // PRIMARY damage trigger — port of $DCF5/$DD2A shadow-carry +
        // $DD3B CALL C,$DD4A → $DDC4.  Looser than the death walker:
        // any pixel-overlap of the player XOR-draw fires this, even
        // when the entity centre is well outside the $DD8C box.  Drain
        // accumulates; 4 hits → 1 shield notch.  See damages.md.
        if (_playerXorOverlap && !Invincible)
        {
            _playerXorOverlap = false;
            HitAccum -= 0x40;
            if (HitAccum < 0)
            {
                HitAccum &= 0xFF;
                Shield = Math.Max(0, Shield - 1);
            }
            Sfx.Trigger(SfxKind.Damage);
            if (Shield <= 0) { TriggerDeath(); return; }
        }
        else if (_playerXorOverlap)
        {
            // Invincible (respawn/level grace) → drop the flag without
            // applying damage, matching the cassette's $DDC4 entry
            // being a no-op when shield drain is suppressed.
            _playerXorOverlap = false;
        }

        // Lasers — port of $DE41 + $DEF0, faithful beam model:
        // 4 slots ($E46B), NO fire cooldown (a fire press is only
        // ignored when all 4 slots are alive), all beam bytes painted
        // at fire time (up to 15, self-limited at scenery per $DEDA),
        // then per-frame the ship-side TAIL byte recedes toward the
        // fixed head until nothing remains ($DEF0..$DF1B).
        if (input.Fire) FireBullet();
        foreach (var b in Bullets)
        {
            if (!b.Alive) continue;
            // Tail recede: one byte per frame; expire when the span
            // is exhausted.
            b.Length--;
            if (b.Length <= 0) { b.Alive = false; continue; }

            // Laser vs ENEMY SHIPS — port of $E9F0: each target's own
            // blitter finds the beam pattern $EF under it and dies.
            // Our equivalent: the ship's screen byte lies within the
            // beam's live span on a scanline the ship covers.  Kill
            // awards the remaining alt-B counter ($E95A LD B,$0F = 15)
            // and runs the 8-particle explosion ($EDDB).  The $F958
            // kill jingle the cassette queues is vestigial (never
            // played) — historic mode is silent; ModernMode keeps a
            // 50% Explode tone as flavour.
            for (int i = 0; i < EnemyShipTable.Slots.Length; i++)
            {
                if (!b.Alive) break;
                if (!EnemyShipTable.IsAlive(i)) continue;
                ref var ship = ref EnemyShipTable.Slots[i];
                int shipByte = (ship.X - ScrollOffsetX) & 0xFF;
                if (shipByte >= 0x20) continue;
                int dyShip = b.Y - ship.Y;
                if (dyShip < 0 || dyShip >= 8) continue;   // ship sprite = 8 rows
                if (!BeamCovers(b, shipByte)) continue;
                ship.Status = 0;
                ship.Sub = (byte)(ModernMode ? 0x80 : 0x00);  // modern: respawn timer
                Score += 15;
                KillBurst(shipByte * 8, ship.Y);
                if (ModernMode && _rng.Next(0, 2) == 0) Sfx.Trigger(SfxKind.Explode);
            }
            if (!b.Alive) continue;

            // Laser vs BOSS — same $E9F0 mechanism, alt-B = $14: one
            // beam contact kills (+20), the boss deactivates ($EC6C
            // randomize + $EE7C = 0) and can respawn; $EE83 counts
            // spawns and ≥ 10 drops the alternate-frame throttle.
            if (Boss.Active)
            {
                int bossByte = (Boss.X - ScrollOffsetX) & 0xFF;
                int dyBoss = b.Y - Boss.Y;
                if (bossByte < 0x20 && dyBoss >= 0 && dyBoss < 16
                    && (BeamCovers(b, bossByte) || BeamCovers(b, bossByte + 1)))
                {
                    Score += 20;
                    KillBurst(bossByte * 8, Boss.Y);
                    if (ModernMode && _rng.Next(0, 2) == 0) Sfx.Trigger(SfxKind.Explode);
                    Boss.Kill(_rng);
                    continue;
                }
            }

            // Laser vs DECOR — MODERN ONLY.  The cassette's System-A
            // records are eternal: $F2BC blits with no $EF beam check,
            // so the beam cannot hurt decor (entities.md,
            // collision-matrix.md).  The modern extension gives decor
            // HP + a score table so generated pages have shootables.
            if (ModernMode)
            {
                foreach (var e in Entities)
                {
                    if (!e.Alive || !e.Visible) continue;
                    if (EntityAI.IsBulletProof(e.TypeId)) continue;
                    int entByte = e.X >> 3;
                    int dyEnt = b.Y - e.Y;
                    if (dyEnt < 0 || dyEnt >= 16) continue;
                    if (!BeamCovers(b, entByte) && !BeamCovers(b, entByte + 1)) continue;
                    e.Hp--;
                    if (e.Hp <= 0)
                    {
                        e.Alive = false;
                        Score += EntityAI.ShootScore(e.TypeId);
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
    }

    /// <summary>True if the beam's LIVE span currently covers the
    /// given screen byte column.  The head (far end) is fixed at
    /// X + (Span-1) bytes from the anchor; the tail has receded
    /// (Span - Length) bytes from the anchor toward the head.</summary>
    private static bool BeamCovers(Bullet b, int screenByte)
    {
        int dir = b.DX > 0 ? 1 : -1;
        int anchor = b.X >> 3;
        int tail = anchor + (b.Span - b.Length) * dir;
        int head = anchor + (b.Span - 1) * dir;
        return dir > 0
            ? screenByte >= tail && screenByte <= head
            : screenByte <= tail && screenByte >= head;
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
        _deathPasses = 0;
        _dimFrames = 0;
        if (ModernMode) Sfx.Trigger(SfxKind.Explode);   // $DC43 has no OUT — cassette death is silent
        // Port of $DBC8: attribute-particle explosion at the player's
        // current screen position.  $DBF9 paints the particles with
        // the LEVEL COLOUR ($E57B) alternated with white.
        Explosion.Trigger(PlayerX, PlayerY, Scroll.LevelColour);
    }

    /// <summary>Port of <c>$EDDB</c> — the 8-particle burst fired when
    /// a ship or the boss dies to the laser (see laser.md §$E9F0).
    /// Reuses the Explosion particle system (the cassette's $EDDB uses
    /// the same paint/step/paint pattern family at a $EEC2 scratch).</summary>
    private void KillBurst(int screenX, int screenY)
    {
        // Don't clobber the spawn-in animation if one is running.
        if (Explosion.Spawning) return;
        Explosion.Trigger(screenX, screenY, Scroll.LevelColour);
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
        // Port of the cassette's post-death restore $F6D4..$F6EF:
        //   $E319 — reload the 7 ships from the $E48D init block
        //   $E29B — clear the bullet table AND deactivate the boss
        //   $E347 — repaint HUD chrome, ending in CALL $E419 (the
        //           bar refill: both accumulators + both bars → full)
        //   $DB1A — replay the scenery slide-in, then loop to $E135
        // System-A decor records are NOT touched — they are eternal.
        foreach (var b in Bullets) b.Alive = false;
        EnemyShipTable.LoadFromInit(EnemyShipInitData, Math.Min(Depth, 5), _genLevel?.ShipInit);
        Boss.Deactivate();
        EnemyShots.Reset();
        PlayerX = FixedPlayerX; Altitude = 0; SpeedShift = 1; DirectionState = 0;
        DirectionState |= 0; FacingLeft = false;   // $F6D4 LD A,$01 → facing right
        StartBarRefill();
        if (ModernMode) SetInvincible(100);   // modern-only respawn grace
        // Port-only state that must not survive a death: the sub-pixel
        // scroll offset and the latched XOR-overlap flag from the
        // death frame.
        SubPixelScroll = 0;
        _playerXorOverlap = false;
        _prevUp = _prevDown = _prevHorizontal = false;
        // $F6EC CALL $DB1A + $F6EF JP $F6C7 → $E135: every respawn
        // re-runs the scenery slide-in AND the spawn-in animation.
        Scroll.Reset();
        EnterState(GameState.Playing);
    }

    private void StartNewGame()
    {
        Lives = 5;
        Score = 0;
        Rescued = 0;
        LastRunRank = -1;
        Boss.Reset();   // $F616 XOR A; LD ($EE83),A — spawn count cleared at title
        // The original's $E587 starts at 0 but $F6F2 INC's it before
        // entering the first playable level — so the first level the
        // player sees uses index 1's records (10 entities, the clean
        // 8-byte stride at $F2EB).  Level 0's data is a misaligned-
        // pointer bug in the original and is never played; see
        // NextLevel + docs/disasm/entities.md §Level 0.
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
        // Depth 6+ only exists in ModernMode: the procedural generator
        // emits a full page in the cassette's own data formats.
        _genLevel = (ModernMode && level > 5) ? _gen.Generate(level) : null;
        foreach (var e in Entities) e.Alive = false;
        foreach (var b in Bullets) b.Alive = false;
        PlayerX = FixedPlayerX; Altitude = 0; SpeedShift = 1; DirectionState = 0;
        // $E347 → $E419: level entry refills BOTH bars via the 48-step
        // fill animation (no partial carry-over).
        StartBarRefill();
        if (ModernMode) SetInvincible(60);   // modern-only level grace
        // Switch the active mini-map buffer to this level's packed
        // bytes (port of the original's $E579 ← $E56D[level*2] step).
        if (_genLevel != null) MiniMap.InstallBuffer(_genLevel.MiniMapBuffer);
        else MiniMap.SelectLevel(level);
        // Per-level cave colour — port of $F706 LD A,(HL); LD ($E57B),A.
        // Cassette bytes for L0..L5: $07 $04 $03 $06 $02 $01 (white,
        // green, magenta, yellow, red, blue).
        if (_genLevel != null)
            Scroll.LevelColour = _genLevel.LevelColour;
        else if (LevelColourData.Length > 0)
            Scroll.LevelColour = LevelColourData[level % LevelColourData.Length];
        Scroll.Reset();
        ScrollOffsetX = 0;
        SubPixelScroll = 0;
        ScrollProgress = 0;
        // Same state-boundary hygiene as Respawn: drop any latched
        // collision flag / edge-detection state from the previous level.
        _playerXorOverlap = false;
        _prevUp = _prevDown = _prevHorizontal = false;
        EnemyShots.Reset();
        // Port of $E319's LDIR from $E48D + level*32 → $E597.  Loads
        // the 7 ships' (X, Y, status, sub) into the live table.
        EnemyShipTable.LoadFromInit(EnemyShipInitData, Math.Min(level, 5), _genLevel?.ShipInit);
        Boss.ResetForLevel();
        Workers.LoadFromSchedule(_genLevel?.WorkerSchedule ?? WorkerScheduleData,
                                 _genLevel != null ? 0 : level);
        // Spawn-in animation ($E135) is fired by TickPlaying on the
        // frame the slide-in completes — matches the cassette flow
        // $F731 (CALL $DB1A returns) → $F6C8 (CALL $E135).  See
        // docs/disasm/spawn-in.md.
        PlaceEntitiesForLevel(level);
        EnterState(GameState.Playing);
    }

    /// <summary>
    /// Static placement of the System-A entity records for this level —
    /// the per-level records from <c>$F2E8</c> (RE-LOG §30), loaded
    /// once by <c>$F1BC</c> and eternal thereafter (never expired,
    /// never consumed — entities.md).  Depth 6+ uses the generated
    /// record list instead.
    /// </summary>
    private void PlaceEntitiesForLevel(int level)
    {
        if (_genLevel != null)
        {
            foreach (var rec in _genLevel.Decor)
            {
                var slot = NextFreeEntity();
                if (slot is null) break;
                slot.TypeId = rec.TypeId;
                slot.Frame = 0;
                slot.FrameTick = 0;
                slot.MaxFrames = TypeMaxFrames(rec.TypeId);
                slot.AgeFrames = 0;
                slot.Hp = rec.Hp;
                slot.WorldX = rec.WorldX;
                slot.Y = rec.Y;
                slot.X = -16;
                slot.DX = 0; slot.DY = 0;
                slot.Alive = true;
            }
            return;
        }
        if (LevelEntities is null || level >= LevelEntities.Levels.Length) return;

        var records = LevelEntities.Levels[level];
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
            // $F225 RET NC).  Y is fixed at level-load from the
            // TopAddr's scanline bits.
            slot.WorldX = rec.Y;
            var (_, y) = LevelEntities.DecodeBitmapAddress(rec.TopAddr);
            slot.Y = y;
            slot.X = -16;       // placed off-screen until first tick
            slot.DX = (rec.Flags & 0x40) != 0 ? -1 : 1;
            slot.DY = 0;
            slot.Alive = true;
        }
    }

    private void EnterState(GameState s)
    {
        State = s;
        StateTicks = 0;
    }

    private int TypeMaxFrames(int typeId)
    {
        if (typeId >= 0 && typeId < EntityTypes.Types.Length)
            return EntityTypes.Types[typeId].MaxFrames;
        return 8;
    }

    private EntityInstance? NextFreeEntity()
    {
        foreach (var e in Entities) if (!e.Alive) return e;
        return null;
    }

    private void FireBullet()
    {
        // Port of $DE41: a fire press is only ignored when all 4 beam
        // slots ($E46B) are alive — there is no cooldown.  The beam
        // starts at the byte adjacent to the ship's edge (byte 17
        // facing right, byte 14 facing left — $DEAD/$DEBC), extends up
        // to 15 bytes ($DED4 LD B,$0F), paints pattern $EF, and
        // SELF-LIMITS at scenery: $DEDA's INC(HL)/DEC(HL) probe bails
        // at the first non-empty screen byte.
        foreach (var b in Bullets)
        {
            if (b.Alive) continue;
            int dir = FacingLeft ? -1 : 1;
            int anchor = FacingLeft ? 14 : 17;
            b.X = anchor * 8;
            b.Y = PlayerY + 4;       // middle of the 8px-tall ship sprite
            b.DX = dir * 8;
            b.DY = 0;
            b.Pattern = 0xEF;         // = 11101111 — original's beam byte
            // Walk outward, clipping at the screen edge and at solid
            // scenery (approximated by the level tile under each byte).
            int span = 0;
            int row = (b.Y >> 3) & 0x0F;
            for (int i = 0; i < Bullet.MaxLength; i++)
            {
                int sb = anchor + i * dir;
                if ((uint)sb >= 32) break;
                if (MiniMap.Buffer.Length >= 4096
                    && MiniMap.Buffer[row * 256 + ((ScrollOffsetX + sb) & 0xFF)] != 0) break;
                span++;
            }
            if (span == 0) return;    // muzzle against a wall — no beam
            b.Span = span;
            b.Length = span;
            // Random colour per shot — port of $DEC3..$DECD:
            // LD A,R; AND $07; JR NZ; LD A,$43 (cyan default when the
            // masked value is 0); OR $40 (bright).  Verified by disasm.
            int rand = _rng.Next(0, 8);
            byte ink = (byte)(rand == 0 ? 0x03 : rand);
            b.Attr = (byte)(ink | 0x40);   // bright | ink
            b.Alive = true;
            if (ModernMode) Sfx.Trigger(SfxKind.Fire);   // cassette fire is silent
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
            case GameState.HallOfFame: DrawHallOfFame(fb); break;
            case GameState.NameEntry:  DrawNameEntry(fb); break;
            case GameState.GameOver:   DrawGameOver(fb); break;
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

    /// <summary>Shift every scanline of the playfield (y=0..127) left
    /// by <paramref name="subPx"/> pixels (0..7).  Used by the Shift
    /// precision modifier so the visible cave + entities slide by
    /// 1 pixel per Shift+Horizontal edge.  In-place left-to-right —
    /// each output byte = `(byte_at_col << subPx) | (byte_at_col+1 >> (8-subPx))`.
    /// Column 31 has no right neighbour; bits 0..subPx-1 fill with 0.
    /// No-op when subPx is 0.</summary>
    private static void ApplyPlayfieldSubPixelShift(Framebuffer fb, int subPx)
    {
        subPx &= 7;
        if (subPx == 0) return;
        int rightShift = 8 - subPx;
        for (int y = 0; y < 128; y++)
        {
            for (int col = 0; col < 32; col++)
            {
                int addr = Framebuffer.BitmapAddress(col * 8, y);
                byte cur = fb.Bitmap[addr];
                byte next = col + 1 < 32
                    ? fb.Bitmap[Framebuffer.BitmapAddress((col + 1) * 8, y)]
                    : (byte)0;
                fb.Bitmap[addr] = (byte)((cur << subPx) | (next >> rightShift));
            }
        }
    }

    private void DrawPlaying(Framebuffer fb)
    {
        DrawLevelScenery(fb);

        // Blit the persistent play-area bitmap (painted at level-load
        // by Scroll.PaintLevel — port of $DB1A) into the framebuffer.
        Scroll.Blit(fb);

        // Entities / workers / ships / bullets are NOT drawn during the
        // level slide-in.  Cassette sequence per spawn-in.md:
        //   $DB1A scenery slide-in  → scenery-only on screen
        //   $E135 spawn-in dots     → scenery + dots
        //   $D7F7 main loop         → scenery + ship + entities
        // The first iteration of $D7FB ($D80A CALL $F1A5) is what
        // first draws entities.  Until then, only scenery is visible.
        if (Scroll.ScrollComplete)
        {
            bool levelCleared = Workers.RemainingThisLevel == 0;
            foreach (var e in Entities)
            {
                if (!e.Alive || !e.Visible) continue;
                if (e.TypeId < 0 || e.TypeId >= EntityTypes.Types.Length) continue;
                // Electric arc (type $12 = 18) blocks the door UNTIL all
                // workers are rescued — port of $F252 CP $07 chain: arc
                // sprite swaps to "off" state when $E77D[level] bit 0 is
                // set.  We just hide it entirely once cleared.
                if (e.TypeId == 0x12 && levelCleared) continue;
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
                // Faithful beam render — port of $DEF0's erase/redraw:
                // paint every byte of the LIVE span (tail..head) with
                // pattern $EF and the per-shot random attribute.
                int dir = b.DX > 0 ? 1 : -1;
                int anchor = b.X >> 3;
                int tailByte = anchor + (b.Span - b.Length) * dir;
                for (int i = 0; i < b.Length; i++)
                {
                    int byteCol = tailByte + i * dir;
                    if ((uint)byteCol >= 32) continue;
                    Blitters.DrawBulletXor(fb, byteCol * 8, b.Y, b.Pattern, b.Attr);
                }
            }

            // Playfield-only draws: ship sprites + boss + workers + bullets.
            // Draw entities FIRST so the player's XOR-overlap probe (in
            // DrawPlayerXor below) catches collisions with their pixels.
            // The cassette's main loop calls $DCF5 (player draw) BEFORE
            // $E8FD (entities), but its bitmap retains last-frame entity
            // pixels — same net effect.  Since XOR is commutative the
            // final pixel state is identical regardless of order.
            EnemyShipTable.Draw(fb, ScrollOffsetX, Scroll.LevelColour);
            Boss.Draw(fb, ScrollOffsetX, Scroll.LevelColour);
            Workers.DrawPlayfield(fb, ScrollOffsetX, Scroll.LevelColour, _frameCounter);
            EnemyShots.Draw(fb, ScrollOffsetX, ModernMode);

            // Port-only sub-pixel scroll: now that level + entities are
            // all painted into the playfield, shift the WHOLE 256×128
            // top half left by SubPixelScroll pixels (0..7) so the
            // cave + workers + ships move together by the precision
            // amount.  Player is drawn AFTER this shift, so it stays
            // at fixed screen X=128.  Bug it fixes: previously the
            // level was painted with sub-byte composition but
            // entities were drawn byte-aligned, so workers appeared
            // to "move away" from the ship by SubPixelScroll pixels
            // each Shift+L press.
            //
            // Note on collision: the player's XOR-overlap probe (in
            // DrawPlayerXor below) reads the SHIFTED bitmap, so pixel
            // damage matches what the player visually overlaps — which
            // is the intended semantics for the precision mode.  The
            // coord-based death walker still uses world bytes
            // (ScrollOffsetX + 15) and ignores the sub-pixel part;
            // at worst that's a ±1 px disagreement, the same slack the
            // cassette's byte-granular $DD8C window already has.
            ApplyPlayfieldSubPixelShift(fb, SubPixelScroll);
        }

        bool hidePlayer = State == GameState.Dying
                          || (Invincible && (_frameCounter & 2) == 0)
                          // Match the emu: the player is not drawn
                          // until the level paint completes.  Verified
                          // by sampling f60..f400 in the emu — first
                          // visible byte at f232, right after $DB1A's
                          // 16 outer iterations finish.
                          || !Scroll.ScrollComplete
                          // Port of cassette flow $F6C7..$F6D1: the
                          // ship sprite isn't drawn until $E135
                          // spawn-in returns and the main loop starts
                          // at $D7F7.
                          || Explosion.Spawning;
        if (!hidePlayer)
        {
            var playerSprite = FacingLeft ? PlayerSpriteLeft : PlayerSpriteRight;
            // Original draws the 16×16 sprite at top-left (120, altitude)
            // per $E8C9; PlayerX = 128 so PlayerX - 8 = 120.  Y is
            // directly the altitude (no -4 offset).
            // Capture shadow-carry overlap flag — port of $DD2A SCF.
            // Consumed by next TickPlaying via _playerXorOverlap.
            if (Blitters.DrawPlayerXor(fb, PlayerX - 8, PlayerY, playerSprite, 0x43))
            {
                _playerXorOverlap = true;
            }
        }

        // $DC43 screen dim during the death sequence: repeated SRL
        // passes over the playfield bitmap (8 passes → black).
        if (State == GameState.Dying && _dimFrames > 0)
        {
            int shift = Math.Min(_dimFrames, 8);
            for (int y = 0; y < 128; y++)
                for (int col = 0; col < 32; col++)
                {
                    int addr = Framebuffer.BitmapAddress(col * 8, y);
                    fb.Bitmap[addr] = (byte)(fb.Bitmap[addr] >> shift);
                }
        }

        // Hud.Draw clears y=128..191 then paints HUD chrome + bars.
        Hud.Draw(fb, this);

        // Mini-map AFTER Hud.Draw (which clears y=128..191) — port of
        // $E104, which the $E347 HUD-repaint chain runs synchronously
        // on every level start and respawn.  The bitmap is persistent
        // on the cassette; we repaint per frame, same net effect.
        MiniMap.DrawTo(fb);
        EnemyShipTable.DrawMiniMapDots(fb, ScrollOffsetX);
        Workers.DrawMiniMapDots(fb, _frameCounter);

        // Player mini-map dot — port of $E248 / $E25E (player position
        // → mini-map row 161+altitude/4, column = scroll+16), XOR like
        // every $E1DE dot (no attribute override — the cassette leaves
        // the strip's attribute untouched).
        {
            int playerMiniX = (ScrollOffsetX + 0x10) & 0xFF;
            int playerMiniY = 0xA1 + (PlayerY >> 2);
            if (playerMiniY >= 160 && playerMiniY < 192)
            {
                int addr = Framebuffer.BitmapAddress(playerMiniX, playerMiniY);
                byte bit = (byte)(0x80 >> (playerMiniX & 7));
                fb.Bitmap[addr] ^= bit;
            }
        }

        // Death-explosion attribute particles draw LAST so they overlay
        // the HUD chrome (matches the original's $DBC8 timing where
        // particles paint the attribute file directly).
        Explosion.Draw(fb);
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
        // Playfield attribute strip — the PER-LEVEL colour from $E57B
        // ($DB1A paints attribute cells with it; $F706 loads it from
        // the $E57C table: $07 $04 $03 $06 $02 $01 for L0..L5).  The
        // old hard-coded $04 was a level-1-only observation (RE-LOG
        // §24 peeked level 1, whose colour happens to be green).
        for (int row = 0; row < HudCharRow; row++)
        {
            for (int col = 0; col < 32; col++)
            {
                fb.Attributes[row * 32 + col] = Scroll.LevelColour;
            }
        }
    }

    private const int HudCharRow = 16;

    private void DrawGameOver(Framebuffer fb)
    {
        DrawPlaying(fb);
        for (int i = 0; i < fb.Attributes.Length; i++) fb.Attributes[i] = 0x07;
        MiniFont.DrawCentered(fb, 64, "GAME OVER", 0x42);
        MiniFont.DrawCentered(fb, 80, $"LEVEL {Depth}  SCORE {Score:D5}", 0x46);
        MiniFont.DrawCentered(fb, 88, $"RESCUED {Rescued:D2}", 0x44);
        if (LastRunRank >= 0)
        {
            MiniFont.DrawCentered(fb, 104, $"HALL OF FAME RANK {LastRunRank + 1}", 0x45);
        }
        if ((StateTicks & 16) < 8)
        {
            MiniFont.DrawCentered(fb, 128, "PRESS FIRE TO RETRY", 0x47);
        }
    }

    /// <summary>Port of the cassette's idle-title HALL OF FAME screen
    /// (`$FCDB` — see docs/disasm/title-menu.md), drawn with our
    /// MiniFont instead of the ROM print stream.  Letter-spaced
    /// header like the original's "S U B T E R R A N E A N".</summary>
    private void DrawHallOfFame(Framebuffer fb)
    {
        MiniFont.DrawCentered(fb, 16, "S U B T E R R A N E A N", 0x47);
        MiniFont.DrawCentered(fb, 26, "S T R Y K E R", 0x47);
        MiniFont.DrawCentered(fb, 44, "- HALL  OF  FAME -", 0x46);
        for (int i = 0; i < HallOfFame.Table.Count; i++)
        {
            var e = HallOfFame.Table[i];
            // Alternate the entry colour like the cassette's menu rows.
            byte attr = (i & 1) == 0 ? (byte)0x45 : (byte)0x44;
            MiniFont.DrawCentered(fb, 64 + i * 10, $"{e.Name,-8} {e.Score,5}", attr);
        }
        if ((StateTicks & 16) < 8)
        {
            MiniFont.DrawCentered(fb, 156, "PRESS FIRE OR 1-5 TO PLAY", 0x47);
        }
    }
}
