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
