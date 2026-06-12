using System.Globalization;
using Subterra.Spectrum;
using Subterra.Spectrum.Z80;

namespace Subterra.Tools;

/// <summary>
/// Boots the snapshot, runs N frames, then instruments the inner
/// tile-draw routine at <c>$DAF2</c> for one further frame: each
/// time PC hits $DAF2 we record the source address (the tile-bank
/// pointer the routine is about to read its tile from) and the
/// destination address (the screen byte it's about to write to).
/// </summary>
internal static class TileTraceCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: tile-trace <48k.rom> <file.z80> <frames-before-trace> [-keys=...]");
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

        // Now run ONE more frame, single-stepping, and watch for the
        // inner tile-draw routine entry point at $DAF2.  The routine's
        // first three documented instructions are:
        //   $DAF2  00          NOP
        //   $DAF3  6E          LD L,(HL)
        //   $DAF4  26 00       LD H,$00
        // — so at PC==$DAF3, HL points to the tile-index *table entry*
        // about to be dereferenced. We capture both that pointer and
        // the destination DE (screen address).
        ApplyKeys(sys, keys, frames);
        sys.Cpu.MaskableInterrupt();
        var tileHits = new List<(ushort tileIndexAddr, byte tileIndex, ushort screenAddr)>();
        var screenWrites = new SortedDictionary<int, int>();  // address bucket → count
        var pcHistogram = new Dictionary<int, int>();
        long target = sys.Cpu.Cycles + Spectrum48.TStatesPerFrame;
        ushort prevPC = sys.Cpu.PC;
        while (sys.Cpu.Cycles < target)
        {
            ushort pc = sys.Cpu.PC;
            if (pc == 0xDAF3)
            {
                ushort hl = sys.Cpu.HL;
                byte tileIndex = sys.ReadMemory(hl);
                ushort de = sys.Cpu.DE;
                tileHits.Add((hl, tileIndex, de));
            }
            // Count PC in 256-byte buckets to find hot regions.
            int bucket = pc & 0xFF00;
            pcHistogram[bucket] = pcHistogram.GetValueOrDefault(bucket) + 1;
            prevPC = pc;
            sys.Cpu.Step();
        }

        Console.WriteLine($"After {frames} frames PC={sys.Cpu.PC:X4}. Captured {tileHits.Count} $DAF2 hits during frame {frames}.");
        Console.WriteLine();
        Console.WriteLine("PC-bucket histogram (top 12 hottest 256-byte regions):");
        foreach (var (addr, count) in pcHistogram
            .OrderByDescending(kv => kv.Value).Take(12))
        {
            Console.WriteLine($"  ${addr:X4}-${addr + 0xFF:X4}: {count,6} steps");
        }

        if (tileHits.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Tile draws:");
            Console.WriteLine("idx     hl=tile-ptr   tile#   de=screen-addr");
            for (int i = 0; i < tileHits.Count; i++)
            {
                var (hl, tile, de) = tileHits[i];
                Console.WriteLine($" {i,3}    ${hl:X4}        ${tile:X2}     ${de:X4}");
            }
        }
        return 0;
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
            if (SpectrumKey.FromName(bits[1]) is { } key) sys.PressKey(key);
        }
    }
}
