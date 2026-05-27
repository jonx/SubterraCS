using System.Globalization;
using Subterra.Assets;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Bulk render of all candidate sprite cells in a memory region. Useful
/// while we don't yet know exact sprite-table addresses — just stride
/// through RAM, drop a sheet for every region, then eyeball the renders
/// folder for the ones that "look like" sprites.
/// </summary>
internal static class SpriteScanCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine(
                "usage: sprite-scan <file.z80> <fromHex> <toHex> <WxH[,WxH...]> [-cols=N] [-count=N]\n" +
                "  Render a sprite sheet starting at every 'WxH' shape, walking the\n" +
                "  range in <count>-cell chunks, into renders/. Lets us eyeball memory\n" +
                "  for sprite tables.\n" +
                "  Example:  sprite-scan SUBSTRYK.Z80 8000 EFFF 16x16,24x16");
            return 2;
        }
        // Accept either a .z80 snapshot or a raw 48K RAM dump (any
        // 0xC000-byte file is treated as RAM at $4000-$FFFF).
        byte[] ram;
        if (args[0].EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            ram = File.ReadAllBytes(args[0]);
            if (ram.Length != 0xC000)
            {
                Console.Error.WriteLine($"Raw RAM dump must be exactly {0xC000} bytes; got {ram.Length}.");
                return 1;
            }
        }
        else
        {
            ram = Z80SnapshotReader.Load(args[0]).Ram48K;
        }
        var snap = new Z80Snapshot(default, ram, Z80SnapshotKind.V1);
        ushort from = ushort.Parse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        ushort to = ushort.Parse(args[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var shapes = ParseShapes(args[3]);
        int cols = 16;
        int count = 32;
        int scale = 3;
        for (int i = 4; i < args.Length; i++)
        {
            if (args[i].StartsWith("-cols=", StringComparison.Ordinal))
                cols = int.Parse(args[i].Substring(6), CultureInfo.InvariantCulture);
            else if (args[i].StartsWith("-count=", StringComparison.Ordinal))
                count = int.Parse(args[i].Substring(7), CultureInfo.InvariantCulture);
            else if (args[i].StartsWith("-scale=", StringComparison.Ordinal))
                scale = int.Parse(args[i].Substring(7), CultureInfo.InvariantCulture);
        }

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        int sheetsWritten = 0;
        foreach (var (wBytes, h) in shapes)
        {
            int bytesPerCell = wBytes * h;
            int sheetBytes = bytesPerCell * count;
            for (int addr = from; addr + sheetBytes <= to + 1; addr += sheetBytes)
            {
                var slice = new byte[sheetBytes];
                Array.Copy(snap.Ram48K, addr - 0x4000, slice, 0, sheetBytes);
                var sheet = new SpriteSheet(slice, addr, wBytes, h, count);
                var rendered = sheet.RenderSheetRgba(
                    cols,
                    inkRgb: (0xFF, 0xFF, 0xFF),
                    paperRgb: (0x00, 0x00, 0x00),
                    gridRgb: (0x40, 0x40, 0x40)).UpscaleNearest(scale);
                var descriptor = string.Format(CultureInfo.InvariantCulture,
                    "scan-${0:X4}-{1}x{2}", addr, wBytes * 8, h);
                var path = RenderTarget.ForPng(repoRoot, descriptor);
                PngWriter.WriteRgba(path, rendered.Rgba, rendered.Width, rendered.Height);
                sheetsWritten++;
            }
        }
        Console.WriteLine($"Wrote {sheetsWritten} sheets into renders/.");
        return 0;
    }

    private static List<(int wBytes, int h)> ParseShapes(string spec)
    {
        var list = new List<(int, int)>();
        foreach (var s in spec.Split(','))
        {
            var parts = s.Split('x');
            if (parts.Length != 2)
                throw new ArgumentException($"Bad shape '{s}', expected like 16x16.");
            int wPixels = int.Parse(parts[0], CultureInfo.InvariantCulture);
            int h = int.Parse(parts[1], CultureInfo.InvariantCulture);
            if (wPixels % 8 != 0)
                throw new ArgumentException($"Width '{wPixels}' must be a multiple of 8.");
            list.Add((wPixels / 8, h));
        }
        return list;
    }
}
