using System.Globalization;
using SubterraCS.Core;

namespace SubterraCS.Game;

/// <summary>
/// Runs the native game loop *without* opening a window — instead it
/// drops a PNG of the framebuffer into the repo's <c>renders/</c>
/// directory every few frames.  Mirrors the convention from the main
/// solution so the visual changelog stays continuous across both
/// implementations.
/// </summary>
internal static class HeadlessTestRunner
{
    public static int Run(World world, int frames, string keySpec)
    {
        var input = new GameInput();
        var fb = new Framebuffer();
        var root = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var schedule = ParseKeySpec(keySpec);
        Console.WriteLine($"  Running {frames} frames headless. Key schedule entries: {schedule.Count}.");

        // One timestamp shared by the whole run so the sequence sorts together.
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        int dropEvery = Math.Max(1, frames / 12);
        int snapshots = 0;

        for (int f = 0; f < frames; f++)
        {
            ApplyKeys(input, schedule, f);
            world.Tick(input);
            // Draw EVERY frame to match SDL2 cadence — the $DCF5
            // XOR-overlap collision flag latches at Draw time, so a
            // sparse Draw schedule would miss most damage events.
            world.Draw(fb);
            if (f == 0 || (f + 1) % dropEvery == 0 || f == frames - 1)
            {
                var rgba = fb.ToRgba();
                var dir = Path.Combine(root, "renders");
                Directory.CreateDirectory(dir);
                var name = string.Format(CultureInfo.InvariantCulture,
                    "native-headless-f{0:D5}_{1}.png", f + 1, stamp);
                var outPath = Path.Combine(dir, name);
                PngWriter.WriteRgba(outPath, rgba, Framebuffer.Width, Framebuffer.Height);
                // Also dump raw RGBA next to the PNG so external diff
                // tools can read the framebuffer without decoding PNG.
                File.WriteAllBytes(outPath + ".rgba", rgba);
                Console.WriteLine($"    frame {f + 1,5}: depth={world.Depth} score={world.Score} shield={world.Shield} fuel={world.Fuel} entities={world.Alive} spawned={world.Spawned}  →  {Path.GetRelativePath(root, outPath)}");
                snapshots++;
            }
        }
        Console.WriteLine($"Wrote {snapshots} render(s).");
        return 0;
    }

    private static List<(int Start, int End, string Key)> ParseKeySpec(string spec)
    {
        var list = new List<(int, int, string)>();
        if (string.IsNullOrEmpty(spec)) return list;
        foreach (var part in spec.Split(','))
        {
            var bits = part.Split(':');
            if (bits.Length != 2) continue;
            string range = bits[0];
            int dash = range.IndexOf('-');
            int start, end;
            if (dash > 0)
            {
                if (!int.TryParse(range[..dash], out start) || !int.TryParse(range[(dash + 1)..], out end)) continue;
            }
            else
            {
                if (!int.TryParse(range, out start)) continue;
                end = start;
            }
            list.Add((start, end, bits[1].ToUpperInvariant()));
        }
        return list;
    }

    private static void ApplyKeys(GameInput input, List<(int Start, int End, string Key)> schedule, int frame)
    {
        input.Up = input.Down = input.Horizontal = input.Left = input.Right = input.Fire = input.Shift = false;
        foreach (var (s, e, k) in schedule)
        {
            if (frame < s || frame > e) continue;
            switch (k)
            {
                case "Q":     case "UP":    input.Up = true; break;
                case "A":     case "DOWN":  input.Down = true; break;
                case "L":                   input.Horizontal = true; break;
                case "LEFT":                input.Left = true; input.Horizontal = true; break;
                case "RIGHT":               input.Right = true; input.Horizontal = true; break;
                case "FIRE":  case "ENTER": case "SPACE": input.Fire = true; break;
                case "SHIFT":               input.Shift = true; break;
            }
        }
    }
}
