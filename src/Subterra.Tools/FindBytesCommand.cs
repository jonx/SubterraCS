using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class FindBytesCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "usage: find-bytes <path/to/file.z80> <hex-pattern> [-min=ADDR] [-max=ADDR]\n" +
                "       hex-pattern is contiguous hex pairs, optionally with '??' wildcards\n" +
                "       e.g.  DB FE        — find every IN A,($FE)\n" +
                "             3E ?? DB FE  — preceded by an LD A,n");
            return 2;
        }
        var snap = Z80SnapshotReader.Load(args[0]);
        var raw = args[1].Replace(" ", "").Replace(",", "").ToUpperInvariant();
        if (raw.Length % 2 != 0)
        {
            Console.Error.WriteLine("Pattern must have an even number of hex digits.");
            return 1;
        }
        int n = raw.Length / 2;
        var bytes = new byte[n];
        var mask = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var b = raw.Substring(i * 2, 2);
            if (b == "??") { mask[i] = false; bytes[i] = 0; }
            else { mask[i] = true; bytes[i] = byte.Parse(b, NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
        }
        int min = 0x4000, max = 0xFFFF;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].StartsWith("-min=", StringComparison.Ordinal))
                min = int.Parse(args[i].Substring(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            else if (args[i].StartsWith("-max=", StringComparison.Ordinal))
                max = int.Parse(args[i].Substring(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        if (max < min || max > 0xFFFF) max = 0xFFFF;

        for (int addr = min; addr <= max - n + 1; addr++)
        {
            bool ok = true;
            for (int i = 0; i < n; i++)
            {
                if (!mask[i]) continue;
                if (snap.Ram48K[addr - 0x4000 + i] != bytes[i]) { ok = false; break; }
            }
            if (ok)
            {
                // Print a little context.
                var ctx = new System.Text.StringBuilder();
                int ctxStart = Math.Max(min, addr - 2);
                int ctxEnd = Math.Min(max, addr + n + 1);
                for (int i = ctxStart; i <= ctxEnd; i++)
                {
                    if (i == addr) ctx.Append('[');
                    ctx.Append(snap.Ram48K[i - 0x4000].ToString("X2", CultureInfo.InvariantCulture));
                    if (i == addr + n - 1) ctx.Append(']');
                    if (i < ctxEnd) ctx.Append(' ');
                }
                Console.WriteLine($"${addr:X4}: {ctx}");
            }
        }
        return 0;
    }
}
