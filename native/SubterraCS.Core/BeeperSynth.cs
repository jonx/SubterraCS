namespace SubterraCS.Core;

/// <summary>
/// A tiny PCM beeper synth.  Renders one tone at a time (the Spectrum
/// only had one channel) plus the Follin-style "pulse-width slide"
/// timbre we documented at <c>$FA32</c>:
///
/// * the *pitch* of the output is a square wave at frequency F.
/// * the *timbre* slides because the high/low pulse widths drift while
///   the total period stays constant — equivalent to chorus / phasing on
///   a single channel.
///
/// Call <see cref="PlayNote"/> from the game thread to set the current
/// note; SDL's audio callback pulls samples via <see cref="Render"/>
/// from a different thread, hence the volatile/locked exchange.
/// </summary>
public sealed class BeeperSynth
{
    // Note state (read by the audio thread, written by the game thread).
    private double _frequency;       // Hz; 0 means silent
    private double _slide;           // ±[0..1] timbre slide rate
    private double _phase;           // 0..1 within current pulse cycle
    private double _duty;            // 0..1 duty cycle (50% = pure square)
    private double _ageSeconds;      // for envelope + auto-stop
    private double _lengthSeconds;   // how long this note plays
    private double _volume = 0.25;   // master volume (0..1)

    private readonly object _lock = new();

    public void SetVolume(double v) => _volume = Math.Clamp(v, 0.0, 1.0);

    /// <summary>
    /// Play a note for <paramref name="lengthSeconds"/>.  Setting
    /// <paramref name="hz"/> to 0 silences the synth.
    /// </summary>
    public void PlayNote(double hz, double lengthSeconds, double slide = 0.0)
    {
        lock (_lock)
        {
            _frequency = Math.Max(0.0, hz);
            _lengthSeconds = Math.Max(0.0, lengthSeconds);
            _slide = slide;
            _duty = 0.5;
            _ageSeconds = 0.0;
        }
    }

    /// <summary>Convenience for the typical 50 Hz frame-tick.</summary>
    public void Tone(double hz, int frames, double slide = 0.0)
        => PlayNote(hz, frames / 50.0, slide);

    // Authentic-PCM playback: when a captured cassette WAV (see
    // SfxWavBank) is queued it takes priority over the synth tone.
    // The samples are recorded at the audio device rate, so Render
    // copies them 1:1.
    private short[]? _pcm;
    private int _pcmPos;

    /// <summary>Play a captured cassette effect.  Pre-empts whatever
    /// the synth was doing; the synth resumes after the PCM ends.</summary>
    public void PlayPcm(short[] samples)
    {
        lock (_lock)
        {
            _pcm = samples;
            _pcmPos = 0;
        }
    }

    /// <summary>True while a captured PCM effect is sounding.</summary>
    public bool PcmActive { get { lock (_lock) return _pcm is not null; } }

    /// <summary>
    /// Pull <paramref name="output"/>.Length samples (signed 16-bit
    /// mono, native endian) at <paramref name="sampleRate"/> Hz.  Called
    /// from the audio thread.
    /// </summary>
    public void Render(Span<short> output, int sampleRate)
    {
        double freq, slide, len, age, duty, vol;
        lock (_lock)
        {
            // Captured-PCM path first: copy samples 1:1 (recorded at
            // the device rate), zero-fill the tail, drop the buffer
            // when exhausted so the synth resumes next callback.
            if (_pcm is { } pcm)
            {
                int n = Math.Min(output.Length, pcm.Length - _pcmPos);
                // Captured samples sit at ±5000; ×4 at the default
                // volume (0.25) keeps them level with the synth's amp.
                double gain = _volume * 4.0;
                for (int i = 0; i < n; i++) output[i] = (short)(pcm[_pcmPos + i] * gain);
                for (int i = n; i < output.Length; i++) output[i] = 0;
                _pcmPos += n;
                if (_pcmPos >= pcm.Length) { _pcm = null; _pcmPos = 0; }
                return;
            }
            freq = _frequency;
            slide = _slide;
            len = _lengthSeconds;
            age = _ageSeconds;
            duty = _duty;
            vol = _volume;
        }

        double secondsPerSample = 1.0 / sampleRate;
        short amp = (short)(0x6000 * vol);

        for (int i = 0; i < output.Length; i++)
        {
            if (freq <= 0.0 || age >= len)
            {
                output[i] = 0;
            }
            else
            {
                // Square wave: high half if phase < duty else low.
                output[i] = _phase < duty ? amp : (short)-amp;
                _phase += freq * secondsPerSample;
                while (_phase >= 1.0) _phase -= 1.0;
                // Slide duty cycle gently — the Follin trick.  Bounce
                // off the [0.1, 0.9] range so the tone stays audible.
                duty += slide * secondsPerSample;
                if (duty < 0.1) { duty = 0.1; slide = -slide; }
                else if (duty > 0.9) { duty = 0.9; slide = -slide; }
            }
            age += secondsPerSample;
        }

        lock (_lock)
        {
            _ageSeconds = age;
            _duty = duty;
            _slide = slide;
        }
    }
}
