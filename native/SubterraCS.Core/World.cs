namespace SubterraCS.Core;

public enum GameState
{
    Splash,     // Loading screen (SUBSTRYK.SCR from the cassette)
    Title,      // Title menu — SELECT CONTROL OPTION (1..4)
    HallOfFame, // Idle-title high-score screen (port of $FCDB)
    NameEntry,  // Port-only: type a name for a Hall of Fame insert
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
    private bool _levelPainted;

    /// <summary>Latched at draw time (port of <c>$DCF5</c>'s shadow-carry
    /// SCF at <c>$DD2A</c>): true if the player's last XOR-draw overlapped
    /// a non-zero screen byte.  Consumed (and cleared) by the next
    /// <see cref="TickPlaying"/> to fire the damage chain (port of
    /// <c>$DD3B CALL C,$DD4A</c>).  This is the cassette's PRIMARY
    /// collision trigger; the <c>$EB7A</c>/<c>$EDC0</c> address-match
    /// paths in <see cref="EnemyShipTable"/> are a backup.</summary>
    private bool _playerXorOverlap;

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
            case GameState.HallOfFame: TickHallOfFame(input); return;
            case GameState.NameEntry:  TickNameEntry(input); return;
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
            // Hall of Fame (port-only persistence on top of the
            // cassette's $FCDB table — the original had no writable
            // storage).  If the run places, ask for a name first;
            // otherwise straight to the game-over screen.
            if (HallOfFame.WouldPlace(Score) >= 0)
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

    private void TickLevelClear()
    {
        if (StateTicks >= 60)
        {
            LoadLevel(NextLevel(Depth));
        }
    }

    /// <summary>Compute the next level index.  The cassette's $F6F2
    /// (INC + CP $06 + XOR A) wraps level 5 back to 0 — but level 0's
    /// data is a BUG in the original: its record pointer ($F594[0] =
    /// $F2E8) sits 3 bytes before level 1's records ($F2EB), so its six
    /// 8-byte records are level 1's bytes read out of alignment (types
    /// $C0/$20/garbage, TopAddrs in ROM at $1102 or stray RAM at $A001
    /// — the cassette would draw garbage AND corrupt memory), and its
    /// $E56D scenery pointer targets $B0F4, the tile bank itself.
    /// Level 0 is unreachable in normal play ($F6F2 increments before
    /// the first playable level); only the 5→0 wrap exposes it.  The
    /// port deliberately deviates: wrap 5 → 1 so post-level-5 play
    /// cycles the real pages.  See docs/disasm/entities.md §Level 0.</summary>
    private static int NextLevel(int current) => (current + 1) > 5 ? 1 : current + 1;

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

        // ---- Player vs scenery — port of $DFAF ----
        // Probe the level tile at the player's world position.  If the
        // tile byte is $01 (= solid wall), die.  Uses the same $EB62
        // semantics as the ship AI scenery probe.
        if (MiniMap.Buffer.Length >= 4096)
        {
            int playerWorldByte = (ScrollOffsetX + 0x0F) & 0xFF;
            int playerRow = (PlayerY >> 3) & 0x0F;
            byte tile = MiniMap.Buffer[playerRow * 256 + playerWorldByte];
            if (tile == 0x01) { TriggerDeath(); return; }
        }

        // ---- Fuel-station pickup — port of $DFCD..$DFEB ----
        // Per-level station position stored at $E58B + level*2 = (X, Y).
        // If player world-X matches AND altitude is in [Y-1..Y], refill
        // fuel via the $E419 animation (we just snap to BarMax).
        if (FuelStationData.Length >= (Depth + 1) * 2)
        {
            byte stationX = FuelStationData[Depth * 2];
            byte stationY = FuelStationData[Depth * 2 + 1];
            int worldX = (ScrollOffsetX + 0x0F) & 0xFF;
            if (worldX == stationX
                && (Altitude == stationY || Altitude == stationY - 1)
                && Fuel < BarMax)
            {
                Fuel = BarMax;
                FuelAccum = 0xFF;
                Sfx.Trigger(SfxKind.Pickup);
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

        // Ambient fuel drain — port of $D86C: $E465-- every frame
        // (= 1 fuel unit per 256 frames ≈ 4.3 sec).
        FuelAccum = (FuelAccum - 1) & 0xFF;
        if (FuelAccum == 0xFF) Fuel = Math.Max(0, Fuel - 1);   // wrap = underflow

        // L-key extra drain — port of $D8D8..$D8EC: holding horizontal
        // drains FuelAccum by $20 per frame on top of the ambient.
        if (input.Horizontal)
        {
            FuelAccum -= 0x20;
            if (FuelAccum < 0)
            {
                FuelAccum &= 0xFF;
                Fuel = Math.Max(0, Fuel - 1);
            }
        }

        // Low-fuel + low-shield warning SFX — port of $D879 / $D88A
        // (random gate; play Sfx.Damage if value < $20).
        if (Fuel < 0x20 && _rng.Next(0, 256) == 0x7E)
            Sfx.Trigger(SfxKind.Damage);
        if (Shield < 0x20 && _rng.Next(0, 256) == 0x7E)
            Sfx.Trigger(SfxKind.Damage);

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
                // which runs immediately after $DB1A returns.
                if (Scroll.ScrollComplete) Explosion.TriggerSpawnIn(Scroll.LevelColour);
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
        EnemyShipTable.TickAi(ScrollOffsetX, playerByteX, PlayerY, EnemyShots, _rng, Depth, MiniMap.Buffer);  // $E920
        Boss.Tick(ScrollProgress, ScrollOffsetX, playerByteX, PlayerY, _rng);                  // $EC10

        // Workers — port of $EF08.  Tick returns # rescued this frame;
        // each rescue gives +50 score, RESCUED++; 8 → level cleared.
        int newRescues = Workers.Tick(ScrollOffsetX, PlayerY);
        if (newRescues > 0)
        {
            Score += newRescues * 50;
            Rescued += newRescues;
            Sfx.Trigger(SfxKind.Pickup);
            if (Workers.RemainingThisLevel == 0)
            {
                EnterState(GameState.LevelClear);
            }
        }

        // Enemy BULLETS — $ED01 per-frame tick.  Bullets are spawned by
        // ships above via $EBB2 (= EnemyShots.TrySpawnAt), not random.
        // The return value is the coord-overlap bitmask (port of
        // $DDAA's `JP C,$DBC8` instant-death detection).
        int deathHits = EnemyShipTable.LastTickHits
                      | EnemyShots.Tick(ScrollOffsetX, playerByteX, PlayerY, MiniMap.Buffer);

        // $DD4D per-frame DEATH walker (called from $E8FD at $E90C).
        // Walks ships ($E597) + bullets ($EE9E) with $DD8C / $DDAA and
        // jumps to $DBC8 (instant death) on any coord overlap.  Tight
        // window: entity_X ∈ {p, p-1}, |Y diff| < 8 (bullets: 0..7
        // below player only).  Honoured Invincible (respawn / level
        // grace) prevents instant-death just after respawn.  See
        // docs/disasm/damages.md.
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
            // Laser vs ENEMY SHIPS — port of $E9F0: in the cassette,
            // each ship's own blitter checks the screen bytes under it
            // for the beam pattern $EF before drawing; a match kills
            // the ship, awards the remaining alt-B counter ($0F = 15
            // points, $E95A) and runs an 8-particle explosion ($EDDB).
            // ($F958 also queues a "kill jingle" message — but the
            // message system is vestigial, nothing ever plays it; see
            // sound.md.  Our Explode tone below is port-only flavour.)
            // We approximate the pixel test with the beam-byte/ship-
            // byte overlap our projectile model exposes.
            int beamByte = (b.X >> 3);
            int beamWorldByte = (ScrollOffsetX + beamByte) & 0xFF;
            for (int i = 0; i < EnemyShipTable.Slots.Length; i++)
            {
                if (!EnemyShipTable.IsAlive(i)) continue;
                ref var ship = ref EnemyShipTable.Slots[i];
                if (ship.X == beamWorldByte && Math.Abs(ship.Y - b.Y) < 10)
                {
                    ship.Status = 0;     // dead
                    ship.Sub = 0x80;      // 128-frame respawn delay
                    b.Alive = false;
                    Score += 15;          // remaining alt-B ($E95A LD B,$0F)
                    KillBurst(((ship.X - ScrollOffsetX) & 0xFF) * 8, ship.Y);
                    if (_rng.Next(0, 2) == 0) Sfx.Trigger(SfxKind.Explode);  // port-only (cassette kill is silent)
                    break;
                }
            }
            if (!b.Alive) continue;

            // Laser vs BOSS — same $E9F0 mechanism, alt-B = $14: a
            // SINGLE beam contact kills it (score += 20), it
            // deactivates ($EC6C randomize + $EE7C=0) and can respawn
            // later; $EE83 counts spawns and ≥10 drops the
            // alternate-frame throttle (handled in BossEntity).
            if (Boss.Active
                && (Boss.X == beamWorldByte || Boss.X + 1 == beamWorldByte)
                && Math.Abs(Boss.Y - b.Y) < 12)
            {
                b.Alive = false;
                Score += 20;              // remaining alt-B ($EC53 LD B,$14)
                KillBurst(((Boss.X - ScrollOffsetX) & 0xFF) * 8, Boss.Y);
                if (_rng.Next(0, 2) == 0) Sfx.Trigger(SfxKind.Explode);
                Boss.Kill(_rng);          // deactivate + randomize, respawnable
                continue;
            }

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
        // Port-only state that must not survive a death: the sub-pixel
        // scroll offset (dying at SubPixelScroll=5 would otherwise leave
        // the whole playfield shifted 5 px forever after respawn) and
        // the latched XOR-overlap flag from the death frame (currently
        // also masked by the invincibility grace, but reset explicitly
        // so damage logic never sees a stale pre-death collision).
        SubPixelScroll = 0;
        _playerXorOverlap = false;
        _prevUp = _prevDown = _prevHorizontal = false;
        // Port of cassette's $F6EC CALL $DB1A + $F6EF JP $F6C7 → $E135:
        // every respawn re-runs the scenery slide-in AND the spawn-in
        // dot-converge animation.  Resetting Scroll triggers
        // TickPlaying's slide-in loop on the next tick; that loop
        // fires TriggerSpawnIn when the slide-in completes.
        Scroll.Reset();
        EnterState(GameState.Playing);
    }

    private void StartNewGame()
    {
        Lives = 5;
        Score = 0;
        Rescued = 0;
        LastRunRank = -1;
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
        // Per-level cave colour — port of $F706 LD A,(HL); LD ($E57B),A.
        // Cassette bytes for L0..L5: $07 $04 $03 $06 $02 $01 (white,
        // green, magenta, yellow, red, blue).
        if (LevelColourData.Length > 0)
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
        EnemyShipTable.LoadFromInit(EnemyShipInitData, level);
        Boss.Reset();
        Workers.LoadFromSchedule(WorkerScheduleData, level);
        // Spawn-in animation ($E135) is fired by TickPlaying on the
        // frame the slide-in completes — matches the cassette flow
        // $F731 (CALL $DB1A returns) → $F6C8 (CALL $E135).  See
        // docs/disasm/spawn-in.md.
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
    /// Note: the original's "level 0" record list is a misaligned-
    /// pointer bug (level 1's bytes read 3 bytes out of phase —
    /// see docs/disasm/entities.md §Level 0); NextLevel never routes
    /// here with level == 0, so only levels 1..5 are ever placed.
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
            case GameState.HallOfFame: DrawHallOfFame(fb); break;
            case GameState.NameEntry:  DrawNameEntry(fb); break;
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

            // Playfield-only draws: ship sprites + boss + workers + bullets.
            // Draw entities FIRST so the player's XOR-overlap probe (in
            // DrawPlayerXor below) catches collisions with their pixels.
            // The cassette's main loop calls $DCF5 (player draw) BEFORE
            // $E8FD (entities), but its bitmap retains last-frame entity
            // pixels — same net effect.  Since XOR is commutative the
            // final pixel state is identical regardless of order.
            EnemyShipTable.Draw(fb, ScrollOffsetX, Scroll.LevelColour);
            Boss.Draw(fb, ScrollOffsetX, EnemyShipTable.SpriteBanks, EnemyShipTable.Cycle);
            Workers.DrawPlayfield(fb, ScrollOffsetX);
            EnemyShots.Draw(fb, ScrollOffsetX);

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

        // Hud.Draw clears y=128..191 then paints HUD chrome + bars.
        Hud.Draw(fb, this);

        // Mini-map AFTER Hud.Draw (which clears y=128..191) — port of
        // $E104.  Paint the cave silhouette base, then ship/worker dots
        // on top so they're visible.
        if (_frameCounter >= 80)
        {
            MiniMap.DrawTo(fb);
        }
        else if (_frameCounter >= 50)
        {
            int progress = _frameCounter - 50;
            int rowsToDraw = 16 * progress / 30;
            MiniMap.DrawToPartial(fb, rowsToDraw);
        }
        EnemyShipTable.DrawMiniMapDots(fb, ScrollOffsetX);
        Workers.DrawMiniMapDots(fb);

        // Player mini-map dot — port of $E248 / $E25E (player position
        // → mini-map row 161+altitude/4, column = scroll+16).
        // OR-paint with bright cyan attribute so it stands out.
        {
            int playerMiniX = (ScrollOffsetX + 0x10) & 0xFF;
            int playerMiniY = 0xA1 + (PlayerY >> 2);
            if (playerMiniY >= 160 && playerMiniY < 192)
            {
                int addr = Framebuffer.BitmapAddress(playerMiniX, playerMiniY);
                byte bit = (byte)(0x80 >> (playerMiniX & 7));
                fb.Bitmap[addr] |= bit;
                fb.Attributes[Framebuffer.AttributeAddress(playerMiniX, playerMiniY)] = 0x45;  // bright cyan
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
