namespace SubterraCS.Core;

/// <summary>
/// The Spectrum 16-colour palette (8 base colours × 2 bright levels).
/// Black is the same in both halves.
/// </summary>
public static class SpectrumPalette
{
    public static readonly (byte R, byte G, byte B)[] Colours =
    {
        (0x00, 0x00, 0x00), // 0  black
        (0x00, 0x00, 0xCD), // 1  blue
        (0xCD, 0x00, 0x00), // 2  red
        (0xCD, 0x00, 0xCD), // 3  magenta
        (0x00, 0xCD, 0x00), // 4  green
        (0x00, 0xCD, 0xCD), // 5  cyan
        (0xCD, 0xCD, 0x00), // 6  yellow
        (0xCD, 0xCD, 0xCD), // 7  white
        (0x00, 0x00, 0x00), // 8  black (bright)
        (0x00, 0x00, 0xFF), // 9  blue
        (0xFF, 0x00, 0x00), // 10 red
        (0xFF, 0x00, 0xFF), // 11 magenta
        (0x00, 0xFF, 0x00), // 12 green
        (0x00, 0xFF, 0xFF), // 13 cyan
        (0xFF, 0xFF, 0x00), // 14 yellow
        (0xFF, 0xFF, 0xFF), // 15 white
    };

    /// <summary>Resolve a Spectrum-style attribute byte into RGB tuples.</summary>
    public static (byte R, byte G, byte B) Ink(byte attr)
    {
        int ink = attr & 0x07;
        bool bright = (attr & 0x40) != 0;
        return Colours[(bright ? 8 : 0) + ink];
    }

    public static (byte R, byte G, byte B) Paper(byte attr)
    {
        int paper = (attr >> 3) & 0x07;
        bool bright = (attr & 0x40) != 0;
        return Colours[(bright ? 8 : 0) + paper];
    }
}
