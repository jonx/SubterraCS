namespace Subterra.Spectrum;

/// <summary>
/// Reconstructs the cassette's EIGHT NEVER-PLAYED sounds — the `$F8xx`
/// message family that the game queues into `$FF51` but no code ever
/// plays (see docs/disasm/sound.md §vestigial and docs/CURIOSITIES.md).
///
/// The message bytes are NOT in the title player's (duration, pitch)
/// word-pair format: interpreted that way they'd yield multi-second
/// notes.  Their structure is variable-length groups of pitch bytes
/// terminated by `$03` (clearest in the game-over data:
/// `1B 58 03 | 58 58 03 | 18 18 03 | ...`), with values 10..125 —
/// exactly the title player's PITCH range.  Our reconstruction
/// therefore:
///
///   - treats each non-separator byte as one short note whose pitch
///     uses the SAME busy-wait semantics as `$FA32` (bigger = lower);
///   - treats `$03` as a group separator (a brief rest);
///   - stops at `$00` (the fanfare messages end `00 00`);
///   - synthesises each note with `$FA32`'s exact pulse-cycle engine:
///     speaker low for D counts, high for E counts (DJNZ semantics —
///     0 means 256), a pitch-gap countdown between toggles, and the
///     Follin duty slide (`INC E / DEC D`, bouncing at the ends).
///
/// The note DURATION is the one free parameter (the lost format never
/// shipped a consumer, so no ground truth exists); we use 56 pulse
/// cycles per note + a 24 ms rest per `$03`, which paces the longest
/// message (game-over, 32 bytes) like a short fanfare.  Every
/// assumption above is documented in CURIOSITIES.md — these renders
/// are archaeology, not gospel.
/// </summary>
public static class LostSoundReconstructor
{
    private const double CpuHz = Spectrum48.CpuFrequencyHz;

    // T-state costs per $FA32 loop section (Z80 manual values):
    // DJNZ taken = 13T, not taken 8T; OUT (n),A = 11T; LD A,n = 7T.
    // Pitch gap: DEC BC(6) + LD A,B(4) + OR C(4) + JR NZ taken(12) = 26T.
    private const double TPerDelayCount = 13.0;
    private const double TPerPitchCount = 26.0;
    private const double TCycleOverhead = 60.0;   // OUTs, loads, branches

    public readonly record struct Message(string Name, ushort Address, byte[] Bytes);

    /// <summary>The eight messages, byte-for-byte from the snapshot
    /// (addresses in docs/disasm/sound.md).  Kept inline so the
    /// reconstruction is reproducible without a RAM image.</summary>
    public static readonly Message[] Messages =
    {
        new("lost-bossalert", 0xF904, new byte[] { 0x77,0x3A,0x37,0x33,0x03,0x18,0x2D,0x33,0x0D,0x03,0xCD }),
        new("lost-pickup",    0xF919, new byte[] { 0x67,0x13,0x28,0x31,0x1F,0x2D,0x0C,0x2C,0x03 }),
        new("lost-warning",   0xF945, new byte[] { 0x17,0x17,0x3E,0x03,0x14,0x2D,0x13,0x07,0x0B,0x37,0x03,0x21,0x13 }),
        new("lost-fuellow",   0xF8C5, new byte[] { 0x6D,0x47,0x23,0x2D,0x03,0x17,0x23,0x03,0x28,0x31,0x1F,0x2D,0x03,0x0C,0x2B,0x03,0x6D,0x35,0x03 }),
        new("lost-shieldlow", 0xF8E9, new byte[] { 0x25,0x13,0x2D,0x15,0x03,0x47,0x78,0x33,0x0A,0x13,0x0C,0x2B,0x03,0x6D,0x35,0x03 }),
        new("lost-shipkill",  0xF96A, new byte[] { 0x7D,0x4E,0x54,0x4D,0x03,0x65,0x57,0x4D,0x03 }),
        new("lost-gameover",  0xF97F, new byte[] { 0x1B,0x58,0x03,0x58,0x58,0x03,0x18,0x18,0x03,0x18,0x18,0x03,0x71,0x5F,0x03,0x2D,
                                                    0x17,0x37,0x11,0x03,0x18,0x62,0x54,0x0B,0x03,0x1B,0x71,0x5F,0x10,0x18,0x0B,0x03 }),
        new("lost-fanfare1",  0xF9C2, new byte[] { 0x68,0x73,0x77,0x51,0x03,0x6D,0x07,0x23,0x2D,0x77,0x47 }),
        new("lost-fanfare2",  0xF9CD, new byte[] { 0x6A,0x17,0x0B,0x21,0x03,0x6D,0x07,0x23,0x2D,0x00,0x00 }),
        new("lost-fanfare3",  0xF9D8, new byte[] { 0x00,0x5D,0x34,0x21,0x03,0x6D,0x07,0x23,0x2D,0x00,0x00 }),
        new("lost-fanfare4",  0xF9E3, new byte[] { 0x00,0x68,0x3A,0x1D,0x03,0x6D,0x07,0x23,0x2D,0x00,0x00 }),
        new("lost-fanfare5",  0xF9EE, new byte[] { 0x68,0x0C,0x28,0x1D,0x03,0x6D,0x07,0x23,0x2D,0x00,0x00 }),
    };

    private const int CyclesPerNote = 56;
    private const double RestSeconds = 0.024;

    /// <summary>Render one message to mono 16-bit PCM.</summary>
    public static short[] Render(byte[] message, int sampleRate, int amplitude = 5000)
    {
        var pcm = new List<short>(sampleRate);   // grows as needed

        // Trailing $00s are end padding (the fanfares end `00 00`),
        // but a LEADING/interior $00 (fanfares 3 and 4 START with
        // one) must be a rest — treat it like a long separator.
        int end = message.Length;
        while (end > 0 && message[end - 1] == 0x00) end--;

        for (int i = 0; i < end; i++)
        {
            byte b = message[i];
            if (b == 0x00)                        // interior rest
            {
                AppendSilence(pcm, sampleRate, RestSeconds * 2);
                continue;
            }
            if (b == 0x03)                        // group separator → rest
            {
                AppendSilence(pcm, sampleRate, RestSeconds);
                continue;
            }
            RenderNote(pcm, b, sampleRate, amplitude);
        }
        // Short tail so the last edge doesn't clip.
        AppendSilence(pcm, sampleRate, 0.01);
        return pcm.ToArray();
    }

    /// <summary>One note: $FA32's pulse-cycle engine with pitch =
    /// <paramref name="pitch"/> (busy-wait count) for
    /// <see cref="CyclesPerNote"/> cycles, duty sliding per cycle.</summary>
    private static void RenderNote(List<short> pcm, byte pitch, int sampleRate, int amplitude)
    {
        int d = 0x00, e = 0xFF;                   // $FA47 LD DE,$00FF
        bool slideUp = true;                      // INC E / DEC D first
        double sampPerT = sampleRate / CpuHz;

        double acc = 0;                            // fractional samples
        for (int cycle = 0; cycle < CyclesPerNote; cycle++)
        {
            // DJNZ semantics: a count of 0 means 256 iterations.
            int dEff = d == 0 ? 256 : d;
            int eEff = e == 0 ? 256 : e;

            AppendLevel(pcm, ref acc, (dEff * TPerDelayCount) * sampPerT, (short)-amplitude);
            AppendLevel(pcm, ref acc, (eEff * TPerDelayCount + pitch * TPerPitchCount + TCycleOverhead) * sampPerT, (short)amplitude);

            // The Follin duty slide, bouncing at the byte ends.
            if (slideUp) { e++; d--; if ((d & 0xFF) == 0) slideUp = false; }
            else         { e--; d++; if ((e & 0xFF) == 0) slideUp = true; }
            d &= 0xFF; e &= 0xFF;
        }
    }

    private static void AppendLevel(List<short> pcm, ref double acc, double samples, short level)
    {
        acc += samples;
        int n = (int)acc;
        acc -= n;
        for (int i = 0; i < n; i++) pcm.Add(level);
    }

    private static void AppendSilence(List<short> pcm, int sampleRate, double seconds)
    {
        int n = (int)(sampleRate * seconds);
        for (int i = 0; i < n; i++) pcm.Add(0);
    }
}
