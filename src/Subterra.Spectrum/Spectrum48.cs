using Subterra.Spectrum.Z80;

namespace Subterra.Spectrum;

/// <summary>
/// A minimal 48 K ZX Spectrum: Z80 CPU + 16 K ROM at $0000-$3FFF + 48 K
/// RAM at $4000-$FFFF + ULA-port stub at port $FE.
///
/// "Minimal" means: we don't model memory contention, floating bus
/// behaviour, AY-3-8912 sound (Spectrum 48K has none anyway), or any
/// interface 1 / Kempston joystick — just enough to run a 48 K game
/// from a snapshot or fresh ROM boot and read its screen memory.
/// </summary>
public sealed class Spectrum48 : IZ80Bus
{
    public const int CpuFrequencyHz = 3_500_000;
    public const int TStatesPerFrame = 69888;   // 312 lines × 224 T-states

    private readonly byte[] _rom = new byte[0x4000];
    private readonly byte[] _ram = new byte[0xC000];

    public Z80Cpu Cpu { get; }

    /// <summary>Last value written to port $FE (border colour in low 3 bits).</summary>
    public byte LastUlaPortWrite { get; private set; }

    /// <summary>
    /// Keyboard half-row state. Index = half-row 0..7, value = 5 bits set
    /// to 1 for "released" / 0 for "pressed" (matching the bus convention).
    /// Bit 0 corresponds to the leftmost key in the row; bit 4 to the
    /// rightmost. Bits 5-7 are unused on a 48 K keyboard.
    /// </summary>
    public byte[] KeyHalfRows { get; } = { 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F };

    public Spectrum48(byte[] rom)
    {
        if (rom.Length != 0x4000)
        {
            throw new ArgumentException("ROM must be exactly 16384 bytes.", nameof(rom));
        }
        Array.Copy(rom, _rom, 0x4000);
        Cpu = new Z80Cpu(this);
    }

    /// <summary>Restore CPU and RAM state from a snapshot.</summary>
    public void LoadSnapshot(Z80Snapshot snap)
    {
        Array.Copy(snap.Ram48K, _ram, 0xC000);
        var r = snap.Registers;
        Cpu.AF = r.AF;
        Cpu.BC = r.BC;
        Cpu.DE = r.DE;
        Cpu.HL = r.HL;
        Cpu.AFp = r.AFp;
        Cpu.BCp = r.BCp;
        Cpu.DEp = r.DEp;
        Cpu.HLp = r.HLp;
        Cpu.IX = r.IX;
        Cpu.IY = r.IY;
        Cpu.PC = r.PC;
        Cpu.SP = r.SP;
        Cpu.I = r.I;
        Cpu.R = r.R;
        Cpu.InterruptMode = r.InterruptMode;
        Cpu.Iff1 = r.Iff1;
        Cpu.Iff2 = r.Iff2;
        LastUlaPortWrite = r.BorderColour;
    }

    public byte ReadMemory(ushort address)
    {
        return address < 0x4000 ? _rom[address] : _ram[address - 0x4000];
    }

    public void WriteMemory(ushort address, byte value)
    {
        if (address >= 0x4000)
        {
            _ram[address - 0x4000] = value;
        }
        // Writes to ROM are simply ignored.
    }

    public byte ReadPort(ushort port)
    {
        // ULA responds when A0 is low (port $FE family).
        if ((port & 0x0001) == 0)
        {
            // Bits 0..4: keyboard read for the half-row(s) selected by
            // A8..A15. A high address bit being 0 selects that half-row.
            byte result = 0x1F;
            byte selector = (byte)(port >> 8);
            for (int row = 0; row < 8; row++)
            {
                if ((selector & (1 << row)) == 0)
                {
                    result &= KeyHalfRows[row];
                }
            }
            // Bit 6: EAR input (we always report 1 = no signal).
            // Bit 5: always 1. Bit 7: always 1.
            result |= 0xA0;
            return result;
        }
        // Unhandled port: floating-bus is hard to model accurately. We
        // return 0xFF (the bus pulled-up state) which is fine for our
        // game.
        return 0xFF;
    }

    public void WritePort(ushort port, byte value)
    {
        if ((port & 0x0001) == 0)
        {
            LastUlaPortWrite = value;
        }
    }

    /// <summary>
    /// Run the CPU until at least <paramref name="tStates"/> T-states
    /// have elapsed. Returns the actual T-states executed (always
    /// ≥ tStates by at most one instruction's worth).
    /// </summary>
    public int Run(int tStates)
    {
        long target = Cpu.Cycles + tStates;
        int run = 0;
        while (Cpu.Cycles < target)
        {
            run += Cpu.Step();
        }
        return run;
    }

    /// <summary>
    /// Run one frame: roughly 69 888 T-states, with a maskable
    /// interrupt fired at the start. The Spectrum ULA raises INT
    /// for ~32 T-states at the very top of each frame.
    /// </summary>
    public int RunFrame()
    {
        Cpu.MaskableInterrupt();
        return Run(TStatesPerFrame);
    }

    /// <summary>Direct read into the snapshot-shaped 48 K RAM buffer
    /// (the same layout as <see cref="Z80Snapshot.Ram48K"/>).</summary>
    public ReadOnlySpan<byte> RamView() => _ram;
}
