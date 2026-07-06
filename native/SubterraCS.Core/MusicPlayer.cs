namespace SubterraCS.Core;

/// <summary>
/// MODERN ONLY — the cassette has NO in-game music (its Follin player
/// $FA32 ticks only inside the title loop; sound.md).  When the modern
/// mode enables it, this plays the real $5E88 stream: pairs of 16-bit
/// little-endian words, (duration, pitch) per note — the format
/// documented in sound.md §The Follin player.  Duration counts player
/// ticks; pitch is the half-cycle delay constant of the busy-wait
/// pulse loop, so frequency ≈ 3.5 MHz / (pitch × 26).
/// </summary>
public sealed class MusicPlayer
{
    private readonly byte[] _data;
    private int _cursor;
    private int _durationLeft;
    private double _currentHz;

    public MusicPlayer(byte[] data)
    {
        _data = data ?? Array.Empty<byte>();
    }

    public void Reset() { _cursor = 0; _durationLeft = 0; }

    /// <summary>Call once per 50 Hz frame.</summary>
    public void Tick(BeeperSynth? synth)
    {
        if (synth is null || _data.Length < 4) return;
        if (_durationLeft > 0)
        {
            _durationLeft--;
            return;
        }
        // Stream terminator: first byte $FF → restart the tune.
        if (_cursor + 3 >= _data.Length || _data[_cursor] == 0xFF) _cursor = 0;
        ushort duration = (ushort)(_data[_cursor] | (_data[_cursor + 1] << 8));
        ushort pitch    = (ushort)(_data[_cursor + 2] | (_data[_cursor + 3] << 8));
        _cursor += 4;

        if (pitch == 0)
        {
            _currentHz = 0;      // rest
            _durationLeft = Math.Clamp(duration / 8, 1, 60);
            return;
        }
        // pitch = half-cycle delay between speaker toggles
        // (freq ≈ 3.5 MHz / (pitch × 26)); duration = pulse-cycle
        // count, so note length in frames = duration / freq × 50.
        _currentHz = Math.Clamp(3_500_000.0 / (pitch * 26.0), 60.0, 3000.0);
        _durationLeft = Math.Clamp((int)Math.Round(duration * 50.0 / _currentHz), 1, 100);
        synth.PlayNote(_currentHz, _durationLeft / 50.0);
    }
}
