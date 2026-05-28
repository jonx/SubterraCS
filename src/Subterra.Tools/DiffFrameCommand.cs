using System.Diagnostics;
using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Side-by-side emulator-vs-native-port diff workflow.
///
/// Run the original cassette inside our Z80 emulator for N frames, run
/// the native C# port headless for N frames using the same key sequence,
/// then produce a composite PNG and a numerical pixel-diff summary so we
/// can see at a glance whether the latest port pass has shrunk the gap.
///
/// Usage:
///
///   diff-frame &lt;48k.rom&gt; &lt;snapshot.z80&gt; &lt;frames&gt;
///       [-keys=...]            same key spec as run-emu
///       [-native-keys=...]     key spec for the native port (defaults
///                              to a sensible translation of -keys)
///       [-seed=N]              native-port seed
///
/// Output: renders/diff-fNNNNN_TIMESTAMP.png — 3-panel composite
/// (emu | native | per-pixel red overlay).  Console: pixel-diff count
/// and percentage.
/// </summary>
internal static class DiffFrameCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: diff-frame <48k.rom> <snapshot.z80> <frames> [-keys=...] [-native-keys=...] [-seed=N]");
            return 2;
        }
        var romPath = args[0];
        var snapPath = args[1];
        int frames = int.Parse(args[2], CultureInfo.InvariantCulture);
        string emuKeys = "";
        string? nativeKeys = null;
        int seed = 1;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].StartsWith("-keys=", StringComparison.Ordinal))
                emuKeys = args[i].Substring("-keys=".Length);
            else if (args[i].StartsWith("-native-keys=", StringComparison.Ordinal))
                nativeKeys = args[i].Substring("-native-keys=".Length);
            else if (args[i].StartsWith("-seed=", StringComparison.Ordinal))
                seed = int.Parse(args[i].Substring("-seed=".Length), CultureInfo.InvariantCulture);
        }
        nativeKeys ??= TranslateKeysToNative(emuKeys);

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // 1) Emulator pass — in-process.
        Console.WriteLine($"[diff-frame] Running emulator {frames} frames (keys=\"{emuKeys}\")...");
        var emuRgba = RunEmulator(romPath, snapPath, frames, emuKeys);
        var emuOut = Path.Combine(repoRoot, "renders", $"diff-emu-f{frames:D5}_{stamp}.png");
        PngWriter.WriteRgba(emuOut, emuRgba, SpectrumScreen.Width, SpectrumScreen.Height);
        Console.WriteLine($"  emu  → {Path.GetRelativePath(repoRoot, emuOut)}");

        // 2) Native pass — subprocess.
        Console.WriteLine($"[diff-frame] Running native port {frames} frames (keys=\"{nativeKeys}\" seed={seed})...");
        var nativeOut = RunNativeHeadless(repoRoot, frames, nativeKeys, seed, stamp);
        if (nativeOut is null)
        {
            Console.Error.WriteLine("  native run failed — see output above.");
            return 1;
        }
        Console.WriteLine($"  native → {Path.GetRelativePath(repoRoot, nativeOut)}");

        // 3) Compose side-by-side + red-overlay diff.
        var nativeRgba = ReadPngRgba(nativeOut);
        if (nativeRgba is null || nativeRgba.Length != emuRgba.Length)
        {
            Console.Error.WriteLine("  native PNG dimensions don't match emu — aborting compose.");
            return 1;
        }

        var compose = Compose(emuRgba, nativeRgba, out int diffPixels);
        var composeOut = Path.Combine(repoRoot, "renders", $"diff-compose-f{frames:D5}_{stamp}.png");
        PngWriter.WriteRgba(composeOut, compose,
            SpectrumScreen.Width * 3 + 4,     // 3 panels + 2 spacers
            SpectrumScreen.Height);
        Console.WriteLine($"  diff → {Path.GetRelativePath(repoRoot, composeOut)}");
        double pct = 100.0 * diffPixels / (SpectrumScreen.Width * SpectrumScreen.Height);
        Console.WriteLine($"[diff-frame] {diffPixels} pixels differ ({pct:F2}%).");
        return 0;
    }

    // ─── Emulator side ─────────────────────────────────────────────

    private static byte[] RunEmulator(string romPath, string snapPath, int frames, string keys)
    {
        var rom = File.ReadAllBytes(romPath);
        var snap = Z80SnapshotReader.Load(snapPath);
        var sys = new Spectrum48(rom);
        sys.LoadSnapshot(snap);
        for (int f = 0; f < frames; f++)
        {
            ApplyKeysForFrame(sys, keys, f);
            sys.RunFrame();
        }
        return SpectrumScreen.DecodeRgba(sys.RamView().Slice(0, SpectrumScreen.ScrBytes));
    }

    private static void ApplyKeysForFrame(Spectrum48 sys, string keys, int frame)
    {
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
                if (!int.TryParse(range, NumberStyles.Integer, CultureInfo.InvariantCulture, out start)) continue;
                end = start;
            }
            if (frame < start || frame > end) continue;
            PressKey(sys, bits[1]);
        }
    }

    private static void PressKey(Spectrum48 sys, string name)
    {
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

    // ─── Native side ───────────────────────────────────────────────

    /// <summary>
    /// Translate the emulator-style key spec into one the native port
    /// accepts.  The native port understands a small symbolic vocabulary
    /// (Q/UP, A/DOWN, L/LEFT/RIGHT, FIRE/ENTER/SPACE) — same vertical
    /// controls the emulator uses, plus the menu auto-advance.
    /// </summary>
    private static string TranslateKeysToNative(string emuKeys)
    {
        // The emulator key spec is a list of "START-END:KEY".  The native
        // port already uses Q/A/L/FIRE.  Pass through verbatim.
        return emuKeys;
    }

    private static string? RunNativeHeadless(string repoRoot, int frames, string keys, int seed, string stamp)
    {
        var nativeDir = Path.Combine(repoRoot, "native");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = nativeDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add("SubterraCS.Game");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add($"--frames={frames}");
        if (!string.IsNullOrEmpty(keys))
            startInfo.ArgumentList.Add($"--keys={keys}");
        startInfo.ArgumentList.Add($"--seed={seed}");

        using var proc = Process.Start(startInfo)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            Console.Error.WriteLine(stdout);
            Console.Error.WriteLine(stderr);
            return null;
        }

        // Native port writes renders/native-headless-fNNNNN_TIMESTAMP.png — we
        // grep the stdout for the last final-frame line and resolve the path.
        string? finalPath = null;
        foreach (var line in stdout.Split('\n'))
        {
            int arrow = line.IndexOf("→");
            if (arrow < 0) continue;
            var rel = line[(arrow + 1)..].Trim();
            if (rel.EndsWith(".png", StringComparison.Ordinal))
            {
                finalPath = Path.Combine(repoRoot, rel);
            }
        }
        return finalPath;
    }

    private static byte[]? ReadPngRgba(string pngPath)
    {
        // The native port writes a sidecar .png.rgba next to every PNG it
        // produces in --headless mode — raw RGBA bytes, no decoding needed.
        var rawPath = pngPath + ".rgba";
        if (!File.Exists(rawPath))
        {
            Console.Error.WriteLine($"  expected raw sidecar at {rawPath} — native port may be out of date.");
            return null;
        }
        return File.ReadAllBytes(rawPath);
    }

    // ─── Compose ───────────────────────────────────────────────────

    private static byte[] Compose(byte[] emu, byte[] native, out int diffPixels)
    {
        int w = SpectrumScreen.Width, h = SpectrumScreen.Height;
        int composeW = w * 3 + 4;        // 3 panels + 2 × 2px separators
        var output = new byte[composeW * h * 4];
        diffPixels = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int si = (y * w + x) * 4;
                // Panel 1 — emulator
                CopyPx(output, (y * composeW + x) * 4, emu, si);
                // Panel 2 — native (offset w + 2)
                CopyPx(output, (y * composeW + (w + 2) + x) * 4, native, si);
                // Panel 3 — diff overlay: emu pixel where they agree,
                // bright red where they differ.
                bool same = emu[si]   == native[si]
                         && emu[si+1] == native[si+1]
                         && emu[si+2] == native[si+2];
                int di = (y * composeW + (2 * (w + 2)) + x) * 4;
                if (same)
                {
                    output[di]   = (byte)(emu[si]   / 3);   // dim
                    output[di+1] = (byte)(emu[si+1] / 3);
                    output[di+2] = (byte)(emu[si+2] / 3);
                    output[di+3] = 0xFF;
                }
                else
                {
                    output[di]   = 0xFF;
                    output[di+1] = 0x00;
                    output[di+2] = 0x00;
                    output[di+3] = 0xFF;
                    diffPixels++;
                }
            }
            // Vertical separators (white) between panels.
            for (int x = w; x < w + 2; x++)
            {
                int di = (y * composeW + x) * 4;
                output[di] = output[di+1] = output[di+2] = 0xFF; output[di+3] = 0xFF;
            }
            for (int x = 2 * w + 2; x < 2 * w + 4; x++)
            {
                int di = (y * composeW + x) * 4;
                output[di] = output[di+1] = output[di+2] = 0xFF; output[di+3] = 0xFF;
            }
        }
        return output;
    }

    private static void CopyPx(byte[] dst, int di, byte[] src, int si)
    {
        dst[di]   = src[si];
        dst[di+1] = src[si+1];
        dst[di+2] = src[si+2];
        dst[di+3] = src[si+3];
    }
}
