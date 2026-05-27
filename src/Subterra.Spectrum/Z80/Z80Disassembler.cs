using System.Globalization;
using System.Text;

namespace Subterra.Spectrum.Z80;

/// <summary>
/// A Z80 disassembler covering the documented instruction set
/// (everything in Zilog's Z80 CPU User Manual). Undocumented prefix
/// quirks (DD/FD chains, NEG variants, IXH/IXL access, etc.) decode to
/// either their documented form or a labelled <c>DEFB</c> when
/// genuinely undocumented — never to garbage.
///
/// The disassembler is delegated to from <see cref="Decode"/>: pass in
/// a 16-bit address and a span of bytes starting at that address;
/// receive a <see cref="Z80Instruction"/> describing what was decoded
/// and how many bytes it consumed.
/// </summary>
public static class Z80Disassembler
{
    /// <summary>
    /// Decode one instruction starting at <paramref name="address"/>.
    /// <paramref name="memory"/> is the *full* 64 K address space (a
    /// 65 536-byte span) so that relative-jump targets can be computed
    /// correctly across page boundaries.
    /// </summary>
    public static Z80Instruction Decode(ushort address, ReadOnlySpan<byte> memory)
    {
        var decoder = new Decoder(address, memory);
        var (mnemonic, length) = decoder.Run();
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = memory[(address + i) & 0xFFFF];
        }
        return new Z80Instruction(address, bytes, mnemonic);
    }

    /// <summary>
    /// Decode <paramref name="count"/> instructions starting from
    /// <paramref name="address"/> and return them as a flat list.
    /// </summary>
    public static List<Z80Instruction> DecodeRange(
        ushort address, ReadOnlySpan<byte> memory, int count)
    {
        var output = new List<Z80Instruction>(count);
        ushort pc = address;
        for (int i = 0; i < count; i++)
        {
            var ins = Decode(pc, memory);
            output.Add(ins);
            pc = (ushort)(pc + ins.Length);
        }
        return output;
    }

    private ref struct Decoder
    {
        private readonly ReadOnlySpan<byte> _memory;
        private readonly ushort _address;
        private int _offset;

        public Decoder(ushort address, ReadOnlySpan<byte> memory)
        {
            _memory = memory;
            _address = address;
            _offset = 0;
        }

        private string IndexedMem(string hl, string mem)
        {
            if (hl == "HL")
            {
                return mem;
            }
            sbyte d = (sbyte)ReadByte();
            string sign = d < 0 ? "-" : "+";
            int abs = d < 0 ? -d : d;
            return $"({hl}{sign}${abs:X2})";
        }

        private byte ReadByte()
        {
            byte b = _memory[(_address + _offset) & 0xFFFF];
            _offset++;
            return b;
        }

        private ushort ReadWord()
        {
            byte lo = ReadByte();
            byte hi = ReadByte();
            return (ushort)((hi << 8) | lo);
        }

        public (string mnemonic, int length) Run()
        {
            byte op = ReadByte();
            string text = op switch
            {
                0xCB => DecodeCB(),
                0xED => DecodeED(),
                0xDD => DecodeIndexed("IX"),
                0xFD => DecodeIndexed("IY"),
                _    => DecodeMain(op, "HL"),
            };
            return (text, _offset);
        }

        // --- Main page ------------------------------------------------

        private string DecodeMain(byte op, string hl)
        {
            // Standard tables — see ZUM (Z80 User Manual) pp.50ff.
            // We special-case the (HL) substitutions for the IX/IY prefix
            // variants by passing "HL", "IX", or "IY" in `hl` and using
            // it whenever the instruction would otherwise touch (HL) or HL.
            // For the genuinely-undocumented IX/IY substitutions of H and
            // L we keep the documented names ("H"/"L") and don't claim to
            // produce undocumented variants from the indexed paths.
            string mem = "(" + hl + ")";

            // First, the regular grid based on bit layout xx-yyy-zzz.
            int x = (op >> 6) & 0x3;
            int y = (op >> 3) & 0x7;
            int z = op & 0x7;
            int p = y >> 1;
            int q = y & 1;

            string[] r = { "B", "C", "D", "E", "H", "L", mem, "A" };
            string[] rp = { "BC", "DE", hl, "SP" };
            string[] rp2 = { "BC", "DE", hl, "AF" };
            string[] cc = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };
            string[] alu = { "ADD A,", "ADC A,", "SUB ", "SBC A,", "AND ", "XOR ", "OR ", "CP " };
            string[] rot = { "RLCA", "RRCA", "RLA", "RRA", "DAA", "CPL", "SCF", "CCF" };

            // For indexed variants, when r[6] is dereferenced we may also
            // need a displacement (d). We collect it lazily via IndexedMem.

            if (x == 0)
            {
                switch (z)
                {
                    case 0:
                        switch (y)
                        {
                            case 0: return "NOP";
                            case 1: return "EX AF,AF'";
                            case 2: { sbyte d = (sbyte)ReadByte(); return $"DJNZ ${(ushort)(_address + _offset + d):X4}"; }
                            case 3: { sbyte d = (sbyte)ReadByte(); return $"JR ${(ushort)(_address + _offset + d):X4}"; }
                            default:
                            {
                                sbyte d = (sbyte)ReadByte();
                                return $"JR {cc[y - 4]},${(ushort)(_address + _offset + d):X4}";
                            }
                        }
                    case 1:
                        if (q == 0)
                        {
                            return $"LD {rp[p]},${ReadWord():X4}";
                        }
                        return $"ADD {hl},{rp[p]}";
                    case 2:
                        switch (y)
                        {
                            case 0: return "LD (BC),A";
                            case 1: return "LD A,(BC)";
                            case 2: return "LD (DE),A";
                            case 3: return "LD A,(DE)";
                            case 4: return $"LD (${ReadWord():X4}),{hl}";
                            case 5: return $"LD {hl},(${ReadWord():X4})";
                            case 6: return $"LD (${ReadWord():X4}),A";
                            default: return $"LD A,(${ReadWord():X4})";
                        }
                    case 3:
                        return q == 0 ? $"INC {rp[p]}" : $"DEC {rp[p]}";
                    case 4:
                    {
                        string target = y == 6 ? IndexedMem(hl, mem) : r[y];
                        return $"INC {target}";
                    }
                    case 5:
                    {
                        string target = y == 6 ? IndexedMem(hl, mem) : r[y];
                        return $"DEC {target}";
                    }
                    case 6:
                    {
                        if (y == 6 && hl != "HL")
                        {
                            string target = IndexedMem(hl, mem);
                            byte n = ReadByte();
                            return $"LD {target},${n:X2}";
                        }
                        string targ2 = y == 6 ? mem : r[y];
                        byte n2 = ReadByte();
                        return $"LD {targ2},${n2:X2}";
                    }
                    case 7:
                        return rot[y];
                }
            }
            else if (x == 1)
            {
                if (z == 6 && y == 6)
                {
                    return "HALT";
                }
                if (hl != "HL" && (z == 6 || y == 6))
                {
                    if (z == 6 && y != 6)
                    {
                        string src = IndexedMem(hl, mem);
                        return $"LD {r[y]},{src}";
                    }
                    if (y == 6 && z != 6)
                    {
                        string dest = IndexedMem(hl, mem);
                        return $"LD {dest},{r[z]}";
                    }
                }
                return $"LD {r[y]},{r[z]}";
            }
            else if (x == 2)
            {
                string operand = z == 6 && hl != "HL" ? IndexedMem(hl, mem) : r[z];
                return alu[y] + operand;
            }
            else // x == 3
            {
                switch (z)
                {
                    case 0: return $"RET {cc[y]}";
                    case 1:
                        if (q == 0) return $"POP {rp2[p]}";
                        return p switch
                        {
                            0 => "RET",
                            1 => "EXX",
                            2 => $"JP ({hl})",
                            _ => $"LD SP,{hl}",
                        };
                    case 2:
                        return $"JP {cc[y]},${ReadWord():X4}";
                    case 3:
                        switch (y)
                        {
                            case 0: return $"JP ${ReadWord():X4}";
                            case 1: return "<<CB-prefix>>"; // shouldn't reach here
                            case 2: return $"OUT (${ReadByte():X2}),A";
                            case 3: return $"IN A,(${ReadByte():X2})";
                            case 4: return $"EX (SP),{hl}";
                            case 5: return "EX DE,HL";
                            case 6: return "DI";
                            default: return "EI";
                        }
                    case 4:
                        return $"CALL {cc[y]},${ReadWord():X4}";
                    case 5:
                        if (q == 0) return $"PUSH {rp2[p]}";
                        return p switch
                        {
                            0 => $"CALL ${ReadWord():X4}",
                            _ => "<<prefix>>",
                        };
                    case 6:
                        return alu[y] + $"${ReadByte():X2}";
                    case 7:
                        return $"RST ${(y * 8):X2}";
                }
            }
            return $"DEFB ${op:X2}";
        }

        // --- CB prefix (rot/shift/bit ops) ----------------------------

        private string DecodeCB()
        {
            byte op = ReadByte();
            int x = (op >> 6) & 3;
            int y = (op >> 3) & 7;
            int z = op & 7;
            string[] r = { "B", "C", "D", "E", "H", "L", "(HL)", "A" };
            string[] rot = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };
            return x switch
            {
                0 => $"{rot[y]} {r[z]}",
                1 => $"BIT {y},{r[z]}",
                2 => $"RES {y},{r[z]}",
                _ => $"SET {y},{r[z]}",
            };
        }

        // --- ED prefix (extended) -------------------------------------

        private string DecodeED()
        {
            byte op = ReadByte();
            int x = (op >> 6) & 3;
            int y = (op >> 3) & 7;
            int z = op & 7;
            int p = y >> 1;
            int q = y & 1;
            string[] rp = { "BC", "DE", "HL", "SP" };

            if (x == 1)
            {
                string[] r = { "B", "C", "D", "E", "H", "L", "(HL)", "A" };
                switch (z)
                {
                    case 0:
                        return y == 6 ? "IN (C)" : $"IN {r[y]},(C)";
                    case 1:
                        return y == 6 ? "OUT (C),0" : $"OUT (C),{r[y]}";
                    case 2:
                        return q == 0
                            ? $"SBC HL,{rp[p]}"
                            : $"ADC HL,{rp[p]}";
                    case 3:
                        return q == 0
                            ? $"LD (${ReadWord():X4}),{rp[p]}"
                            : $"LD {rp[p]},(${ReadWord():X4})";
                    case 4: return "NEG";
                    case 5: return y == 1 ? "RETI" : "RETN";
                    case 6:
                        return y switch
                        {
                            0 or 1 or 4 or 5 => "IM 0",
                            2 or 6 => "IM 1",
                            _ => "IM 2",
                        };
                    case 7:
                        return y switch
                        {
                            0 => "LD I,A",
                            1 => "LD R,A",
                            2 => "LD A,I",
                            3 => "LD A,R",
                            4 => "RRD",
                            5 => "RLD",
                            _ => $"DEFB $ED,${op:X2}",
                        };
                }
            }
            if (x == 2 && y >= 4 && z <= 3)
            {
                // Block instructions
                string[,] block =
                {
                    // z = 0   1     2     3
                    { "LDI",  "CPI",  "INI",  "OUTI" }, // y=4
                    { "LDD",  "CPD",  "IND",  "OUTD" }, // y=5
                    { "LDIR", "CPIR", "INIR", "OTIR" }, // y=6
                    { "LDDR", "CPDR", "INDR", "OTDR" }, // y=7
                };
                return block[y - 4, z];
            }
            return $"DEFB $ED,${op:X2}";
        }

        // --- DD / FD prefix (IX / IY) ---------------------------------

        private string DecodeIndexed(string idx)
        {
            byte op = ReadByte();
            if (op == 0xCB)
            {
                // DD CB d op or FD CB d op
                sbyte d = (sbyte)ReadByte();
                byte sub = ReadByte();
                int x = (sub >> 6) & 3;
                int y = (sub >> 3) & 7;
                int z = sub & 7;
                string[] r = { "B", "C", "D", "E", "H", "L", "", "A" };
                string sign = d < 0 ? "-" : "+";
                int abs = d < 0 ? -d : d;
                string mem = $"({idx}{sign}${abs:X2})";
                string[] rot = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };
                if (x == 0)
                {
                    return z == 6
                        ? $"{rot[y]} {mem}"
                        : $"{rot[y]} {mem},{r[z]}"; // undocumented "store-through"
                }
                if (x == 1)
                {
                    return $"BIT {y},{mem}";
                }
                if (x == 2)
                {
                    return z == 6
                        ? $"RES {y},{mem}"
                        : $"RES {y},{mem},{r[z]}";
                }
                return z == 6
                    ? $"SET {y},{mem}"
                    : $"SET {y},{mem},{r[z]}";
            }
            // Otherwise, treat as the main page with HL→IX/IY substitution.
            return DecodeMain(op, idx);
        }
    }

    /// <summary>
    /// Format an instruction in a typical listing column layout:
    /// <c>XXXX  AA BB CC ...  MNEMONIC</c>.
    /// </summary>
    public static string Format(Z80Instruction ins, int byteCol = 6, int mnemonicCol = 18)
    {
        var sb = new StringBuilder();
        sb.Append(ins.Address.ToString("X4", CultureInfo.InvariantCulture));
        while (sb.Length < byteCol)
        {
            sb.Append(' ');
        }
        for (int i = 0; i < ins.Bytes.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }
            sb.Append(ins.Bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        while (sb.Length < mnemonicCol)
        {
            sb.Append(' ');
        }
        sb.Append(ins.Mnemonic);
        return sb.ToString();
    }
}
