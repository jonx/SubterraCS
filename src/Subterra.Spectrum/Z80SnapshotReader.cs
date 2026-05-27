using System.Buffers.Binary;

namespace Subterra.Spectrum;

/// <summary>
/// Reader for the `.z80` snapshot format created by the original Z80 emulator
/// by Gerton Lunter. This handles both the original (v1) 30-byte-header form
/// and the v2/v3 extended forms that came with later emulators.
///
/// Spec references we worked from:
///   * Gerton Lunter's z80.txt distributed with the original Z80 emulator
///   * The condensed Spectaculator / Speccy.pl summaries widely mirrored
///     around the preservation community
///
/// Only the bits we actually need to load Subterranean Stryker are
/// implemented. In particular we always assume a 48 K machine (anything
/// requiring a 128 K bank layout will throw).
/// </summary>
public static class Z80SnapshotReader
{
    /// <summary>Read the snapshot bytes (the raw file contents).</summary>
    public static Z80Snapshot Load(ReadOnlySpan<byte> file)
    {
        if (file.Length < 30)
        {
            throw new InvalidDataException(
                $".z80 file is only {file.Length} bytes; cannot fit a v1 header.");
        }

        var header = ParseV1Header(file);
        var afterV1 = file[30..];

        Z80SnapshotKind kind;
        ReadOnlySpan<byte> body;
        ushort pc;

        if (header.V1PC != 0)
        {
            // v1: a single compressed memory stream follows the 30-byte header.
            kind = Z80SnapshotKind.V1;
            body = afterV1;
            pc = header.V1PC;
        }
        else
        {
            // v2 / v3: an extra header follows, then one or more banked memory
            // blocks. Subterranean Stryker is distributed as a v1 .z80 (see
            // docs/RE-LOG.md §4), so we don't need this path right now — but
            // we leave the door open.
            if (afterV1.Length < 2)
            {
                throw new InvalidDataException(
                    "Extended .z80 header is truncated.");
            }
            var extraLen = BinaryPrimitives.ReadUInt16LittleEndian(afterV1[..2]);
            if (extraLen != 23 && extraLen != 54 && extraLen != 55)
            {
                throw new NotSupportedException(
                    $"Unknown extended .z80 header length: {extraLen}.");
            }
            kind = extraLen switch
            {
                23 => Z80SnapshotKind.V2,
                _ => Z80SnapshotKind.V3,
            };
            var extra = afterV1.Slice(2, extraLen);
            pc = BinaryPrimitives.ReadUInt16LittleEndian(extra[..2]);
            var hwMode = extra[2];
            // For a 48 K machine in v2 hwMode is 0 (48k) or 1 (48k+if1);
            // in v3 it's 0 or 1 also. Anything else is 128 K territory.
            if (hwMode > 1)
            {
                throw new NotSupportedException(
                    $"Only 48 K snapshots are supported; hardware mode = {hwMode}.");
            }
            body = afterV1[(2 + extraLen)..];
        }

        var ram = new byte[0xC000];

        if (kind == Z80SnapshotKind.V1)
        {
            DecodeV1Body(body, header.IsCompressed, ram);
        }
        else
        {
            DecodeExtendedBody(body, ram);
        }

        var regs = header.ToRegisters(pc);
        return new Z80Snapshot(regs, ram, kind);
    }

    /// <summary>Load from a path.</summary>
    public static Z80Snapshot Load(string path)
        => Load(File.ReadAllBytes(path));

    private static V1Header ParseV1Header(ReadOnlySpan<byte> file)
    {
        // .z80 v1 layout (offsets in decimal):
        //  0  A
        //  1  F
        //  2  C
        //  3  B
        //  4  L
        //  5  H
        //  6  PC low
        //  7  PC high   (zero ⇒ extended v2/v3 header follows)
        //  8  SP low
        //  9  SP high
        // 10  I
        // 11  R (low 7 bits — bit 7 lives in byte 12 bit 0)
        // 12  flags1: bit 0 = R bit 7
        //             bits 1..3 = border colour
        //             bit 4 = 1 if BASIC SamRom switched in (ignore)
        //             bit 5 = 1 if memory block is compressed (V1)
        //             bits 6..7 = unused / SamRom selector
        //             0xFF here means "treat as 1" per spec.
        // 13  E
        // 14  D
        // 15  BC' low
        // 16  BC' high
        // 17  DE' low
        // 18  DE' high
        // 19  HL' low
        // 20  HL' high
        // 21  A'
        // 22  F'
        // 23  IY low
        // 24  IY high
        // 25  IX low
        // 26  IX high
        // 27  IFF1 (0 or 1)
        // 28  IFF2 (0 or 1)
        // 29  flags2: bits 0..1 = interrupt mode
        //             bit 2 = issue 2 emulation
        //             bit 3 = double interrupt frequency
        //             bits 4..5 = video sync
        //             bits 6..7 = joystick emulation
        byte a = file[0];
        byte f = file[1];
        ushort bc = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(2, 2));
        ushort hl = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(4, 2));
        ushort v1Pc = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(6, 2));
        ushort sp = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(8, 2));
        byte i = file[10];
        byte rLow = file[11];
        byte flags1 = file[12];
        if (flags1 == 0xFF)
        {
            flags1 = 0x01;
        }
        byte r = (byte)((rLow & 0x7F) | ((flags1 & 0x01) << 7));
        byte border = (byte)((flags1 >> 1) & 0x07);
        bool isCompressed = (flags1 & 0x20) != 0;
        ushort de = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(13, 2));
        ushort bcp = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(15, 2));
        ushort dep = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(17, 2));
        ushort hlp = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(19, 2));
        byte ap = file[21];
        byte fp = file[22];
        ushort iy = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(23, 2));
        ushort ix = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(25, 2));
        bool iff1 = file[27] != 0;
        bool iff2 = file[28] != 0;
        byte flags2 = file[29];
        byte im = (byte)(flags2 & 0x03);

        return new V1Header(
            A: a, F: f, BC: bc, HL: hl, V1PC: v1Pc, SP: sp,
            I: i, R: r, BorderColour: border, IsCompressed: isCompressed,
            DE: de, BCp: bcp, DEp: dep, HLp: hlp, Ap: ap, Fp: fp,
            IY: iy, IX: ix, Iff1: iff1, Iff2: iff2, InterruptMode: im);
    }

    private static void DecodeV1Body(
        ReadOnlySpan<byte> body, bool compressed, byte[] ram)
    {
        if (!compressed)
        {
            if (body.Length < 0xC000)
            {
                throw new InvalidDataException(
                    "Uncompressed v1 .z80 body is shorter than 48 K.");
            }
            body[..0xC000].CopyTo(ram);
            return;
        }

        // v1 compressed stream terminator: 0x00 0xED 0xED 0x00. We strip
        // those four bytes (the run-length decoder would otherwise emit
        // them literally).
        var stream = body;
        // Find the terminator and use the slice that precedes it. Spec
        // says the terminator MUST exist for compressed v1 dumps.
        for (int i = 0; i <= stream.Length - 4; i++)
        {
            if (stream[i] == 0x00 && stream[i + 1] == 0xED
                && stream[i + 2] == 0xED && stream[i + 3] == 0x00)
            {
                stream = stream[..i];
                break;
            }
        }

        var written = DecodeRle(stream, ram, 0);
        if (written != ram.Length)
        {
            throw new InvalidDataException(
                $".z80 RLE stream decoded to {written} bytes, expected {ram.Length}.");
        }
    }

    private static void DecodeExtendedBody(ReadOnlySpan<byte> body, byte[] ram)
    {
        // v2/v3: a sequence of blocks, each
        //   2 bytes  compressed length (0xFFFF means "16384 uncompressed bytes")
        //   1 byte   page number
        //   N bytes  data
        // For a 48 K snapshot the pages we care about are:
        //   page 4 → 0x8000..0xBFFF
        //   page 5 → 0xC000..0xFFFF
        //   page 8 → 0x4000..0x7FFF
        // (other page numbers map to 128 K banks we don't support).
        while (body.Length >= 3)
        {
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(body[..2]);
            byte page = body[2];
            body = body[3..];
            bool raw = len == 0xFFFF;
            int take = raw ? 0x4000 : len;
            if (body.Length < take)
            {
                throw new InvalidDataException(
                    "Extended .z80 page block is truncated.");
            }
            int destOffset = page switch
            {
                4 => 0x8000 - 0x4000,
                5 => 0xC000 - 0x4000,
                8 => 0x4000 - 0x4000,
                _ => throw new NotSupportedException(
                    $"Unsupported page number {page} (need 128 K support)."),
            };
            if (raw)
            {
                body[..take].CopyTo(ram.AsSpan(destOffset, 0x4000));
            }
            else
            {
                int written = DecodeRle(body[..take], ram, destOffset);
                if (written != 0x4000)
                {
                    throw new InvalidDataException(
                        $"Page {page} decoded to {written} bytes, expected 16384.");
                }
            }
            body = body[take..];
        }
    }

    /// <summary>
    /// Decode the Z80 emulator's RLE stream into <paramref name="dest"/>
    /// starting at <paramref name="offset"/>.  Returns the number of bytes
    /// written.
    ///
    /// The encoding is: any pair of literal <c>ED ED</c> bytes is replaced
    /// by <c>ED ED count value</c>. A lone <c>ED</c> is passed through
    /// untouched (and the byte after it is NOT subject to RLE).
    /// </summary>
    internal static int DecodeRle(
        ReadOnlySpan<byte> source, byte[] dest, int offset)
    {
        int o = offset;
        int i = 0;
        while (i < source.Length)
        {
            byte b = source[i];
            if (b == 0xED && i + 1 < source.Length && source[i + 1] == 0xED)
            {
                if (i + 3 >= source.Length)
                {
                    throw new InvalidDataException(
                        "Truncated ED ED run in .z80 RLE stream.");
                }
                int count = source[i + 2];
                byte value = source[i + 3];
                if (o + count > dest.Length)
                {
                    throw new InvalidDataException(
                        "RLE run would overflow the destination buffer.");
                }
                for (int k = 0; k < count; k++)
                {
                    dest[o++] = value;
                }
                i += 4;
            }
            else if (b == 0xED)
            {
                // A lone ED: emit ED, then the next byte verbatim (so we
                // don't accidentally start a run from the byte after).
                if (i + 1 >= source.Length)
                {
                    if (o >= dest.Length)
                    {
                        throw new InvalidDataException(
                            "Lone ED at end of stream overflows the destination.");
                    }
                    dest[o++] = 0xED;
                    i++;
                }
                else
                {
                    if (o + 2 > dest.Length)
                    {
                        throw new InvalidDataException(
                            "Lone ED + follower would overflow destination.");
                    }
                    dest[o++] = 0xED;
                    dest[o++] = source[i + 1];
                    i += 2;
                }
            }
            else
            {
                if (o >= dest.Length)
                {
                    throw new InvalidDataException(
                        "Decoded RLE stream is longer than 48 K.");
                }
                dest[o++] = b;
                i++;
            }
        }
        return o - offset;
    }

    private readonly record struct V1Header(
        byte A, byte F, ushort BC, ushort HL, ushort V1PC, ushort SP,
        byte I, byte R, byte BorderColour, bool IsCompressed,
        ushort DE, ushort BCp, ushort DEp, ushort HLp, byte Ap, byte Fp,
        ushort IY, ushort IX, bool Iff1, bool Iff2, byte InterruptMode)
    {
        public Z80Registers ToRegisters(ushort pc) => new(
            AF: (ushort)((A << 8) | F),
            BC: BC,
            DE: DE,
            HL: HL,
            AFp: (ushort)((Ap << 8) | Fp),
            BCp: BCp,
            DEp: DEp,
            HLp: HLp,
            IX: IX,
            IY: IY,
            PC: pc,
            SP: SP,
            I: I,
            R: R,
            InterruptMode: InterruptMode,
            Iff1: Iff1,
            Iff2: Iff2,
            BorderColour: BorderColour);
    }
}
