using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// One-shot dump of every asset we've identified in the game's RAM
/// into <c>assets/extracted/</c>.  Run this once against a fresh
/// post-game RAM dump and the native port can <c>File.ReadAllBytes</c>
/// each piece directly.
/// </summary>
internal static class ExtractAllCommand
{
    private static readonly (int Addr, int Length, string Name)[] Pieces =
    {
        (0x5E88, 0x1000, "music-5e88.bin"),      // Follin tune (~4 KB)
        (0xB0F4, 0x0C00, "tiles-b0f4.bin"),       // master tile bank
        (0xB8F4, 0x1E00, "entity-banks-b8f4.bin"),// 16 entity types × 512 bytes
        (0xE62B, 0x00A8, "udgs-e62b.bin"),         // 21 cave UDGs
        (0xE63B, 0x0060, "player-e63b.bin"),       // player frames + effects
        (0xF5A0, 0x0040, "entity-types-f5a0.bin"), // type table
        (0xE56D, 0x000C, "level-spriteptr-e56d.bin"),
        (0xE57C, 0x0006, "level-speed-e57c.bin"),
        (0xE58B, 0x000C, "level-secondptr-e58b.bin"),
        (0xE69D, 0x00C0, "level-schedules-e69d.bin"),
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "usage: extract-all <ram.bin>\n" +
                "  Dumps every named asset into assets/extracted/.");
            return 2;
        }
        var ram = File.ReadAllBytes(args[0]);
        if (ram.Length != 0xC000)
        {
            Console.Error.WriteLine("Need a 48 K RAM image.");
            return 1;
        }
        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var outDir = Path.Combine(repoRoot, "assets", "extracted");
        Directory.CreateDirectory(outDir);

        foreach (var (addr, len, name) in Pieces)
        {
            int offset = addr - 0x4000;
            var slice = new byte[len];
            Array.Copy(ram, offset, slice, 0, len);
            var path = Path.Combine(outDir, name);
            File.WriteAllBytes(path, slice);
            Console.WriteLine($"  ${addr:X4} +{len,5}  →  {Path.GetRelativePath(repoRoot, path)}");
        }
        return 0;
    }
}
