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

        int stride = 0;
        string? ramOut = null;
        string? wavOut = null;
        int wavSampleRate = 44100;
        int wavFromFrame = 0;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].StartsWith("-stride=", StringComparison.Ordinal))
            {
                stride = int.Parse(args[i].Substring("-stride=".Length), CultureInfo.InvariantCulture);
            }
            else if (args[i].StartsWith("-ram=", StringComparison.Ordinal))
            {
                ramOut = args[i].Substring("-ram=".Length);
            }
            else if (args[i].StartsWith("-wav=", StringComparison.Ordinal))
            {
                wavOut = args[i].Substring("-wav=".Length);
            }
            else if (args[i].StartsWith("-wav-rate=", StringComparison.Ordinal))
            {
                wavSampleRate = int.Parse(args[i].Substring("-wav-rate=".Length), CultureInfo.InvariantCulture);
            }
            else if (args[i].StartsWith("-wav-from=", StringComparison.Ordinal))
            {
                wavFromFrame = int.Parse(args[i].Substring("-wav-from=".Length), CultureInfo.InvariantCulture);
            }
        }

        var rom = File.ReadAllBytes(romPath);
        var snap = Z80SnapshotReader.Load(snapPath);
        var sys = new Spectrum48(rom);
        sys.LoadSnapshot(snap);

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        Console.WriteLine($"Boot: PC={sys.Cpu.PC:X4} SP={sys.Cpu.SP:X4} I={sys.Cpu.I:X2}");

        var snapName = Path.GetFileNameWithoutExtension(snapPath).ToLowerInvariant();
        // One timestamp shared by the whole run, so the sequence sorts together.
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        long wavFromCycle = 0;
        for (int f = 0; f < frames; f++)
        {
            if (f == wavFromFrame) wavFromCycle = sys.Cpu.Cycles;
            ApplyKeysForFrame(sys, keys, f);
            sys.RunFrame();

            if (stride > 0 && (f + 1) % stride == 0)
            {
                var rgba = SpectrumScreen.DecodeRgba(sys.RamView().Slice(0, SpectrumScreen.ScrBytes));
                var path = RenderTarget.ForExtension(
                    repoRoot, $"emu-{snapName}-seq-f{f + 1:D5}", "png", stamp);
                PngWriter.WriteRgba(path, rgba, SpectrumScreen.Width, SpectrumScreen.Height);
            }
        }

        // Always render the final frame too.
        var finalRgba = SpectrumScreen.DecodeRgba(sys.RamView().Slice(0, SpectrumScreen.ScrBytes));
        var outPath = RenderTarget.ForExtension(
            repoRoot, $"emu-{snapName}-f{frames:D5}", "png", stamp);
        PngWriter.WriteRgba(outPath, finalRgba, SpectrumScreen.Width, SpectrumScreen.Height);
        Console.WriteLine($"After {frames} frames: PC={sys.Cpu.PC:X4} Cycles={sys.Cpu.Cycles}");
        Console.WriteLine(outPath);
        if (ramOut is not null)
        {
            File.WriteAllBytes(ramOut, sys.RamView().ToArray());
            Console.WriteLine($"RAM dump: {ramOut}");
        }
        if (wavOut is not null)
        {
            // Resample the full beeper-edge log to PCM at the requested
            // rate.  The cassette plays sound by toggling bit 4 of port
            // $FE; we captured every edge with its CPU cycle stamp, so
            // a square-wave resampler reproduces the actual sound the
            // user would hear from the Spectrum's speaker.
            var pcm = sys.Beeper.RenderPcm(wavFromCycle, sys.Cpu.Cycles, wavSampleRate);
            WavWriter.WriteMono16(wavOut, pcm, wavSampleRate);
            Console.WriteLine($"Beeper WAV: {wavOut} ({sys.Beeper.EdgeCount} edges, {pcm.Length} samples @ {wavSampleRate} Hz, {(double)pcm.Length / wavSampleRate:F2}s)");
        }
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
