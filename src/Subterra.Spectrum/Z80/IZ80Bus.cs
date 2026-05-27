namespace Subterra.Spectrum.Z80;

/// <summary>
/// The "world" a Z80 CPU talks to: a 16-bit memory space plus an
/// 8-bit I/O port space. Implementations decide what lives where
/// (e.g. ROM vs RAM mapping, ULA at port $FE, etc.).
/// </summary>
public interface IZ80Bus
{
    byte ReadMemory(ushort address);
    void WriteMemory(ushort address, byte value);

    /// <summary>
    /// On the Z80, IN/OUT instructions actually present a full 16-bit
    /// address on the bus (BC for <c>IN r,(C)</c> / <c>OUT (C),r</c>,
    /// or A·n for <c>IN A,(n)</c> / <c>OUT (n),A</c>). The Spectrum ULA
    /// decodes its presence based on A0 being zero, and uses A8-A15
    /// for keyboard half-row selection — so we always pass the full
    /// 16-bit port.
    /// </summary>
    byte ReadPort(ushort port);
    void WritePort(ushort port, byte value);
}
