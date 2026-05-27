using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class RunEmuCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: run-emu <path/to/48k.rom> <path/to/snapshot.z80> <frames> [-keys=COMMA,SEPARATED]");
            return 2;
        }
        var romPath = args[0];
        var snapPath = args[1];
        int frames = int.Parse(args[2], CultureInfo.InvariantCulture);
        string keys = "";
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].StartsWith("-keys=", StringComparison.Ordinal))
            {
                keys = args[i].Substring("-keys=".Length);
            }
        }

        var rom = File.ReadAllBytes(romPath);
        var snap = Z80SnapshotReader.Load(snapPath);
        var sys = new Spectrum48(rom);
        sys.LoadSnapshot(snap);

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        Console.WriteLine($"Boot: PC={sys.Cpu.PC:X4} SP={sys.Cpu.SP:X4} I={sys.Cpu.I:X2}");

        for (int f = 0; f < frames; f++)
        {
            ApplyKeysForFrame(sys, keys, f);
            sys.RunFrame();
        }

        // Snapshot the screen and write it out.
        var rgba = SpectrumScreen.DecodeRgba(sys.RamView().Slice(0, SpectrumScreen.ScrBytes));
        var descriptor = $"emu-{Path.GetFileNameWithoutExtension(snapPath).ToLowerInvariant()}-f{frames:D5}";
        var outPath = RenderTarget.ForPng(repoRoot, descriptor);
        PngWriter.WriteRgba(outPath, rgba, SpectrumScreen.Width, SpectrumScreen.Height);
        Console.WriteLine($"After {frames} frames: PC={sys.Cpu.PC:X4} Cycles={sys.Cpu.Cycles}");
        Console.WriteLine(outPath);
        return 0;
    }

    /// <summary>
    /// Lazy keyboard scheduler. The keys argument is "SPEC,SPEC,..."
    /// where SPEC is either:
    ///   <c>FRAME:KEY</c>            press KEY for one frame
    ///   <c>START-END:KEY</c>        hold KEY across frames START..END
    /// KEY is a single character or "SPACE", "ENTER", "CAPS", "SYMBOL".
    /// </summary>
    private static void ApplyKeysForFrame(Spectrum48 sys, string keys, int frame)
    {
        // Release everything by default.
        for (int i = 0; i < 8; i++) sys.KeyHalfRows[i] = 0x1F;
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
                if (!int.TryParse(range[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out start) ||
                    !int.TryParse(range[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out end))
                    continue;
            }
            else
            {
                if (!int.TryParse(range, NumberStyles.Integer, CultureInfo.InvariantCulture, out start))
                    continue;
                end = start;
            }
            if (frame < start || frame > end) continue;
            PressKey(sys, bits[1]);
        }
    }

    private static void PressKey(Spectrum48 sys, string name)
    {
        // Spectrum keyboard half-rows (port high byte / bit within row).
        // Row 0 (A8=0): CAPS Z X C V        (bits 0..4)
        // Row 1 (A9=0): A   S D F G
        // Row 2 (A10):  Q   W E R T
        // Row 3 (A11):  1   2 3 4 5
        // Row 4 (A12):  0   9 8 7 6
        // Row 5 (A13):  P   O I U Y
        // Row 6 (A14):  ENT L K J H
        // Row 7 (A15):  SPC SS M N B
        (int row, int bit)? key = name.ToUpperInvariant() switch
        {
            "CAPS"  => (0, 0), "Z" => (0, 1), "X" => (0, 2), "C" => (0, 3), "V" => (0, 4),
            "A" => (1, 0), "S" => (1, 1), "D" => (1, 2), "F" => (1, 3), "G" => (1, 4),
            "Q" => (2, 0), "W" => (2, 1), "E" => (2, 2), "R" => (2, 3), "T" => (2, 4),
            "1" => (3, 0), "2" => (3, 1), "3" => (3, 2), "4" => (3, 3), "5" => (3, 4),
            "0" => (4, 0), "9" => (4, 1), "8" => (4, 2), "7" => (4, 3), "6" => (4, 4),
            "P" => (5, 0), "O" => (5, 1), "I" => (5, 2), "U" => (5, 3), "Y" => (5, 4),
            "ENTER" or "ENT" => (6, 0), "L" => (6, 1), "K" => (6, 2), "J" => (6, 3), "H" => (6, 4),
            "SPACE" or "SPC" => (7, 0), "SYMBOL" or "SS" => (7, 1), "M" => (7, 2), "N" => (7, 3), "B" => (7, 4),
            _ => null,
        };
        if (key is { } k)
        {
            sys.KeyHalfRows[k.row] &= (byte)~(1 << k.bit);
        }
    }
}
