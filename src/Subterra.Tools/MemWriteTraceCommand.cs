using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Trace every memory write into a target address range over a
/// specified frame window. Reports which PCs are doing the writing
/// (so you can disasm those PCs and understand the populating
/// routine), plus a histogram of which target bytes got touched.
///
/// Built specifically to identify the routine that populates the
/// mini-map buffer at <c>$60F4..$70F4</c> in Subterranean Stryker —
/// but useful for any "who writes to this region?" investigation.
///
/// Usage:
///   mem-write-trace &lt;48k.rom&gt; &lt;file.z80&gt; &lt;warm-frames&gt; &lt;trace-frames&gt;
///       &lt;startHex&gt; &lt;endHex&gt; [-keys=...]
///
/// Example:
///   mem-write-trace original/rom/48k.rom original/dumps/SUBSTRYK.Z80 \
///       50 50 60F4 70F4 -keys=5-10:SPACE,40-50:1
/// </summary>
internal static class MemWriteTraceCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 6)
        {
            Console.Error.WriteLine(
                "usage: mem-write-trace <48k.rom> <file.z80> <warm-frames> <trace-frames> <startHex> <endHex> [-keys=...]");
            return 2;
        }
        var rom = File.ReadAllBytes(args[0]);
        var snap = Z80SnapshotReader.Load(args[1]);
        int warmFrames = int.Parse(args[2], CultureInfo.InvariantCulture);
        int traceFrames = int.Parse(args[3], CultureInfo.InvariantCulture);
        ushort lo = ushort.Parse(args[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        ushort hi = ushort.Parse(args[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        string keys = "";
        for (int i = 6; i < args.Length; i++)
        {
            if (args[i].StartsWith("-keys=", StringComparison.Ordinal))
                keys = args[i].Substring("-keys=".Length);
        }

        var sys = new Spectrum48(rom);
        sys.LoadSnapshot(snap);

        // Warm-up: just run frames without tracing.
        for (int f = 0; f < warmFrames; f++)
        {
            ApplyKeys(sys, keys, f);
            sys.RunFrame();
        }

        // Now trace.
        var pcHits = new Dictionary<ushort, int>();
        var addrHits = new Dictionary<ushort, int>();
        var firstWritesPerAddr = new Dictionary<ushort, ushort>();   // addr → first PC
        int totalWrites = 0;
        sys.MemoryWritten += (addr, value) =>
        {
            if (addr < lo || addr > hi) return;
            totalWrites++;
            ushort pc = sys.Cpu.PC;
            pcHits[pc] = pcHits.GetValueOrDefault(pc) + 1;
            addrHits[addr] = addrHits.GetValueOrDefault(addr) + 1;
            if (!firstWritesPerAddr.ContainsKey(addr))
                firstWritesPerAddr[addr] = pc;
        };

        for (int f = 0; f < traceFrames; f++)
        {
            ApplyKeys(sys, keys, warmFrames + f);
            sys.RunFrame();
        }

        Console.WriteLine($"Warm: {warmFrames} frames.  Traced: {traceFrames} frames.");
        Console.WriteLine($"Target range: ${lo:X4}..${hi:X4}  ({hi - lo + 1} bytes)");
        Console.WriteLine($"Total writes to range: {totalWrites}");
        Console.WriteLine($"Distinct PCs writing: {pcHits.Count}");
        Console.WriteLine($"Distinct addresses touched: {addrHits.Count}");
        Console.WriteLine();
        Console.WriteLine("Hot PCs (top 20):");
        foreach (var (pc, count) in pcHits.OrderByDescending(kv => kv.Value).Take(20))
        {
            Console.WriteLine($"  PC=${pc:X4}  {count,6} writes");
        }

        if (addrHits.Count > 0)
        {
            ushort addrLo = addrHits.Keys.Min();
            ushort addrHi = addrHits.Keys.Max();
            Console.WriteLine();
            Console.WriteLine($"Address span touched: ${addrLo:X4}..${addrHi:X4}");
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
