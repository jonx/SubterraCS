using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class UnZ80Command
{
    public static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: unz80 <path/to/file.z80> <output.bin>");
            return 2;
        }
        var snap = Z80SnapshotReader.Load(args[0]);
        File.WriteAllBytes(args[1], snap.Ram48K);
        Console.WriteLine(
            $"Wrote {snap.Ram48K.Length} bytes ({snap.Kind}, PC={snap.Registers.PC:X4}, SP={snap.Registers.SP:X4}).");
        return 0;
    }
}
