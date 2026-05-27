namespace Subterra.Spectrum;

/// <summary>
/// A single Spectrum key, identified by its keyboard half-row (0-7)
/// and the bit within that row (0 = leftmost..4 = rightmost).
/// </summary>
public readonly record struct SpectrumKey(int Row, int Bit)
{
    // Row 0 (port high-byte bit 8 low): CAPS Z X C V
    public static readonly SpectrumKey CapsShift = new(0, 0);
    public static readonly SpectrumKey Z = new(0, 1);
    public static readonly SpectrumKey X = new(0, 2);
    public static readonly SpectrumKey C = new(0, 3);
    public static readonly SpectrumKey V = new(0, 4);

    // Row 1: A S D F G
    public static readonly SpectrumKey A = new(1, 0);
    public static readonly SpectrumKey S = new(1, 1);
    public static readonly SpectrumKey D = new(1, 2);
    public static readonly SpectrumKey F = new(1, 3);
    public static readonly SpectrumKey G = new(1, 4);

    // Row 2: Q W E R T
    public static readonly SpectrumKey Q = new(2, 0);
    public static readonly SpectrumKey W = new(2, 1);
    public static readonly SpectrumKey E = new(2, 2);
    public static readonly SpectrumKey R = new(2, 3);
    public static readonly SpectrumKey T = new(2, 4);

    // Row 3: 1 2 3 4 5
    public static readonly SpectrumKey D1 = new(3, 0);
    public static readonly SpectrumKey D2 = new(3, 1);
    public static readonly SpectrumKey D3 = new(3, 2);
    public static readonly SpectrumKey D4 = new(3, 3);
    public static readonly SpectrumKey D5 = new(3, 4);

    // Row 4: 0 9 8 7 6
    public static readonly SpectrumKey D0 = new(4, 0);
    public static readonly SpectrumKey D9 = new(4, 1);
    public static readonly SpectrumKey D8 = new(4, 2);
    public static readonly SpectrumKey D7 = new(4, 3);
    public static readonly SpectrumKey D6 = new(4, 4);

    // Row 5: P O I U Y
    public static readonly SpectrumKey P = new(5, 0);
    public static readonly SpectrumKey O = new(5, 1);
    public static readonly SpectrumKey I = new(5, 2);
    public static readonly SpectrumKey U = new(5, 3);
    public static readonly SpectrumKey Y = new(5, 4);

    // Row 6: ENTER L K J H
    public static readonly SpectrumKey Enter = new(6, 0);
    public static readonly SpectrumKey L = new(6, 1);
    public static readonly SpectrumKey K = new(6, 2);
    public static readonly SpectrumKey J = new(6, 3);
    public static readonly SpectrumKey H = new(6, 4);

    // Row 7: SPACE SYM M N B
    public static readonly SpectrumKey Space = new(7, 0);
    public static readonly SpectrumKey SymbolShift = new(7, 1);
    public static readonly SpectrumKey M = new(7, 2);
    public static readonly SpectrumKey N = new(7, 3);
    public static readonly SpectrumKey B = new(7, 4);
}

public static class Spectrum48KeyExtensions
{
    /// <summary>Press <paramref name="key"/> (clear its bit in the half-row).</summary>
    public static void PressKey(this Spectrum48 sys, SpectrumKey key)
    {
        sys.KeyHalfRows[key.Row] &= (byte)~(1 << key.Bit);
    }

    /// <summary>Release <paramref name="key"/> (set its bit back to 1).</summary>
    public static void ReleaseKey(this Spectrum48 sys, SpectrumKey key)
    {
        sys.KeyHalfRows[key.Row] |= (byte)(1 << key.Bit);
    }

    /// <summary>Release every key.</summary>
    public static void ReleaseAllKeys(this Spectrum48 sys)
    {
        for (int i = 0; i < 8; i++)
        {
            sys.KeyHalfRows[i] = 0x1F;
        }
    }
}
