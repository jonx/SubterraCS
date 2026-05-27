using System.Globalization;
using System.Text;
using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class HexCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: hex <path/to/file.z80> <hexAddr> <count>");
            return 2;
        }
        byte[] ram;
        if (args[0].EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            ram = File.ReadAllBytes(args[0]);
            if (ram.Length != 0xC000)
            {
                Console.Error.WriteLine($"Raw RAM dump must be exactly {0xC000} bytes; got {ram.Length}.");
                return 1;
            }
        }
        else
        {
            ram = Z80SnapshotReader.Load(args[0]).Ram48K;
        }
        ushort addr = ushort.Parse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int count = int.Parse(args[2], CultureInfo.InvariantCulture);

        if (addr < 0x4000)
        {
            Console.Error.WriteLine("RAM image starts at $4000; pick an address ≥ $4000.");
            return 1;
        }

        for (int row = 0; row < count; row += 16)
        {
            int len = Math.Min(16, count - row);
            var sb = new StringBuilder();
            sb.Append(((addr + row) & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture));
            sb.Append("  ");
            for (int i = 0; i < 16; i++)
            {
                if (i == 8) sb.Append(' ');
                if (i < len)
                {
                    byte b = ram[addr + row + i - 0x4000];
                    sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }
            }
            sb.Append("  |");
            for (int i = 0; i < len; i++)
            {
                byte b = ram[addr + row + i - 0x4000];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.Append('|');
            Console.WriteLine(sb.ToString());
        }
        return 0;
    }
}
