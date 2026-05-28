using System.Globalization;
using SubterraCS.Core;

namespace SubterraCS.Game;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool headless = args.Contains("--headless");
        int frames = 600;
        string keys = "";
        int? seed = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--frames=", StringComparison.Ordinal))
                frames = int.Parse(args[i].Substring(9), CultureInfo.InvariantCulture);
            else if (args[i].StartsWith("--keys=", StringComparison.Ordinal))
                keys = args[i].Substring(7);
            else if (args[i].StartsWith("--seed=", StringComparison.Ordinal))
                seed = int.Parse(args[i].Substring(7), CultureInfo.InvariantCulture);
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
            originalLevels: assets.OriginalLevelSchedules,
            seed: seed ?? Environment.TickCount)
        {
            SplashScr = assets.SplashScr,
            TitleMenuScr = assets.TitleMenuScr,
        };

        // Pass music data through to whichever runner uses it.
        Sdl2Runner.MusicData = assets.MusicData;

        if (headless)
        {
            return HeadlessTestRunner.Run(world, frames, keys);
        }
        return Sdl2Runner.Run(world);
    }
}
