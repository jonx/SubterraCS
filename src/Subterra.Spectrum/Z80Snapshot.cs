namespace Subterra.Spectrum;

/// <summary>
/// CPU and memory state from a `.z80` snapshot file. Memory holds the full
/// 48 K of Spectrum RAM mapped at addresses 0x4000..0xFFFF — index by
/// subtracting 0x4000 from the Spectrum address.
/// </summary>
public sealed record Z80Snapshot(
    Z80Registers Registers,
    byte[] Ram48K,
    Z80SnapshotKind Kind)
{
    /// <summary>Read a byte from a Spectrum address (only 0x4000..0xFFFF
    /// is meaningful; the ROM at 0x0000..0x3FFF is not present in a
    /// snapshot).</summary>
    public byte Peek(ushort address)
    {
        if (address < 0x4000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address),
                "ROM region 0x0000-0x3FFF is not stored in a snapshot.");
        }
        return Ram48K[address - 0x4000];
    }
}

public enum Z80SnapshotKind
{
    V1,
    V2,
    V3,
}

public readonly record struct Z80Registers(
    ushort AF, ushort BC, ushort DE, ushort HL,
    ushort AFp, ushort BCp, ushort DEp, ushort HLp,
    ushort IX, ushort IY,
    ushort PC, ushort SP,
    byte I, byte R,
    byte InterruptMode,
    bool Iff1,
    bool Iff2,
    byte BorderColour);
