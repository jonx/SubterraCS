namespace Subterra.Spectrum.Z80;

/// <summary>
/// Zilog Z80 CPU. Implements the documented instruction set in full
/// (main page + CB / ED / DD / FD / DD-CB / FD-CB prefixes), with
/// flag-correct ALU. Undocumented behaviour (IXH/IXL register access,
/// undocumented NEG variants, DD/FD chains, etc.) is implemented for
/// the cases we've observed in the wild; the F3/F5 "undocumented"
/// flag bits follow the documented "shadow of result" rule. Memory
/// timing (contention, IO timing) is not modelled — every instruction
/// just reports nominal T-states.
/// </summary>
public sealed class Z80Cpu
{
    public IZ80Bus Bus { get; }

    // 8-bit registers stored individually so we can hand them out by
    // ref where useful. We pair them into 16-bit views with properties.
    public byte A, F, B, C, D, E, H, L;
    public byte Ap, Fp, Bp, Cp, Dp, Ep, Hp, Lp;
    public ushort IX, IY, SP, PC;
    public byte I, R;
    public bool Iff1, Iff2;
    public byte InterruptMode;
    public bool Halted;

    /// <summary>Running cycle counter (T-states executed since reset).</summary>
    public long Cycles;

    /// <summary>If true, the next instruction won't honour a maskable
    /// interrupt (set by EI to defer interrupts by one instruction, as
    /// the real Z80 does).</summary>
    public bool DeferInterrupt;

    public ushort AF
    {
        get => (ushort)((A << 8) | F);
        set { A = (byte)(value >> 8); F = (byte)(value & 0xFF); }
    }
    public ushort BC
    {
        get => (ushort)((B << 8) | C);
        set { B = (byte)(value >> 8); C = (byte)(value & 0xFF); }
    }
    public ushort DE
    {
        get => (ushort)((D << 8) | E);
        set { D = (byte)(value >> 8); E = (byte)(value & 0xFF); }
    }
    public ushort HL
    {
        get => (ushort)((H << 8) | L);
        set { H = (byte)(value >> 8); L = (byte)(value & 0xFF); }
    }
    public ushort AFp
    {
        get => (ushort)((Ap << 8) | Fp);
        set { Ap = (byte)(value >> 8); Fp = (byte)(value & 0xFF); }
    }
    public ushort BCp
    {
        get => (ushort)((Bp << 8) | Cp);
        set { Bp = (byte)(value >> 8); Cp = (byte)(value & 0xFF); }
    }
    public ushort DEp
    {
        get => (ushort)((Dp << 8) | Ep);
        set { Dp = (byte)(value >> 8); Ep = (byte)(value & 0xFF); }
    }
    public ushort HLp
    {
        get => (ushort)((Hp << 8) | Lp);
        set { Hp = (byte)(value >> 8); Lp = (byte)(value & 0xFF); }
    }

    // Flag bit layout (standard Z80):
    //   bit 7 S   sign
    //   bit 6 Z   zero
    //   bit 5 F5  shadow of result bit 5
    //   bit 4 H   half-carry (carry from/to bit 3)
    //   bit 3 F3  shadow of result bit 3
    //   bit 2 PV  parity (logical) / overflow (arithmetic)
    //   bit 1 N   1 if last op was subtraction
    //   bit 0 C   carry
    public const byte FlagS  = 0x80;
    public const byte FlagZ  = 0x40;
    public const byte FlagF5 = 0x20;
    public const byte FlagH  = 0x10;
    public const byte FlagF3 = 0x08;
    public const byte FlagPV = 0x04;
    public const byte FlagN  = 0x02;
    public const byte FlagC  = 0x01;

    private static readonly byte[] ParityTable = BuildParityTable();

    private static byte[] BuildParityTable()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int bits = 0;
            int v = i;
            while (v != 0) { bits ^= v & 1; v >>= 1; }
            table[i] = bits == 0 ? FlagPV : (byte)0;
        }
        return table;
    }

    public Z80Cpu(IZ80Bus bus)
    {
        Bus = bus;
        ResetState();
    }

    public void ResetState()
    {
        A = F = B = C = D = E = H = L = 0xFF;
        Ap = Fp = Bp = Cp = Dp = Ep = Hp = Lp = 0xFF;
        IX = IY = 0xFFFF;
        SP = 0xFFFF;
        PC = 0;
        I = R = 0;
        Iff1 = Iff2 = false;
        InterruptMode = 0;
        Halted = false;
        Cycles = 0;
    }

    /// <summary>
    /// Trigger a maskable interrupt (IM 0 / 1 / 2). The Spectrum ULA
    /// raises this at the start of each video frame.  Returns true if
    /// the interrupt was accepted (IFF1 was set and the previous
    /// instruction wasn't EI).
    /// </summary>
    public bool MaskableInterrupt()
    {
        if (!Iff1 || DeferInterrupt)
        {
            return false;
        }
        if (Halted)
        {
            // Coming out of HALT: the real Z80 stalls PC at the HALT
            // opcode (executing NOPs in place). On interrupt acceptance
            // we must advance past it before pushing the return address.
            PC = (ushort)(PC + 1);
        }
        Halted = false;
        Iff1 = Iff2 = false;
        Push(PC);
        switch (InterruptMode)
        {
            case 0: // IM 0 takes the instruction from the bus; on Spectrum
                    // the bus has $FF, which decodes to RST $38.
                PC = 0x0038;
                Cycles += 13;
                break;
            case 1:
                PC = 0x0038;
                Cycles += 13;
                break;
            case 2:
                ushort vector = (ushort)((I << 8) | 0xFF);
                ushort handler = (ushort)(Bus.ReadMemory(vector) | (Bus.ReadMemory((ushort)(vector + 1)) << 8));
                PC = handler;
                Cycles += 19;
                break;
        }
        return true;
    }

    // --- Memory helpers --------------------------------------------------

    private byte FetchByte()
    {
        byte b = Bus.ReadMemory(PC);
        PC = (ushort)(PC + 1);
        return b;
    }

    private ushort FetchWord()
    {
        byte lo = FetchByte();
        byte hi = FetchByte();
        return (ushort)((hi << 8) | lo);
    }

    private byte ReadMem(ushort addr) => Bus.ReadMemory(addr);
    private void WriteMem(ushort addr, byte value) => Bus.WriteMemory(addr, value);

    private ushort ReadMem16(ushort addr)
    {
        byte lo = Bus.ReadMemory(addr);
        byte hi = Bus.ReadMemory((ushort)(addr + 1));
        return (ushort)((hi << 8) | lo);
    }

    private void WriteMem16(ushort addr, ushort value)
    {
        Bus.WriteMemory(addr, (byte)(value & 0xFF));
        Bus.WriteMemory((ushort)(addr + 1), (byte)(value >> 8));
    }

    private void Push(ushort value)
    {
        SP = (ushort)(SP - 1);
        Bus.WriteMemory(SP, (byte)(value >> 8));
        SP = (ushort)(SP - 1);
        Bus.WriteMemory(SP, (byte)(value & 0xFF));
    }

    private ushort Pop()
    {
        byte lo = Bus.ReadMemory(SP);
        SP = (ushort)(SP + 1);
        byte hi = Bus.ReadMemory(SP);
        SP = (ushort)(SP + 1);
        return (ushort)((hi << 8) | lo);
    }

    // --- Flag setters ---------------------------------------------------

    private void SetSZ53P(byte value)
    {
        F = (byte)((F & FlagC)
            | (value == 0 ? FlagZ : 0)
            | (value & (FlagS | FlagF5 | FlagF3))
            | ParityTable[value]);
    }

    private void SetSZ53(byte value)
    {
        F = (byte)((F & (FlagC | FlagPV | FlagN))
            | (value == 0 ? FlagZ : 0)
            | (value & (FlagS | FlagF5 | FlagF3)));
    }

    private byte Add8(byte a, byte b, int carry = 0)
    {
        int sum = a + b + carry;
        byte r = (byte)(sum & 0xFF);
        byte flags = (byte)(r & (FlagS | FlagF5 | FlagF3));
        if (r == 0) flags |= FlagZ;
        if (((a & 0xF) + (b & 0xF) + carry) > 0xF) flags |= FlagH;
        // Overflow: two-operand-signs-equal-but-differ-from-result.
        if ((~(a ^ b) & (a ^ r) & 0x80) != 0) flags |= FlagPV;
        if (sum > 0xFF) flags |= FlagC;
        F = flags;
        return r;
    }

    private byte Sub8(byte a, byte b, int carry = 0)
    {
        int diff = a - b - carry;
        byte r = (byte)(diff & 0xFF);
        byte flags = (byte)((r & (FlagS | FlagF5 | FlagF3)) | FlagN);
        if (r == 0) flags |= FlagZ;
        if (((a & 0xF) - (b & 0xF) - carry) < 0) flags |= FlagH;
        if (((a ^ b) & (a ^ r) & 0x80) != 0) flags |= FlagPV;
        if (diff < 0) flags |= FlagC;
        F = flags;
        return r;
    }

    private void Cp8(byte a, byte b)
    {
        // CP is SUB without storing the result. Flag bits 3 and 5 come
        // from the *operand*, not the result, per Zilog.
        int diff = a - b;
        byte r = (byte)(diff & 0xFF);
        byte flags = (byte)((r & FlagS) | FlagN);
        if (r == 0) flags |= FlagZ;
        if (((a & 0xF) - (b & 0xF)) < 0) flags |= FlagH;
        if (((a ^ b) & (a ^ r) & 0x80) != 0) flags |= FlagPV;
        if (diff < 0) flags |= FlagC;
        flags |= (byte)(b & (FlagF3 | FlagF5));
        F = flags;
    }

    private byte And8(byte a, byte b)
    {
        byte r = (byte)(a & b);
        F = (byte)(FlagH | ParityTable[r] | (r & (FlagS | FlagF5 | FlagF3)) | (r == 0 ? FlagZ : 0));
        return r;
    }

    private byte Or8(byte a, byte b)
    {
        byte r = (byte)(a | b);
        F = (byte)(ParityTable[r] | (r & (FlagS | FlagF5 | FlagF3)) | (r == 0 ? FlagZ : 0));
        return r;
    }

    private byte Xor8(byte a, byte b)
    {
        byte r = (byte)(a ^ b);
        F = (byte)(ParityTable[r] | (r & (FlagS | FlagF5 | FlagF3)) | (r == 0 ? FlagZ : 0));
        return r;
    }

    private byte Inc8(byte v)
    {
        byte r = (byte)(v + 1);
        byte flags = (byte)((F & FlagC) | (r & (FlagS | FlagF5 | FlagF3)));
        if (r == 0) flags |= FlagZ;
        if ((v & 0xF) == 0xF) flags |= FlagH;
        if (v == 0x7F) flags |= FlagPV;
        F = flags;
        return r;
    }

    private byte Dec8(byte v)
    {
        byte r = (byte)(v - 1);
        byte flags = (byte)((F & FlagC) | (r & (FlagS | FlagF5 | FlagF3)) | FlagN);
        if (r == 0) flags |= FlagZ;
        if ((v & 0xF) == 0x00) flags |= FlagH;
        if (v == 0x80) flags |= FlagPV;
        F = flags;
        return r;
    }

    private ushort AddHL16(ushort hl, ushort v)
    {
        int sum = hl + v;
        ushort r = (ushort)sum;
        byte flags = (byte)(F & (FlagS | FlagZ | FlagPV));
        if (((hl & 0x0FFF) + (v & 0x0FFF)) > 0x0FFF) flags |= FlagH;
        if (sum > 0xFFFF) flags |= FlagC;
        flags |= (byte)((r >> 8) & (FlagF5 | FlagF3));
        F = flags;
        return r;
    }

    private ushort Adc16(ushort hl, ushort v)
    {
        int carry = (F & FlagC) != 0 ? 1 : 0;
        int sum = hl + v + carry;
        ushort r = (ushort)sum;
        byte flags = 0;
        if (r == 0) flags |= FlagZ;
        flags |= (byte)((r >> 8) & (FlagS | FlagF5 | FlagF3));
        if (((hl & 0x0FFF) + (v & 0x0FFF) + carry) > 0x0FFF) flags |= FlagH;
        if (sum > 0xFFFF) flags |= FlagC;
        if ((~(hl ^ v) & (hl ^ r) & 0x8000) != 0) flags |= FlagPV;
        F = flags;
        return r;
    }

    private ushort Sbc16(ushort hl, ushort v)
    {
        int carry = (F & FlagC) != 0 ? 1 : 0;
        int diff = hl - v - carry;
        ushort r = (ushort)diff;
        byte flags = FlagN;
        if (r == 0) flags |= FlagZ;
        flags |= (byte)((r >> 8) & (FlagS | FlagF5 | FlagF3));
        if (((hl & 0x0FFF) - (v & 0x0FFF) - carry) < 0) flags |= FlagH;
        if (diff < 0) flags |= FlagC;
        if (((hl ^ v) & (hl ^ r) & 0x8000) != 0) flags |= FlagPV;
        F = flags;
        return r;
    }

    // Rotates / shifts. Each returns the result and sets flags fully
    // (P/V = parity, others standard). Used by both the A-only RLCA/etc.
    // variants and the general CB-prefix ones.

    private byte RotateLeftCircular(byte v, bool fullFlags)
    {
        byte r = (byte)(((v << 1) | (v >> 7)) & 0xFF);
        F = (byte)((F & ~(FlagH | FlagN | FlagC | FlagS | FlagZ | FlagPV | FlagF5 | FlagF3))
            | ((v & 0x80) != 0 ? FlagC : 0)
            | (r & (FlagF5 | FlagF3)));
        if (fullFlags)
        {
            F |= (byte)(ParityTable[r] | (r & FlagS) | (r == 0 ? FlagZ : 0));
        }
        else
        {
            F |= (byte)(A & (FlagF5 | FlagF3));
        }
        return r;
    }

    private byte RotateRightCircular(byte v, bool fullFlags)
    {
        byte r = (byte)(((v >> 1) | (v << 7)) & 0xFF);
        F = (byte)((F & ~(FlagH | FlagN | FlagC | FlagS | FlagZ | FlagPV | FlagF5 | FlagF3))
            | ((v & 0x01) != 0 ? FlagC : 0)
            | (r & (FlagF5 | FlagF3)));
        if (fullFlags)
        {
            F |= (byte)(ParityTable[r] | (r & FlagS) | (r == 0 ? FlagZ : 0));
        }
        return r;
    }

    private byte RotateLeftThroughCarry(byte v, bool fullFlags)
    {
        int oldCarry = (F & FlagC) != 0 ? 1 : 0;
        byte r = (byte)(((v << 1) | oldCarry) & 0xFF);
        F = (byte)((F & ~(FlagH | FlagN | FlagC | FlagS | FlagZ | FlagPV | FlagF5 | FlagF3))
            | ((v & 0x80) != 0 ? FlagC : 0)
            | (r & (FlagF5 | FlagF3)));
        if (fullFlags)
        {
            F |= (byte)(ParityTable[r] | (r & FlagS) | (r == 0 ? FlagZ : 0));
        }
        return r;
    }

    private byte RotateRightThroughCarry(byte v, bool fullFlags)
    {
        int oldCarry = (F & FlagC) != 0 ? 1 : 0;
        byte r = (byte)(((v >> 1) | (oldCarry << 7)) & 0xFF);
        F = (byte)((F & ~(FlagH | FlagN | FlagC | FlagS | FlagZ | FlagPV | FlagF5 | FlagF3))
            | ((v & 0x01) != 0 ? FlagC : 0)
            | (r & (FlagF5 | FlagF3)));
        if (fullFlags)
        {
            F |= (byte)(ParityTable[r] | (r & FlagS) | (r == 0 ? FlagZ : 0));
        }
        return r;
    }

    private byte ShiftLeftArithmetic(byte v)
    {
        byte r = (byte)((v << 1) & 0xFE);
        F = (byte)(ParityTable[r] | (r & (FlagS | FlagF5 | FlagF3))
            | (r == 0 ? FlagZ : 0)
            | ((v & 0x80) != 0 ? FlagC : 0));
        return r;
    }

    private byte ShiftRightArithmetic(byte v)
    {
        byte r = (byte)((v >> 1) | (v & 0x80));
        F = (byte)(ParityTable[r] | (r & (FlagS | FlagF5 | FlagF3))
            | (r == 0 ? FlagZ : 0)
            | ((v & 0x01) != 0 ? FlagC : 0));
        return r;
    }

    private byte ShiftLeftLogical(byte v)
    {
        // SLL is undocumented but commonly used; it shifts a 1 into bit 0.
        byte r = (byte)((v << 1) | 0x01);
        F = (byte)(ParityTable[r] | (r & (FlagS | FlagF5 | FlagF3))
            | (r == 0 ? FlagZ : 0)
            | ((v & 0x80) != 0 ? FlagC : 0));
        return r;
    }

    private byte ShiftRightLogical(byte v)
    {
        byte r = (byte)(v >> 1);
        F = (byte)(ParityTable[r] | (r & (FlagS | FlagF5 | FlagF3))
            | (r == 0 ? FlagZ : 0)
            | ((v & 0x01) != 0 ? FlagC : 0));
        return r;
    }

    private void Bit(int bit, byte v, byte address35)
    {
        byte mask = (byte)(1 << bit);
        byte r = (byte)(v & mask);
        byte flags = (byte)((F & FlagC) | FlagH | (r == 0 ? FlagZ | FlagPV : 0));
        if (bit == 7 && r != 0) flags |= FlagS;
        flags |= (byte)(address35 & (FlagF5 | FlagF3));
        F = flags;
    }

    private void Daa()
    {
        int a = A;
        int adjust = 0;
        bool carry = (F & FlagC) != 0;
        bool half = (F & FlagH) != 0;
        bool subtract = (F & FlagN) != 0;

        if (half || (!subtract && (a & 0xF) > 9)) adjust |= 0x06;
        if (carry || (!subtract && a > 0x99))
        {
            adjust |= 0x60;
            carry = true;
        }
        int result = subtract ? a - adjust : a + adjust;
        byte r = (byte)(result & 0xFF);
        byte flags = (byte)(F & FlagN);
        flags |= (byte)(r & (FlagS | FlagF5 | FlagF3));
        if (r == 0) flags |= FlagZ;
        if (((a ^ r) & 0x10) != 0) flags |= FlagH;
        flags |= ParityTable[r];
        if (carry) flags |= FlagC;
        A = r;
        F = flags;
    }

    // --- Instruction dispatch ------------------------------------------

    /// <summary>
    /// Run one instruction (handling all prefixes). Returns the number
    /// of T-states consumed.
    /// </summary>
    public int Step()
    {
        if (Halted)
        {
            R = (byte)((R & 0x80) | ((R + 1) & 0x7F));
            Cycles += 4;
            return 4;
        }
        DeferInterrupt = false;
        long start = Cycles;
        byte op = FetchOpcode();
        switch (op)
        {
            case 0xCB: ExecuteCB(0, false); break;
            case 0xED: ExecuteED(); break;
            case 0xDD: ExecuteIndex(ref IX); break;
            case 0xFD: ExecuteIndex(ref IY); break;
            default:   ExecuteMain(op); break;
        }
        return (int)(Cycles - start);
    }

    private byte FetchOpcode()
    {
        // M1 cycle: 4 T-states for opcode fetch (plus 1 for R refresh).
        byte op = Bus.ReadMemory(PC);
        PC = (ushort)(PC + 1);
        R = (byte)((R & 0x80) | ((R + 1) & 0x7F));
        Cycles += 4;
        return op;
    }

    private bool ConditionMet(int cc)
    {
        return cc switch
        {
            0 => (F & FlagZ) == 0,
            1 => (F & FlagZ) != 0,
            2 => (F & FlagC) == 0,
            3 => (F & FlagC) != 0,
            4 => (F & FlagPV) == 0,
            5 => (F & FlagPV) != 0,
            6 => (F & FlagS) == 0,
            _ => (F & FlagS) != 0,
        };
    }

    private byte GetReg(int idx) => idx switch
    {
        0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L,
        6 => Bus.ReadMemory(HL),
        _ => A,
    };

    private void SetReg(int idx, byte value)
    {
        switch (idx)
        {
            case 0: B = value; break;
            case 1: C = value; break;
            case 2: D = value; break;
            case 3: E = value; break;
            case 4: H = value; break;
            case 5: L = value; break;
            case 6: Bus.WriteMemory(HL, value); break;
            default: A = value; break;
        }
    }

    private void Alu(int op, byte v)
    {
        switch (op)
        {
            case 0: A = Add8(A, v); break;
            case 1: A = Add8(A, v, (F & FlagC) != 0 ? 1 : 0); break;
            case 2: A = Sub8(A, v); break;
            case 3: A = Sub8(A, v, (F & FlagC) != 0 ? 1 : 0); break;
            case 4: A = And8(A, v); break;
            case 5: A = Xor8(A, v); break;
            case 6: A = Or8(A, v); break;
            case 7: Cp8(A, v); break;
        }
    }

    private void ExecuteMain(byte op)
    {
        int x = (op >> 6) & 3;
        int y = (op >> 3) & 7;
        int z = op & 7;
        int p = y >> 1;
        int q = y & 1;

        if (x == 1) // LD r,r' (and HALT)
        {
            if (op == 0x76)
            {
                Halted = true;
                PC = (ushort)(PC - 1);
                return;
            }
            byte v = GetReg(z);
            SetReg(y, v);
            if (y == 6 || z == 6) Cycles += 3;
            return;
        }
        if (x == 2) // ALU
        {
            byte v = GetReg(z);
            if (z == 6) Cycles += 3;
            Alu(y, v);
            return;
        }

        if (x == 0)
        {
            switch (z)
            {
                case 0:
                    switch (y)
                    {
                        case 0: /* NOP */ return;
                        case 1: // EX AF,AF'
                            (A, Ap) = (Ap, A);
                            (F, Fp) = (Fp, F);
                            return;
                        case 2: // DJNZ d
                            B = (byte)(B - 1);
                            { sbyte d = (sbyte)FetchByte(); if (B != 0) { PC = (ushort)(PC + d); Cycles += 5; } Cycles += 4; }
                            return;
                        case 3: // JR d
                            { sbyte d = (sbyte)FetchByte(); PC = (ushort)(PC + d); Cycles += 8; }
                            return;
                        default:
                            { sbyte d = (sbyte)FetchByte();
                              if (ConditionMet(y - 4)) { PC = (ushort)(PC + d); Cycles += 8; }
                              else Cycles += 3; }
                            return;
                    }
                case 1:
                    if (q == 0)
                    {
                        ushort v = FetchWord();
                        SetRP(p, v);
                        Cycles += 6;
                    }
                    else
                    {
                        ushort v = GetRP(p);
                        HL = AddHL16(HL, v);
                        Cycles += 7;
                    }
                    return;
                case 2:
                    switch (y)
                    {
                        case 0: Bus.WriteMemory(BC, A); Cycles += 3; return;
                        case 1: A = Bus.ReadMemory(BC); Cycles += 3; return;
                        case 2: Bus.WriteMemory(DE, A); Cycles += 3; return;
                        case 3: A = Bus.ReadMemory(DE); Cycles += 3; return;
                        case 4: { ushort a = FetchWord(); WriteMem16(a, HL); Cycles += 12; } return;
                        case 5: { ushort a = FetchWord(); HL = ReadMem16(a); Cycles += 12; } return;
                        case 6: { ushort a = FetchWord(); Bus.WriteMemory(a, A); Cycles += 9; } return;
                        case 7: { ushort a = FetchWord(); A = Bus.ReadMemory(a); Cycles += 9; } return;
                    }
                    return;
                case 3:
                    if (q == 0) { SetRP(p, (ushort)(GetRP(p) + 1)); Cycles += 2; }
                    else        { SetRP(p, (ushort)(GetRP(p) - 1)); Cycles += 2; }
                    return;
                case 4:
                    {
                        byte v = GetReg(y);
                        if (y == 6) Cycles += 7;
                        byte r = Inc8(v);
                        SetReg(y, r);
                    }
                    return;
                case 5:
                    {
                        byte v = GetReg(y);
                        if (y == 6) Cycles += 7;
                        byte r = Dec8(v);
                        SetReg(y, r);
                    }
                    return;
                case 6:
                    {
                        byte n = FetchByte();
                        SetReg(y, n);
                        if (y == 6) Cycles += 6;
                        else Cycles += 3;
                    }
                    return;
                case 7:
                    switch (y)
                    {
                        case 0: A = RotateLeftCircular(A, false); return;
                        case 1: A = RotateRightCircular(A, false); return;
                        case 2: A = RotateLeftThroughCarry(A, false); return;
                        case 3: A = RotateRightThroughCarry(A, false); return;
                        case 4: Daa(); return;
                        case 5: // CPL
                            A = (byte)(A ^ 0xFF);
                            F = (byte)((F & (FlagS | FlagZ | FlagPV | FlagC))
                                | FlagH | FlagN
                                | (A & (FlagF5 | FlagF3)));
                            return;
                        case 6: // SCF
                            F = (byte)((F & (FlagS | FlagZ | FlagPV))
                                | FlagC
                                | (A & (FlagF5 | FlagF3)));
                            return;
                        case 7: // CCF
                            {
                                byte oldC = (byte)(F & FlagC);
                                F = (byte)((F & (FlagS | FlagZ | FlagPV))
                                    | (oldC != 0 ? FlagH : FlagC)
                                    | (A & (FlagF5 | FlagF3)));
                            }
                            return;
                    }
                    return;
            }
        }

        if (x == 3)
        {
            switch (z)
            {
                case 0: // RET cc
                    if (ConditionMet(y)) { PC = Pop(); Cycles += 7; }
                    else                  Cycles += 1;
                    return;
                case 1:
                    if (q == 0) { SetRP2(p, Pop()); Cycles += 6; }
                    else switch (p)
                    {
                        case 0: PC = Pop(); Cycles += 6; return;
                        case 1: // EXX
                            (B, Bp) = (Bp, B);
                            (C, Cp) = (Cp, C);
                            (D, Dp) = (Dp, D);
                            (E, Ep) = (Ep, E);
                            (H, Hp) = (Hp, H);
                            (L, Lp) = (Lp, L);
                            return;
                        case 2: PC = HL; return;        // JP (HL)
                        case 3: SP = HL; Cycles += 2; return; // LD SP,HL
                    }
                    return;
                case 2: // JP cc, nn
                    {
                        ushort a = FetchWord();
                        if (ConditionMet(y)) PC = a;
                        Cycles += 6;
                    }
                    return;
                case 3:
                    switch (y)
                    {
                        case 0: PC = FetchWord(); Cycles += 6; return;
                        case 1: ExecuteCB(0, false); return; // shouldn't reach
                        case 2: // OUT (n),A
                            {
                                byte n = FetchByte();
                                ushort port = (ushort)((A << 8) | n);
                                Bus.WritePort(port, A);
                                Cycles += 7;
                            }
                            return;
                        case 3: // IN A,(n)
                            {
                                byte n = FetchByte();
                                ushort port = (ushort)((A << 8) | n);
                                A = Bus.ReadPort(port);
                                Cycles += 7;
                            }
                            return;
                        case 4: // EX (SP),HL
                            {
                                ushort tmp = ReadMem16(SP);
                                WriteMem16(SP, HL);
                                HL = tmp;
                                Cycles += 15;
                            }
                            return;
                        case 5: // EX DE,HL
                            (D, H) = (H, D);
                            (E, L) = (L, E);
                            return;
                        case 6: Iff1 = Iff2 = false; return; // DI
                        case 7: Iff1 = Iff2 = true; DeferInterrupt = true; return; // EI
                    }
                    return;
                case 4: // CALL cc, nn
                    {
                        ushort a = FetchWord();
                        if (ConditionMet(y)) { Push(PC); PC = a; Cycles += 13; }
                        else                  Cycles += 6;
                    }
                    return;
                case 5:
                    if (q == 0) { Push(GetRP2(p)); Cycles += 7; return; }
                    if (p == 0) { ushort a = FetchWord(); Push(PC); PC = a; Cycles += 13; return; }
                    return; // other p values are prefixes, handled above
                case 6:
                    {
                        byte n = FetchByte();
                        Alu(y, n);
                        Cycles += 3;
                    }
                    return;
                case 7: // RST y*8
                    Push(PC);
                    PC = (ushort)(y * 8);
                    Cycles += 7;
                    return;
            }
        }
    }

    private ushort GetRP(int p) => p switch
    {
        0 => BC, 1 => DE, 2 => HL, _ => SP,
    };

    private void SetRP(int p, ushort v)
    {
        switch (p)
        {
            case 0: BC = v; break;
            case 1: DE = v; break;
            case 2: HL = v; break;
            default: SP = v; break;
        }
    }

    private ushort GetRP2(int p) => p switch
    {
        0 => BC, 1 => DE, 2 => HL, _ => AF,
    };

    private void SetRP2(int p, ushort v)
    {
        switch (p)
        {
            case 0: BC = v; break;
            case 1: DE = v; break;
            case 2: HL = v; break;
            default: AF = v; break;
        }
    }

    // --- CB-prefix opcodes (rot/shift/bit) -----------------------------

    private void ExecuteCB(sbyte displacement, bool indexed, ushort indexBase = 0)
    {
        byte op = FetchOpcode();
        int x = (op >> 6) & 3;
        int y = (op >> 3) & 7;
        int z = op & 7;

        byte v;
        ushort addr = 0;
        bool isMem = z == 6 || indexed;
        if (indexed)
        {
            addr = (ushort)(indexBase + displacement);
            v = Bus.ReadMemory(addr);
        }
        else if (z == 6)
        {
            addr = HL;
            v = Bus.ReadMemory(addr);
        }
        else
        {
            v = GetReg(z);
        }

        byte r = 0;
        switch (x)
        {
            case 0:
                r = y switch
                {
                    0 => RotateLeftCircular(v, true),
                    1 => RotateRightCircular(v, true),
                    2 => RotateLeftThroughCarry(v, true),
                    3 => RotateRightThroughCarry(v, true),
                    4 => ShiftLeftArithmetic(v),
                    5 => ShiftRightArithmetic(v),
                    6 => ShiftLeftLogical(v),
                    _ => ShiftRightLogical(v),
                };
                break;
            case 1:
                {
                    byte hiByte = isMem ? (byte)((addr >> 8) & 0xFF) : v;
                    Bit(y, v, hiByte);
                }
                break;
            case 2:
                r = (byte)(v & ~(1 << y));
                break;
            case 3:
                r = (byte)(v | (1 << y));
                break;
        }

        if (x != 1)
        {
            if (indexed)
            {
                Bus.WriteMemory(addr, r);
                if (z != 6) SetReg(z, r); // undocumented store-through
            }
            else if (z == 6)
            {
                Bus.WriteMemory(addr, r);
            }
            else
            {
                SetReg(z, r);
            }
        }

        if (isMem) Cycles += 11;
    }

    // --- ED-prefix opcodes ---------------------------------------------

    private void ExecuteED()
    {
        byte op = FetchOpcode();
        int x = (op >> 6) & 3;
        int y = (op >> 3) & 7;
        int z = op & 7;
        int p = y >> 1;
        int q = y & 1;

        if (x == 1)
        {
            switch (z)
            {
                case 0: // IN r,(C)
                    {
                        byte v = Bus.ReadPort(BC);
                        F = (byte)((F & FlagC) | ParityTable[v] | (v & (FlagS | FlagF5 | FlagF3)) | (v == 0 ? FlagZ : 0));
                        if (y != 6) SetReg(y, v);
                        Cycles += 4;
                    }
                    return;
                case 1: // OUT (C),r
                    {
                        byte v = y == 6 ? (byte)0 : GetReg(y);
                        Bus.WritePort(BC, v);
                        Cycles += 4;
                    }
                    return;
                case 2:
                    if (q == 0) HL = Sbc16(HL, GetRP(p));
                    else        HL = Adc16(HL, GetRP(p));
                    Cycles += 11;
                    return;
                case 3:
                    {
                        ushort a = FetchWord();
                        if (q == 0) WriteMem16(a, GetRP(p));
                        else        SetRP(p, ReadMem16(a));
                        Cycles += 12;
                    }
                    return;
                case 4: // NEG
                    {
                        byte v = A;
                        A = Sub8(0, v);
                    }
                    return;
                case 5: // RETN / RETI
                    PC = Pop();
                    Iff1 = Iff2;
                    Cycles += 6;
                    return;
                case 6:
                    InterruptMode = y switch
                    {
                        2 or 6 => 1,
                        3 or 7 => 2,
                        _ => 0,
                    };
                    return;
                case 7:
                    switch (y)
                    {
                        case 0: I = A; Cycles += 1; return;
                        case 1: R = A; Cycles += 1; return;
                        case 2: A = I;
                            F = (byte)((F & FlagC) | (A & (FlagS | FlagF5 | FlagF3))
                                | (A == 0 ? FlagZ : 0)
                                | (Iff2 ? FlagPV : 0));
                            Cycles += 1; return;
                        case 3: A = R;
                            F = (byte)((F & FlagC) | (A & (FlagS | FlagF5 | FlagF3))
                                | (A == 0 ? FlagZ : 0)
                                | (Iff2 ? FlagPV : 0));
                            Cycles += 1; return;
                        case 4: // RRD
                            {
                                byte v = Bus.ReadMemory(HL);
                                byte newA = (byte)((A & 0xF0) | (v & 0x0F));
                                byte newM = (byte)((v >> 4) | ((A & 0x0F) << 4));
                                Bus.WriteMemory(HL, newM);
                                A = newA;
                                F = (byte)((F & FlagC) | ParityTable[A] | (A & (FlagS | FlagF5 | FlagF3)) | (A == 0 ? FlagZ : 0));
                                Cycles += 10;
                            }
                            return;
                        case 5: // RLD
                            {
                                byte v = Bus.ReadMemory(HL);
                                byte newM = (byte)(((v << 4) | (A & 0x0F)) & 0xFF);
                                byte newA = (byte)((A & 0xF0) | (v >> 4));
                                Bus.WriteMemory(HL, newM);
                                A = newA;
                                F = (byte)((F & FlagC) | ParityTable[A] | (A & (FlagS | FlagF5 | FlagF3)) | (A == 0 ? FlagZ : 0));
                                Cycles += 10;
                            }
                            return;
                    }
                    return;
            }
        }

        if (x == 2 && y >= 4 && z <= 3)
        {
            int code = (y - 4) * 4 + z;
            ExecuteBlock(code);
            return;
        }
        // Anything else: NOP.
    }

    private void ExecuteBlock(int code)
    {
        // code = 0..15 ->  LDI, CPI, INI, OUTI,
        //                  LDD, CPD, IND, OUTD,
        //                  LDIR, CPIR, INIR, OTIR,
        //                  LDDR, CPDR, INDR, OTDR
        int dir = (code & 0x4) == 0 ? 1 : -1;       // INC vs DEC
        bool repeat = (code & 0x8) != 0;
        int kind = code & 0x3;                       // LD / CP / IN / OUT

        bool again = false;
        switch (kind)
        {
            case 0: // LD
                {
                    byte v = Bus.ReadMemory(HL);
                    Bus.WriteMemory(DE, v);
                    HL = (ushort)(HL + dir);
                    DE = (ushort)(DE + dir);
                    BC = (ushort)(BC - 1);
                    byte n = (byte)(v + A);
                    F = (byte)((F & (FlagS | FlagZ | FlagC))
                        | (n & FlagF3)
                        | ((n & 0x02) != 0 ? FlagF5 : 0)
                        | (BC != 0 ? FlagPV : 0));
                    again = repeat && BC != 0;
                    Cycles += 8;
                }
                break;
            case 1: // CP
                {
                    byte v = Bus.ReadMemory(HL);
                    Cp8(A, v);
                    byte n = (byte)(A - v - ((F & FlagH) != 0 ? 1 : 0));
                    F = (byte)((F & ~(FlagF3 | FlagF5 | FlagPV))
                        | (n & FlagF3)
                        | ((n & 0x02) != 0 ? FlagF5 : 0)
                        | (BC - 1 != 0 ? FlagPV : 0));
                    HL = (ushort)(HL + dir);
                    BC = (ushort)(BC - 1);
                    again = repeat && BC != 0 && (F & FlagZ) == 0;
                    Cycles += 8;
                }
                break;
            case 2: // IN
                {
                    byte v = Bus.ReadPort(BC);
                    Bus.WriteMemory(HL, v);
                    B = (byte)(B - 1);
                    HL = (ushort)(HL + dir);
                    F = (byte)((F & FlagC) | FlagN | (B == 0 ? FlagZ : 0) | (B & (FlagS | FlagF5 | FlagF3)));
                    again = repeat && B != 0;
                    Cycles += 8;
                }
                break;
            case 3: // OUT
                {
                    byte v = Bus.ReadMemory(HL);
                    Bus.WritePort(BC, v);
                    B = (byte)(B - 1);
                    HL = (ushort)(HL + dir);
                    F = (byte)((F & FlagC) | FlagN | (B == 0 ? FlagZ : 0) | (B & (FlagS | FlagF5 | FlagF3)));
                    again = repeat && B != 0;
                    Cycles += 8;
                }
                break;
        }
        if (again)
        {
            PC = (ushort)(PC - 2);
            Cycles += 5;
        }
    }

    // --- DD / FD prefix (IX / IY) --------------------------------------

    private void ExecuteIndex(ref ushort idx)
    {
        byte op = FetchOpcode();
        if (op == 0xCB)
        {
            sbyte d = (sbyte)FetchByte();
            ExecuteCB(d, true, idx);
            return;
        }
        if (op == 0xDD || op == 0xED || op == 0xFD)
        {
            // Treat consecutive prefix as a 4-T-state NOP that re-enters
            // dispatch on the next byte. We back up PC by 1 so the next
            // Step() picks the prefix up again.
            PC = (ushort)(PC - 1);
            return;
        }

        // Indexed forms: most main-page instructions are reinterpreted
        // with HL → idx and (HL) → (idx + d). We dispatch the few common
        // patterns directly below; anything not matched falls through to
        // the un-prefixed decoder at the bottom.
        int x = (op >> 6) & 3;
        int y = (op >> 3) & 7;
        int z = op & 7;

        if (op == 0xE1) { idx = Pop(); Cycles += 6; return; }
        if (op == 0xE5) { Push(idx); Cycles += 7; return; }
        if (op == 0xE9) { PC = idx; return; }
        if (op == 0xF9) { SP = idx; Cycles += 2; return; }
        if (op == 0xE3) // EX (SP),IX/IY
        {
            ushort tmp = ReadMem16(SP);
            WriteMem16(SP, idx);
            idx = tmp;
            Cycles += 15;
            return;
        }
        if (op == 0x21) { idx = FetchWord(); Cycles += 6; return; }
        if (op == 0x22) { ushort a = FetchWord(); WriteMem16(a, idx); Cycles += 12; return; }
        if (op == 0x2A) { ushort a = FetchWord(); idx = ReadMem16(a); Cycles += 12; return; }
        if (op == 0x23) { idx = (ushort)(idx + 1); Cycles += 2; return; }
        if (op == 0x2B) { idx = (ushort)(idx - 1); Cycles += 2; return; }
        if (op == 0x09 || op == 0x19 || op == 0x29 || op == 0x39)
        {
            ushort v = ((op >> 4) & 0x3) switch
            {
                0 => BC,
                1 => DE,
                2 => idx,
                _ => SP,
            };
            idx = AddHL16(idx, v);
            Cycles += 7;
            return;
        }
        if (op == 0x34) // INC (idx+d)
        {
            sbyte d = (sbyte)FetchByte();
            ushort addr = (ushort)(idx + d);
            byte v = Bus.ReadMemory(addr);
            byte r = Inc8(v);
            Bus.WriteMemory(addr, r);
            Cycles += 14;
            return;
        }
        if (op == 0x35) // DEC (idx+d)
        {
            sbyte d = (sbyte)FetchByte();
            ushort addr = (ushort)(idx + d);
            byte v = Bus.ReadMemory(addr);
            byte r = Dec8(v);
            Bus.WriteMemory(addr, r);
            Cycles += 14;
            return;
        }
        if (op == 0x36) // LD (idx+d), n
        {
            sbyte d = (sbyte)FetchByte();
            byte n = FetchByte();
            Bus.WriteMemory((ushort)(idx + d), n);
            Cycles += 11;
            return;
        }
        // Indexed LD r,(idx+d) and LD (idx+d),r
        if (x == 1 && (y == 6 || z == 6) && !(y == 6 && z == 6))
        {
            sbyte d = (sbyte)FetchByte();
            ushort addr = (ushort)(idx + d);
            if (y == 6)
            {
                Bus.WriteMemory(addr, GetReg(z));
            }
            else
            {
                SetReg(y, Bus.ReadMemory(addr));
            }
            Cycles += 11;
            return;
        }
        // Indexed ALU
        if (x == 2 && z == 6)
        {
            sbyte d = (sbyte)FetchByte();
            ushort addr = (ushort)(idx + d);
            byte v = Bus.ReadMemory(addr);
            Alu(y, v);
            Cycles += 11;
            return;
        }
        // Undocumented IXH/IXL/IYH/IYL access (x==1, y or z in {4,5})
        if (x == 1 || x == 2)
        {
            byte v;
            int extra = 0;
            if (z == 4) v = (byte)((idx >> 8) & 0xFF);
            else if (z == 5) v = (byte)(idx & 0xFF);
            else if (z == 6) { sbyte d = (sbyte)FetchByte(); v = Bus.ReadMemory((ushort)(idx + d)); extra = 11; }
            else v = GetReg(z);

            if (x == 2)
            {
                Alu(y, v);
                Cycles += extra;
                return;
            }

            // LD with index half-register source/dest
            if (y == 4) idx = (ushort)((idx & 0x00FF) | (v << 8));
            else if (y == 5) idx = (ushort)((idx & 0xFF00) | v);
            else SetReg(y, v);
            return;
        }
        // INC IXH/IXL/IYH/IYL / DEC ditto / LD reg, n with index
        if (op == 0x24) { byte v = (byte)((idx >> 8) & 0xFF); v = Inc8(v); idx = (ushort)((idx & 0x00FF) | (v << 8)); return; }
        if (op == 0x25) { byte v = (byte)((idx >> 8) & 0xFF); v = Dec8(v); idx = (ushort)((idx & 0x00FF) | (v << 8)); return; }
        if (op == 0x2C) { byte v = (byte)(idx & 0xFF); v = Inc8(v); idx = (ushort)((idx & 0xFF00) | v); return; }
        if (op == 0x2D) { byte v = (byte)(idx & 0xFF); v = Dec8(v); idx = (ushort)((idx & 0xFF00) | v); return; }
        if (op == 0x26) { byte n = FetchByte(); idx = (ushort)((idx & 0x00FF) | (n << 8)); Cycles += 3; return; }
        if (op == 0x2E) { byte n = FetchByte(); idx = (ushort)((idx & 0xFF00) | n); Cycles += 3; return; }

        // Fall back: a DD-prefixed instruction that doesn't use HL is just
        // the unprefixed instruction. Back up PC and dispatch normally.
        PC = (ushort)(PC - 1);
        ExecuteMain(op);
    }
}
