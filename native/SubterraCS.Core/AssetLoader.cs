namespace SubterraCS.Core;

/// <summary>
/// Locates and loads the asset bins that <c>subterra extract-all</c>
/// produced into <c>&lt;repo&gt;/assets/extracted/</c>.  The loader walks
/// up from the executable directory to find the repo root, so the
/// game runs identically whether launched via <c>dotnet run</c> or as
/// a published binary.
/// </summary>
public sealed class Assets
{
    public TileBank Tiles { get; }
    public UdgBank Udgs { get; }
    public EntityBank EntityBank { get; }
    public EntityTypeTable EntityTypes { get; }
    public byte[] PlayerSpriteRight { get; }
    public byte[] PlayerSpriteLeft { get; }
    public byte[] MusicData { get; }
    public SpawnSchedule[] OriginalLevelSchedules { get; }
    public byte[] SplashScr { get; }
    public byte[] TitleMenuScr { get; }
    public RomFont RomFont { get; }
    public LevelEntities LevelEntities { get; }
    public MiniMap MiniMap { get; }
    /// <summary>Per-level enemy-ship init data ($E48D, 6 × 32 bytes).
    /// Copied to $E597 at level-load by $E319.</summary>
    public byte[] EnemyShipInitData { get; }

    public Assets(string assetsDir)
    {
        Tiles = TileBank.Load(Path.Combine(assetsDir, "tiles-b0f4.bin"));
        Udgs  = UdgBank.Load(Path.Combine(assetsDir, "udgs-e62b.bin"));
        EntityBank = EntityBank.Load(Path.Combine(assetsDir, "entity-banks-b8f4.bin"));
        EntityTypes = EntityTypeTable.Load(Path.Combine(assetsDir, "entity-types-f5a0.bin"));

        var playerBytes = File.ReadAllBytes(Path.Combine(assetsDir, "player-e63b.bin"));
        // First 16 = right-facing, next 16 = left-facing, remaining = effects.
        PlayerSpriteRight = playerBytes[0..16];
        PlayerSpriteLeft = playerBytes[16..32];

        MusicData = File.ReadAllBytes(Path.Combine(assetsDir, "music-5e88.bin"));
        OriginalLevelSchedules = OriginalLevels.Load(Path.Combine(assetsDir, "level-schedules-e69d.bin"));

        // Static cassette screens — the painted loading splash and the
        // procedurally-drawn title menu.  These are captured from the
        // running emulator (one-shot, not regenerated per frame).
        // Per-level scenery is NOT captured — that's drawn at runtime.
        SplashScr   = LoadOptionalScr(assetsDir, "splash-scr.bin");
        TitleMenuScr = LoadOptionalScr(assetsDir, "title-menu-scr.bin");

        // Spectrum ROM font ($3D00..$3FFF, 768 bytes), used by the
        // original HUD-print path through RST 10.  Required for
        // byte-identical HUD text.
        RomFont = RomFont.Load(Path.Combine(assetsDir, "rom-font.bin"));

        // Per-level static entity placements ($F2E8+, indexed by
        // counts at $F2E2 and pointers at $F594).  6-byte header
        // + 6 × N × 8 bytes of records.
        LevelEntities = LevelEntities.Load(Path.Combine(assetsDir, "level-entities-f2e8.bin"));

        // Per-level mini-map source buffers (6 × 4 KB).  Each level's
        // buffer is the static packed asset the original game ships
        // in its $60F4 / $70F4 / etc. RAM regions — extracted directly
        // from the boot snapshot.
        MiniMap = MiniMap.LoadFromAsset(Path.Combine(assetsDir, "level-minimaps.bin"));

        // Per-level enemy-ship init data (6 × 32 bytes = 192).
        // Loaded into EnemyShipTable at level-load via $E319 port.
        EnemyShipInitData = File.ReadAllBytes(Path.Combine(assetsDir, "level-init-e48d.bin"));
    }

    private static byte[] LoadOptionalScr(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
    }

    public static Assets LoadFromRepo()
    {
        var root = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var dir = Path.Combine(root, "assets", "extracted");
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                $"Extracted asset directory not found at {dir}. " +
                "Run `subterra extract-all build/post-game.bin` from the " +
                "main solution to (re)generate it.");
        }
        return new Assets(dir);
    }
}
