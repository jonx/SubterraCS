using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class SnapshotInfoCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: snapshot-info <path/to/file.z80>");
            return 2;
        }
        var snap = Z80SnapshotReader.Load(args[0]);
        var r = snap.Registers;
        Console.WriteLine($"Kind:           {snap.Kind}");
        Console.WriteLine($"PC: {r.PC:X4}    SP: {r.SP:X4}    IM: {r.InterruptMode}    " +
                          $"Border: {r.BorderColour}    IFF1/2: {(r.Iff1 ? '1' : '0')}/{(r.Iff2 ? '1' : '0')}");
        Console.WriteLine($"AF: {r.AF:X4}    BC: {r.BC:X4}    DE: {r.DE:X4}    HL: {r.HL:X4}");
        Console.WriteLine($"AF':{r.AFp:X4}    BC':{r.BCp:X4}    DE':{r.DEp:X4}    HL':{r.HLp:X4}");
        Console.WriteLine($"IX: {r.IX:X4}    IY: {r.IY:X4}    I:  {r.I:X2}      R: {r.R:X2}");

        // Cheap region fingerprint so we can spot which parts of RAM are
        // code/data quickly.
        Console.WriteLine();
        Console.WriteLine("RAM region byte histogram peaks (top byte values per 4 KB block):");
        for (int block = 0; block < 12; block++)
        {
            int baseAddr = 0x4000 + block * 0x1000;
            var histogram = new int[256];
            for (int i = 0; i < 0x1000; i++)
            {
                histogram[snap.Ram48K[block * 0x1000 + i]]++;
            }
            int peakByte = 0;
            for (int b = 1; b < 256; b++)
            {
                if (histogram[b] > histogram[peakByte])
                {
                    peakByte = b;
                }
            }
            int zeros = histogram[0];
            int ff = histogram[0xFF];
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0:X4}-{1:X4}: peak=${2:X2} ({3,4}x)   00:{4,4}   FF:{5,4}",
                baseAddr, baseAddr + 0xFFF, peakByte, histogram[peakByte], zeros, ff));
        }
        return 0;
    }
}
