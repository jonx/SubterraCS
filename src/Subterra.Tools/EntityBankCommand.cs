using System.Globalization;
using Subterra.Assets;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Render an entity-type sprite bank — 16 frames × 32 bytes each, in the
/// quadrant column-major layout the game uses — into a single PNG.
/// </summary>
internal static class EntityBankCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "usage: entity-bank <file.z80|file.bin> [hexAddr] [frames] [-cols=N] [-scale=N]\n" +
                "  Render <frames> 16x16 sprite frames starting at <hexAddr>\n" +
                "  in the column-major quadrant layout (32 bytes per frame).\n" +
                "  Defaults: hexAddr=B8F4, frames=16, cols=8, scale=4.\n" +
                "  Or 'all' as hexAddr to dump every type from the table at $F5A0.");
            return 2;
        }

        byte[] ram;
        if (args[0].EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            ram = File.ReadAllBytes(args[0]);
        }
        else
        {
            ram = Z80SnapshotReader.Load(args[0]).Ram48K;
        }
        if (ram.Length != 0xC000)
        {
            Console.Error.WriteLine($"RAM image must be 48 K (0xC000 bytes); got {ram.Length}.");
            return 1;
        }

        string addrSpec = args.Length >= 2 ? args[1] : "B8F4";
        int frames = args.Length >= 3
            ? int.Parse(args[2], CultureInfo.InvariantCulture) : 16;
        int cols = 8;
        int scale = 4;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].StartsWith("-cols=", StringComparison.Ordinal))
                cols = int.Parse(args[i].Substring(6), CultureInfo.InvariantCulture);
            else if (args[i].StartsWith("-scale=", StringComparison.Ordinal))
                scale = int.Parse(args[i].Substring(7), CultureInfo.InvariantCulture);
        }

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);

        if (addrSpec.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            // Walk every 4-byte entry at $F5A0 until we hit something
            // that doesn't look like a sprite-bank pointer.
            const int tableBase = 0xF5A0;
            for (int slot = 0; slot < 32; slot++)
            {
                int off = tableBase + slot * 4 - 0x4000;
                byte lo = ram[off];
                byte hi = ram[off + 1];
                ushort ptr = (ushort)((hi << 8) | lo);
                byte maxFrames = ram[off + 2];
                byte attr = ram[off + 3];
                if (ptr < 0x4000 || ptr + maxFrames * 32 > 0x10000)
                {
                    break;
                }
                Dump(ram, repoRoot, ptr, maxFrames, cols, scale, $"type{slot:D2}", attr);
                Console.WriteLine($"  type {slot:D2}: ptr=${ptr:X4}, frames={maxFrames}, attr=${attr:X2}");
            }
            return 0;
        }

        ushort baseAddr = ushort.Parse(addrSpec, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Dump(ram, repoRoot, baseAddr, frames, cols, scale, $"${baseAddr:X4}", null);
        return 0;
    }

    private static void Dump(
        byte[] ram, string repoRoot, ushort baseAddr, int frames,
        int cols, int scale, string label, byte? attr)
    {
        int totalBytes = frames * QuadrantSpriteRenderer.BytesPerSprite;
        int offset = baseAddr - 0x4000;
        var slice = ram.AsSpan(offset, totalBytes);

        // Pick an ink colour based on the type's attribute byte if given.
        (byte, byte, byte) inkRgb = (0xFF, 0xFF, 0xFF);
        if (attr is byte a)
        {
            int ink = a & 0x07;
            bool bright = (a & 0x40) != 0;
            var (r, g, b) = SpectrumScreen.Palette[(bright ? 8 : 0) + ink];
            inkRgb = (r, g, b);
        }

        var rendered = QuadrantSpriteRenderer.RenderBank(
            slice,
            cols,
            inkRgb,
            paperRgb: (0x10, 0x10, 0x10),
            gridRgb: (0x30, 0x30, 0x30));
        rendered = rendered.UpscaleNearest(scale);
        var descriptor = string.Format(CultureInfo.InvariantCulture,
            "entity-{0}", label.Replace("$", ""));
        var path = RenderTarget.ForPng(repoRoot, descriptor);
        PngWriter.WriteRgba(path, rendered.Rgba, rendered.Width, rendered.Height);
        Console.WriteLine($"  → {path}");
    }
}
