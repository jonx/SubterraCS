using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return PrintHelp();
        }
        var command = args[0];
        var rest = args[1..];
        return command switch
        {
            "render-scr"        => RenderScrCommand.Run(rest),
            "render-snapshot"   => RenderSnapshotCommand.Run(rest),
            "unz80"             => UnZ80Command.Run(rest),
            "snapshot-info"     => SnapshotInfoCommand.Run(rest),
            "disasm"            => DisasmCommand.Run(rest),
            "stack-walk"        => StackWalkCommand.Run(rest),
            "hex"               => HexCommand.Run(rest),
            "run-emu"           => RunEmuCommand.Run(rest),
            "find-bytes"        => FindBytesCommand.Run(rest),
            "emu-peek"          => EmuPeekCommand.Run(rest),
            "sprite-scan"       => SpriteScanCommand.Run(rest),
            "tile-trace"        => TileTraceCommand.Run(rest),
            "scrwrite-trace"    => ScreenWriteTraceCommand.Run(rest),
            "entity-bank"       => EntityBankCommand.Run(rest),
            "player-dump"       => PlayerDumpCommand.Run(rest),
            "extract-all"       => ExtractAllCommand.Run(rest),
            "diff-frame"        => DiffFrameCommand.Run(rest),
            "sfx-render"        => SfxRenderCommand.Run(rest),
            "mem-write-trace"   => MemWriteTraceCommand.Run(rest),
            "-h" or "--help" or "help" => PrintHelp(),
            _ => Unknown(command),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return PrintHelp();
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
        Subterra.Tools — reverse-engineering toolkit.

        Commands:
          render-scr <path/to/file.scr>
            Render a 6 912-byte Spectrum screen file as RGBA PNG into
            renders/, with a timestamped filename.

          render-snapshot <path/to/file.z80>
            Load a .z80 snapshot, decode its screen memory and render
            it as RGBA PNG into renders/ with a timestamped filename.

          unz80 <path/to/file.z80> <output.bin>
            Decompress a .z80 snapshot and write the flat 48 K RAM image
            to <output.bin> (Spectrum address 0x4000 lands at offset 0).

          snapshot-info <path/to/file.z80>
            Print register state and basic memory checksums for a
            snapshot.

          disasm <path/to/file.z80> <hexAddr> <count> [out.asm]
            Disassemble <count> Z80 instructions starting at hex
            address <hexAddr>. Writes to stdout unless out.asm given.

          stack-walk <path/to/file.z80> [depth]
            Show the top of stack (return addresses) for a snapshot;
            handy for tracking down where game code was running when
            PC sits in ROM.

          hex <path/to/file.z80> <hexAddr> <count>
            Hex/ASCII dump of <count> bytes starting at hex address
            <hexAddr> in the snapshot's RAM image.

          run-emu <48k.rom> <file.z80> <frames> [opts]
            Boot the snapshot inside our Z80 emulator, run <frames>
            video frames, then render the final screen to renders/.
            Optional flags:
              -keys=START[-END]:KEY,... — press keys on given frames
              -stride=N — drop a render every N frames into renders/
              -ram=path/to/out.bin — dump the 48 K RAM after running
              -wav=path/to/out.wav — render the beeper audio to WAV
              -wav-rate=N — WAV sample rate in Hz (default 44100)

          emu-peek <48k.rom> <file.z80> <frames> <hexAddr> [hexAddr ...]
            Like run-emu, but prints the byte/word/triple value at
            each <hexAddr> after running. -keys is supported too.

          find-bytes <file.z80> <hex-pattern> [-min=ADDR] [-max=ADDR]
            Find every occurrence of a byte pattern (with ?? wildcards)
            in the snapshot's RAM. Useful for spotting opcodes.

          sprite-scan <file.z80|file.bin> <fromHex> <toHex> <WxH[,WxH...]> [opts]
            Bulk-render candidate sprite cells across a RAM range.
            Each addr/shape produces a contact sheet PNG in renders/.
              -cols=N, -count=N, -scale=N — sheet dimensions / zoom

          diff-frame <48k.rom> <snapshot.z80> <frames>
              [-keys=...] [-native-keys=...] [-seed=N]
            Run the original game inside our Z80 emulator for <frames>
            frames AND the native C# port (headless) for the same number
            of frames using the same input.  Drops a 3-panel side-by-side
            composite (emu | native | red-on-diff overlay) into renders/
            and prints the pixel-diff count + percentage.  This is the
            primary driver for the RE-and-port workflow.
        """);
        return 0;
    }
}
