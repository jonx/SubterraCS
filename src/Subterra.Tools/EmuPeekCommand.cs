using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Boot the snapshot, run N frames, then dump a list of memory
/// addresses. Useful for seeing which counters in RAM actually
/// change between two runs.
/// </summary>
internal static class EmuPeekCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: emu-peek <48k.rom> <file.z80> <frames> <hexAddr> [hexAddr...] [-keys=...]");
            return 2;
        }
        var rom = File.ReadAllBytes(args[0]);
        var snap = Z80SnapshotReader.Load(args[1]);
        int frames = int.Parse(args[2], CultureInfo.InvariantCulture);

        string keys = "";
        var addrs = new List<ushort>();
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].StartsWith("-keys=", StringComparison.Ordinal))
            {
                keys = args[i].Substring("-keys=".Length);
            }
            else
            {
                addrs.Add(ushort.Parse(args[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        var sys = new Spectrum48(rom);
        sys.LoadSnapshot(snap);
        for (int f = 0; f < frames; f++)
        {
            ApplyKeys(sys, keys, f);
            sys.RunFrame();
        }
        var ram = sys.RamView();
        Console.WriteLine($"After {frames} frames, PC={sys.Cpu.PC:X4}, Cycles={sys.Cpu.Cycles}");
        foreach (var addr in addrs)
        {
            byte b0 = addr >= 0x4000 ? ram[addr - 0x4000] : (byte)0;
            byte b1 = (addr + 1) >= 0x4000 ? ram[addr + 1 - 0x4000] : (byte)0;
            byte b2 = (addr + 2) >= 0x4000 ? ram[addr + 2 - 0x4000] : (byte)0;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  ${0:X4}: ${1:X2} ${2:X2} ${3:X2}  word=${4:X4}  triple=${5:X6}",
                addr, b0, b1, b2,
                (ushort)(b0 | (b1 << 8)),
                b0 | (b1 << 8) | (b2 << 16)));
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
            PressNamed(sys, bits[1]);
        }
    }

    private static void PressNamed(Spectrum48 sys, string name)
    {
        SpectrumKey? k = name.ToUpperInvariant() switch
        {
            "SPACE" => SpectrumKey.Space, "ENTER" => SpectrumKey.Enter,
            "1" => SpectrumKey.D1, "2" => SpectrumKey.D2, "3" => SpectrumKey.D3, "4" => SpectrumKey.D4, "5" => SpectrumKey.D5,
            "Q" => SpectrumKey.Q, "A" => SpectrumKey.A, "P" => SpectrumKey.P, "O" => SpectrumKey.O, "M" => SpectrumKey.M,
            "CAPS" => SpectrumKey.CapsShift,
            _ => null,
        };
        if (k is { } key) sys.PressKey(key);
    }
}
