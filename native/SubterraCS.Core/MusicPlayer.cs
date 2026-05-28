namespace SubterraCS.Core;

/// <summary>
/// Drives the Follin music data (16-bit little-endian period pairs at
/// <c>$5E88</c>) through the <see cref="BeeperSynth"/>.  We don't
/// re-implement the exact pulse-width sweep at <c>$FA32</c>; instead
/// we map each period to a frequency and play it as a sustained note
/// with a gentle slide so the timbre approximates the original.
///
/// One period word = one note.  The period is the half-cycle delay
/// constant on the Z80 (DJNZ count), so the produced frequency is
/// roughly <c>3.5e6 / (period × 26)</c> on a real Spectrum — we factor
/// the constants out into <see cref="ScaleHz"/> for tuning.
/// </summary>
public sealed class MusicPlayer
{
    private readonly byte[] _data;
    private int _cursor;
    private int _framesPerNote;
    private int _frameTick;
    private bool _enabled = true;

    public MusicPlayer(byte[] data, int framesPerNote = 6)
    {
        _data = data ?? Array.Empty<byte>();
        _framesPerNote = Math.Max(1, framesPerNote);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public void Reset() { _cursor = 0; _frameTick = 0; }

    /// <summary>
    /// Call once per 50 Hz frame.  Picks up the next note when the
    /// per-note timer expires and pushes it into the synth.
    /// </summary>
    public void Tick(BeeperSynth? synth)
    {
        if (!_enabled || synth is null || _data.Length < 4) return;
        _frameTick++;
        if (_frameTick < _framesPerNote) return;
        _frameTick = 0;

        if (_cursor + 1 >= _data.Length) _cursor = 0;
        ushort period = (ushort)(_data[_cursor] | (_data[_cursor + 1] << 8));
        _cursor += 2;

        if (period == 0)
        {
            // Rest / sentinel.
            synth.PlayNote(0, _framesPerNote / 50.0);
            return;
        }
        double hz = ScaleHz(period);
        // Length matches one beat (slightly longer so notes blend).
        double lengthSec = (_framesPerNote + 1) / 50.0;
        synth.PlayNote(hz, lengthSec, slide: 12.0);
    }

    private static double ScaleHz(ushort period)
    {
        // Map the raw period to a musically pleasant range (~80 Hz–1.2 kHz).
        // The original used a tight Z80 delay loop; we just normalise.
        // Smaller period → higher pitch.
        double clamped = Math.Clamp((double)period, 30.0, 4000.0);
        return 64000.0 / clamped;
    }
}
