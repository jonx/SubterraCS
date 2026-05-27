using System.Globalization;
using Subterra.Assets;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Render the player sprite as the game currently has it staged in
/// the buffer at <c>$E8A9</c>.  The player draw routine at
/// <c>$DCF5</c> uses 4 screen-address bytes stored at <c>$E8C9</c>
/// to lay out the 4 quadrants of a 16 × 16 sprite, but the *source*
/// bytes themselves are a flat 32-byte block in <c>$E8A9..$E8C8</c>
/// (column-major: top-left 8 bytes, top-right 8 bytes, bottom-left
/// 8 bytes, bottom-right 8 bytes).
/// </summary>
internal static class PlayerDumpCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: player-dump <file.bin|file.z80>");
            return 2;
        }
        byte[] ram = args[0].EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
            ? File.ReadAllBytes(args[0])
            : Z80SnapshotReader.Load(args[0]).Ram48K;
        if (ram.Length != 0xC000)
        {
            Console.Error.WriteLine("Need a 48 K RAM image.");
            return 1;
        }

        // Pull the 4 screen addresses out of $E8C9 — that tells us
        // exactly where the player is on screen right now.
        var addrs = new ushort[4];
        for (int i = 0; i < 4; i++)
        {
            byte lo = ram[0xE8C9 - 0x4000 + i * 2];
            byte hi = ram[0xE8C9 - 0x4000 + i * 2 + 1];
            addrs[i] = (ushort)((hi << 8) | lo);
        }
        Console.WriteLine("Player sprite screen addresses ($E8C9):");
        Console.WriteLine($"  TL ${addrs[0]:X4}   TR ${addrs[1]:X4}");
        Console.WriteLine($"  BL ${addrs[2]:X4}   BR ${addrs[3]:X4}");

        // 32-byte sprite buffer at $E8A9 — column-major quadrant layout
        // matching QuadrantSpriteRenderer.
        var sprite = ram.AsSpan(0xE8A9 - 0x4000, 32);
        var rgba = QuadrantSpriteRenderer.RenderRgba(
            sprite,
            inkRgb: (0xFF, 0xFF, 0xFF),
            paperRgb: (0x10, 0x10, 0x10));
        var rendered = new RenderedImage(rgba, 16, 16).UpscaleNearest(12);

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var outPath = RenderTarget.ForPng(repoRoot, "player-current");
        PngWriter.WriteRgba(outPath, rendered.Rgba, rendered.Width, rendered.Height);
        Console.WriteLine($"→ {outPath}");

        // Now render both directional frames from the source bank at
        // $E63B / $E64B side-by-side.  Each frame is 16 bytes = the
        // top half (TL + TR quadrants) of a 32-byte sprite — the
        // bottom half is intentionally blank, the ship is 16 × 8.
        DumpFrame(ram, 0xE63B, "player-frame-right", repoRoot);
        DumpFrame(ram, 0xE64B, "player-frame-left",  repoRoot);

        // Print the raw bytes of the live sprite for the curious.
        Console.WriteLine();
        Console.WriteLine("Live sprite bytes ($E8A9 — TL | TR | BL | BR):");
        for (int row = 0; row < 8; row++)
        {
            byte tl = sprite[row];
            byte tr = sprite[8 + row];
            byte bl = sprite[16 + row];
            byte br = sprite[24 + row];
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}{1}  {2}{3}",
                ToBin(tl), ToBin(tr), ToBin(bl), ToBin(br)));
        }
        return 0;
    }

    private static void DumpFrame(byte[] ram, int addr, string descriptor, string repoRoot)
    {
        var frame = new byte[32];
        Array.Copy(ram, addr - 0x4000, frame, 0, 16);
        // bottom half stays zero
        var rgba = QuadrantSpriteRenderer.RenderRgba(
            frame,
            inkRgb: (0xFF, 0x80, 0xFF),  // magenta-ish
            paperRgb: (0x10, 0x10, 0x10));
        var rendered = new RenderedImage(rgba, 16, 16).UpscaleNearest(12);
        var path = RenderTarget.ForPng(repoRoot, descriptor);
        PngWriter.WriteRgba(path, rendered.Rgba, rendered.Width, rendered.Height);
        Console.WriteLine($"→ {path}");
    }

    private static string ToBin(byte b)
    {
        var sb = new System.Text.StringBuilder(8);
        for (int i = 7; i >= 0; i--)
        {
            sb.Append((b & (1 << i)) != 0 ? '#' : '.');
        }
        return sb.ToString();
    }
}
