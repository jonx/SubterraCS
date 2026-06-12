namespace Subterra.Spectrum;

/// <summary>
/// Hand-designed beeper SFX for the events the original cassette left
/// silent (the $F8xx message system was vestigial — see
/// docs/disasm/sound.md and docs/CURIOSITIES.md §2).
///
/// Unlike the archaeological <see cref="LostSoundReconstructor"/>
/// reconstructions, these sounds are purpose-built to be distinct and
/// recognisable in game, while staying within the Spectrum square-wave
/// timbre.  Each sequence is a list of (hz, seconds, slide, pauseBefore)
/// notes rendered by the same duty-slide algorithm as
/// <see cref="BeeperSynth"/>:
///   phase += hz * dt each sample;
///   duty += slide * dt, bouncing at [0.1, 0.9];
///   output is +amplitude when phase &lt; duty else -amplitude.
///
/// Use <c>subterra sfx-render</c> to regenerate the <c>sfx-*.wav</c>
/// files in <c>assets/extracted/sfx/</c>.  The <c>lost-*.wav</c>
/// archaeological reconstructions are kept alongside but are selected
/// only when the native port's N-key is cycled to HISTORICAL mode.
/// </summary>
public static class DesignedSfxSet
{
    /// <param name="Hz">Fundamental frequency in Hertz.</param>
    /// <param name="Seconds">Duration in seconds.</param>
    /// <param name="Slide">Duty-cycle slide rate (same units as
    ///   <see cref="BeeperSynth.Tone"/>; ~50 = subtle warmth,
    ///   ~200 = rapid Follin wobble).</param>
    /// <param name="PauseBefore">Silent gap inserted before this note.</param>
    public readonly record struct Note(
        double Hz, double Seconds, double Slide = 0.0, double PauseBefore = 0.0);

    public readonly record struct Sequence(string Name, Note[] Notes);

    /// <summary>One sequence per in-game event, named to match the
    /// <c>sfx-*.wav</c> files the runner looks up by string key.</summary>
    public static readonly Sequence[] Sequences =
    {
        // Boss spawn — rising siren: low attack → mid → high sustained,
        // with fast duty-slide on all notes for the classic beeper alarm
        // timbre.  Ascending trajectory reads as "danger incoming",
        // not defeat.
        new("sfx-bossalert", new Note[]
        {
            new(330, 0.06, Slide: +200),
            new(550, 0.07, Slide: +150, PauseBefore: 0.010),
            new(880, 0.14, Slide: +200),
        }),

        // Fuel-station pickup / rescue reward — bright ascending arpeggio.
        new("sfx-pickup", new Note[]
        {
            new(440, 0.04),
            new(550, 0.04),
            new(660, 0.04),
            new(880, 0.08, Slide: +80),
        }),

        // Low fuel warning — two short pulses at a low pitch.
        new("sfx-fuellow", new Note[]
        {
            new(330, 0.07),
            new(330, 0.07, PauseBefore: 0.040),
        }),

        // Low shield warning — same rhythm, different pitch so it's
        // distinguishable from the fuel warning.
        new("sfx-shieldlow", new Note[]
        {
            new(480, 0.06, Slide: +40),
            new(480, 0.06, Slide: +40, PauseBefore: 0.030),
        }),

        // Ship / enemy kill — quick attack then descending crash.
        new("sfx-shipkill", new Note[]
        {
            new(660, 0.04),
            new(330, 0.06, Slide: -200),
            new(165, 0.10, Slide: -100),
            new(110, 0.14, Slide:  -40),
        }),

        // Game over — slow descending scale, deliberately long and final.
        new("sfx-gameover", new Note[]
        {
            new(440, 0.14, Slide: -20, PauseBefore: 0.020),
            new(330, 0.14, Slide: -20, PauseBefore: 0.060),
            new(220, 0.18, Slide: -15, PauseBefore: 0.060),
            new(165, 0.24, Slide: -10, PauseBefore: 0.080),
        }),

        // Per-level fanfares — ascending, each level a note longer.
        new("sfx-fanfare1", new Note[]
        {
            new(440, 0.06),
            new(660, 0.10, Slide: +80),
        }),
        new("sfx-fanfare2", new Note[]
        {
            new(440, 0.05),
            new(550, 0.05),
            new(660, 0.10, Slide: +100),
        }),
        new("sfx-fanfare3", new Note[]
        {
            new(440, 0.05),
            new(550, 0.05),
            new(660, 0.05),
            new(880, 0.10, Slide: +120),
        }),
        new("sfx-fanfare4", new Note[]
        {
            new(330, 0.04),
            new(440, 0.04),
            new(550, 0.05),
            new(660, 0.05),
            new(880, 0.10, Slide: +150),
        }),
        new("sfx-fanfare5", new Note[]
        {
            new(330, 0.04),
            new(440, 0.04),
            new(550, 0.04),
            new(660, 0.04),
            new(880, 0.04),
            new(1100, 0.12, Slide: +200),
        }),
    };

    /// <summary>Render a sequence to mono 16-bit PCM at
    /// <paramref name="sampleRate"/> Hz.  Uses the same square-wave +
    /// duty-slide algorithm as <see cref="BeeperSynth"/>.</summary>
    public static short[] Render(Sequence seq, int sampleRate)
    {
        var pcm = new List<short>(sampleRate / 2);
        foreach (var note in seq.Notes)
        {
            if (note.PauseBefore > 0)
                AppendSilence(pcm, sampleRate, note.PauseBefore);
            RenderNote(pcm, note.Hz, note.Seconds, note.Slide, sampleRate);
        }
        AppendSilence(pcm, sampleRate, 0.010);   // short tail to avoid clipping
        return pcm.ToArray();
    }

    private static void RenderNote(List<short> pcm,
        double hz, double seconds, double slide, int sampleRate,
        short amplitude = 5000)
    {
        if (hz <= 0) { AppendSilence(pcm, sampleRate, seconds); return; }
        double dt = 1.0 / sampleRate;
        double phase = 0.0, duty = 0.5;
        int n = (int)(seconds * sampleRate);
        for (int i = 0; i < n; i++)
        {
            pcm.Add(phase < duty ? amplitude : (short)-amplitude);
            phase += hz * dt;
            while (phase >= 1.0) phase -= 1.0;
            duty += slide * dt;
            if      (duty < 0.1) { duty = 0.1; slide = -slide; }
            else if (duty > 0.9) { duty = 0.9; slide = -slide; }
        }
    }

    private static void AppendSilence(List<short> pcm, int sampleRate, double seconds)
    {
        int n = (int)(sampleRate * seconds);
        for (int i = 0; i < n; i++) pcm.Add(0);
    }
}
