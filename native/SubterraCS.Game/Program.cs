using System.Globalization;
using SubterraCS.Core;

namespace SubterraCS.Game;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool headless = args.Contains("--headless");
        bool modern = args.Contains("--modern");
        int frames = 600;
        string keys = "";
        int? seed = null;
        int? startLevel = null;   // debug: jump straight into a level
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--frames=", StringComparison.Ordinal))
                frames = int.Parse(args[i].Substring(9), CultureInfo.InvariantCulture);
            else if (args[i].StartsWith("--keys=", StringComparison.Ordinal))
                keys = args[i].Substring(7);
            else if (args[i].StartsWith("--seed=", StringComparison.Ordinal))
                seed = int.Parse(args[i].Substring(7), CultureInfo.InvariantCulture);
            else if (args[i].StartsWith("--level=", StringComparison.Ordinal))
                startLevel = int.Parse(args[i].Substring(8), CultureInfo.InvariantCulture);
        }

        Console.WriteLine($"SubterraCS — native C# port (mode: {(headless ? "headless" : "SDL2")})");
        Assets assets;
        try
        {
            assets = Assets.LoadFromRepo();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load assets: {ex.Message}");
            return 1;
        }
        Console.WriteLine($"  loaded {assets.Tiles.TileCount} tiles, " +
                          $"{assets.EntityTypes.Types.Length} entity types, " +
                          $"music data: {assets.MusicData.Length} bytes.");

        var world = new World(
            assets.Tiles, assets.Udgs, assets.EntityBank, assets.EntityTypes,
            assets.PlayerSpriteRight, assets.PlayerSpriteLeft,
            seed: seed ?? Environment.TickCount)
        {
            // Historic (cassette-rules) by default; --modern or the H
            // key enables the port-only modernities.
            ModernMode = modern,
            SplashScr = assets.SplashScr,
            TitleMenuScr = assets.TitleMenuScr,
            RomFont = assets.RomFont,
            LevelEntities = assets.LevelEntities,
            MiniMap = assets.MiniMap,
            EnemyShipInitData = assets.EnemyShipInitData,
            WorkerScheduleData = assets.WorkerScheduleData,
            FuelStationData = assets.FuelStationData,
            LevelColourData = assets.LevelColourData,
            HallOfFame = HallOfFame.Load(
                Path.Combine(RenderTarget.FindRepoRoot(AppContext.BaseDirectory), "hiscores.cfg")),
        };

        // Pass music data + captured cassette SFX through to the runner.
        Sdl2Runner.MusicData = assets.MusicData;
        Sdl2Runner.SfxBank = assets.SfxBank;

        // Debug jump-to-level (skips splash/title) — used by the
        // headless harness to exercise procedural pages directly.
        if (startLevel is { } lvl) world.LoadLevel(lvl);

        if (headless)
        {
            return HeadlessTestRunner.Run(world, frames, keys);
        }
        return Sdl2Runner.Run(world);
    }
}
