using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class StackWalkCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            Console.Error.WriteLine("usage: stack-walk <path/to/file.z80> [depth]");
            return 2;
        }
        int depth = args.Length == 2
            ? int.Parse(args[1], CultureInfo.InvariantCulture)
            : 16;

        var snap = Z80SnapshotReader.Load(args[0]);
        ushort sp = snap.Registers.SP;
        Console.WriteLine($"SP = {sp:X4}");
        Console.WriteLine("offset  addr  word    ascii");
        for (int i = 0; i < depth; i++)
        {
            int p = sp + i * 2;
            if (p + 1 >= 0x10000 || p < 0x4000)
            {
                break;
            }
            byte lo = snap.Ram48K[p - 0x4000];
            byte hi = snap.Ram48K[p + 1 - 0x4000];
            ushort w = (ushort)((hi << 8) | lo);
            char cLo = lo >= 32 && lo < 127 ? (char)lo : '.';
            char cHi = hi >= 32 && hi < 127 ? (char)hi : '.';
            Console.WriteLine($"  +{i * 2,2}  {p:X4}  {w:X4}    {cLo}{cHi}");
        }
        return 0;
    }
}
