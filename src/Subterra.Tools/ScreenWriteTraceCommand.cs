using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Boot the snapshot, run N frames, then trace every write into
/// Spectrum bitmap memory (\$4000-\$57FF) and attribute memory
/// (\$5800-\$5AFF) during one further frame.  Reports the top
/// "hot PCs" — the addresses of the game-code instructions that
/// did most of the writing.
/// </summary>
internal static class ScreenWriteTraceCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: scrwrite-trace <48k.rom> <file.z80> <frames-before-trace> [-keys=...]");
            return 2;
        }
        var rom = File.ReadAllBytes(args[0]);
        var snap = Z80SnapshotReader.Load(args[1]);
        int frames = int.Parse(args[2], CultureInfo.InvariantCulture);
        string keys = "";
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].StartsWith("-keys=", StringComparison.Ordinal))
                keys = args[i].Substring("-keys=".Length);
        }

        var sys = new Spectrum48(rom);
        sys.LoadSnapshot(snap);
        for (int f = 0; f < frames; f++)
        {
            ApplyKeys(sys, keys, f);
            sys.RunFrame();
        }
        ApplyKeys(sys, keys, frames);

        var bitmapHits = new Dictionary<ushort, int>();
        var attrHits = new Dictionary<ushort, int>();
        var writtenBitmapAddresses = new SortedSet<ushort>();
        var writtenAttrAddresses = new SortedSet<ushort>();
        var bitmapTrace = new List<(ushort pc, ushort addr, byte oldVal, byte newVal)>();
        sys.MemoryWritten += (addr, value) =>
        {
            ushort pc = sys.Cpu.PC;
            if (addr < 0x5800)
            {
                bitmapHits[pc] = bitmapHits.GetValueOrDefault(pc) + 1;
                writtenBitmapAddresses.Add(addr);
                // Capture the first ~40 writes in full detail so we can
                // hand-trace which sprite went to which screen byte.
                if (bitmapTrace.Count < 40)
                {
                    bitmapTrace.Add((pc, addr, 0, value));
                }
            }
            else if (addr < 0x5B00)
            {
                attrHits[pc] = attrHits.GetValueOrDefault(pc) + 1;
                writtenAttrAddresses.Add(addr);
            }
        };

        sys.Cpu.MaskableInterrupt();
        long target = sys.Cpu.Cycles + Spectrum48.TStatesPerFrame;
        while (sys.Cpu.Cycles < target)
        {
            sys.Cpu.Step();
        }

        Console.WriteLine($"After {frames} frames, traced 1 frame:");
        Console.WriteLine($"  bitmap writes: {bitmapHits.Values.Sum()} into {writtenBitmapAddresses.Count} distinct addresses");
        Console.WriteLine($"  attr writes:   {attrHits.Values.Sum()} into {writtenAttrAddresses.Count} distinct addresses");
        Console.WriteLine();
        Console.WriteLine("Hot bitmap-write PCs (top 15):");
        foreach (var (pc, c) in bitmapHits.OrderByDescending(kv => kv.Value).Take(15))
        {
            Console.WriteLine($"  PC=${pc:X4}  {c,6} writes");
        }
        if (writtenBitmapAddresses.Count > 0)
        {
            ushort lo = writtenBitmapAddresses.Min;
            ushort hi = writtenBitmapAddresses.Max;
            Console.WriteLine();
            Console.WriteLine($"Bitmap addresses touched: ${lo:X4}..${hi:X4}");
        }
        Console.WriteLine();
        Console.WriteLine($"First {bitmapTrace.Count} bitmap writes (chronological):");
        Console.WriteLine("  #    PC      screen   byte   sx,sy");
        for (int i = 0; i < bitmapTrace.Count; i++)
        {
            var (pc, addr, _, val) = bitmapTrace[i];
            var (sx, sy) = ScreenToXY(addr);
            Console.WriteLine($"  {i,3}  ${pc:X4}   ${addr:X4}    ${val:X2}    ({sx,3},{sy,3})");
        }
        return 0;
    }

    private static (int x, int y) ScreenToXY(ushort addr)
    {
        // Inverse of SpectrumScreen.BitmapAddress.  offset = addr - 0x4000
        // bits 12-11 = band, 10-8 = pixel row, 7-5 = char row, 4-0 = x byte.
        int o = addr - 0x4000;
        int band = (o >> 11) & 3;
        int pixelRow = (o >> 8) & 7;
        int charRow = (o >> 5) & 7;
        int col = o & 0x1F;
        int y = band * 64 + charRow * 8 + pixelRow;
        int x = col * 8;
        return (x, y);
    }

    private static void ApplyKeys(Spectrum48 sys, string keys, int frame)
    {
        sys.ReleaseAllKeys();
        if (string.IsNullOrEmpty(keys)) return;
        foreach (var part in keys.Split(','))
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
            if (frame < start || frame > end) continue;
            SpectrumKey? k = bits[1].ToUpperInvariant() switch
            {
                "SPACE" => SpectrumKey.Space, "ENTER" => SpectrumKey.Enter,
                "1" => SpectrumKey.D1, "2" => SpectrumKey.D2, "3" => SpectrumKey.D3, "4" => SpectrumKey.D4, "5" => SpectrumKey.D5,
                "Q" => SpectrumKey.Q, "A" => SpectrumKey.A, "L" => SpectrumKey.L,
                _ => null,
            };
            if (k is { } key) sys.PressKey(key);
        }
    }
}
