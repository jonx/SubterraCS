using System.Globalization;
using Subterra.Spectrum;
using Subterra.Spectrum.Z80;

namespace Subterra.Tools;

internal static class DisasmCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3 || args.Length > 4)
        {
            Console.Error.WriteLine(
                "usage: disasm <path/to/file.z80> <hexAddr> <count> [out.asm]\n" +
                "       <hexAddr> is a 16-bit Spectrum address, e.g. E000\n" +
                "       <count>   is the number of instructions to decode\n" +
                "       writes to stdout if [out.asm] omitted");
            return 2;
        }

        ushort addr = ushort.Parse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int count = int.Parse(args[2], CultureInfo.InvariantCulture);

        // Build a full 64 K view: zero-filled ROM region, snapshot RAM
        // for $4000-$FFFF. Anything that decodes into the ROM region
        // will read zeros (NOP), which is fine for our purposes — we
        // only ever disassemble game code that lives in RAM anyway.
        var memory = new byte[0x10000];
        // Accept either a .z80 snapshot OR a raw 48K RAM dump (49152
        // bytes covering $4000..$FFFF — the at-fNNN.bin captures).
        var raw = File.ReadAllBytes(args[0]);
        if (raw.Length == 49152)
        {
            Array.Copy(raw, 0, memory, 0x4000, 49152);
        }
        else
        {
            var snap = Z80SnapshotReader.Load(args[0]);
            Array.Copy(snap.Ram48K, 0, memory, 0x4000, snap.Ram48K.Length);
        }

        var lines = Z80Disassembler.DecodeRange(addr, memory, count);
        TextWriter writer = args.Length == 4
            ? new StreamWriter(args[3])
            : Console.Out;
        try
        {
            foreach (var ins in lines)
            {
                writer.WriteLine(Z80Disassembler.Format(ins));
            }
        }
        finally
        {
            if (args.Length == 4)
            {
                writer.Dispose();
            }
        }
        return 0;
    }
}
