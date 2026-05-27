namespace Subterra.Spectrum.Z80;

/// <summary>
/// A decoded Z80 instruction.  Holds the address it was decoded from,
/// the raw bytes that make it up, and a string mnemonic.  The
/// disassembler is the producer of these — the emulator does not use
/// it on the hot path (it dispatches on opcodes directly).
/// </summary>
public readonly record struct Z80Instruction(
    ushort Address,
    byte[] Bytes,
    string Mnemonic)
{
    public int Length => Bytes.Length;
    public ushort EndAddress => (ushort)(Address + Length);
}
