namespace Subterra.Spectrum;

/// <summary>
/// Captures every transition of bit 4 of the value written to port
/// <c>$FE</c> (the Spectrum beeper output) along with the CPU cycle
/// at which the write happened.  The cassette's Follin player at
/// <c>$5E88</c> and all the short SFX entries (<c>$F8F9</c>,
/// <c>$F90E</c>, <c>$F93A</c>, <c>$F8B4</c>, <c>$F8D8</c>) drive
/// audio entirely through this one bit, so capturing it and
/// resampling to PCM is a complete, byte-faithful sound system.
///
/// The recorder coalesces consecutive same-value writes — only edges
/// are logged.  This keeps the event log compact even when the CPU
/// pounds the port at every cycle (which the Follin pulse-width
/// trick at <c>$FA47..$FA56</c> does).
///
/// See <see cref="RenderPcm"/> for the cycle → PCM conversion.
/// </summary>
public sealed class BeeperRecorder
{
    /// <summary>Spectrum CPU base clock.  Audio resampler uses this to
    /// map cycles → wall-clock time → sample positions.</summary>
    public const int CpuFrequencyHz = Spectrum48.CpuFrequencyHz;

    public readonly record struct Edge(long Cycle, bool High);

    private readonly List<Edge> _edges = new();
    private bool _lastHigh;

    /// <summary>Record an OUT to port $FE; bit 4 is the beeper line.
    /// Only logs transitions; same-value writes are dropped.</summary>
    public void OnPortWrite(long cycle, byte value)
    {
        bool high = (value & 0x10) != 0;
        if (_edges.Count == 0)
        {
            _edges.Add(new Edge(cycle, high));
            _lastHigh = high;
            return;
        }
        if (high != _lastHigh)
        {
            _edges.Add(new Edge(cycle, high));
            _lastHigh = high;
        }
    }

    public void Clear() { _edges.Clear(); _lastHigh = false; }

    public int EdgeCount => _edges.Count;
    public IReadOnlyList<Edge> Edges => _edges;

    /// <summary>Resample the captured edges to mono 16-bit PCM at
    /// <paramref name="sampleRate"/> Hz over the cycle range
    /// [<paramref name="startCycle"/>, <paramref name="endCycle"/>).
    /// Each sample equals ±<paramref name="amplitude"/> based on the
    /// latest beeper level at or before that sample's cycle.
    ///
    /// This is a SQUARE-WAVE resampler — no low-pass filter, no
    /// anti-aliasing.  Matches the actual ULA hardware: the speaker
    /// either crackles HIGH or LOW with nothing in between, and the
    /// "tone" we hear is the average pulse-width.  Tim Follin's
    /// pulse-width-modulation trick relies on exactly that.
    /// </summary>
    public short[] RenderPcm(long startCycle, long endCycle, int sampleRate, int amplitude = 8000)
    {
        if (endCycle <= startCycle) return Array.Empty<short>();
        long span = endCycle - startCycle;
        long sampleCount64 = span * sampleRate / CpuFrequencyHz;
        int sampleCount = (int)Math.Min(sampleCount64, int.MaxValue);
        if (sampleCount <= 0) return Array.Empty<short>();
        var pcm = new short[sampleCount];

        // Walk edges in parallel with samples.  For each sample i,
        // find the latest edge with cycle ≤ sampleCycle and use its
        // High level.
        int evtIdx = 0;
        bool currentHigh = false;
        // Initialize currentHigh from the most recent edge BEFORE
        // startCycle, if any.
        while (evtIdx < _edges.Count && _edges[evtIdx].Cycle < startCycle)
        {
            currentHigh = _edges[evtIdx].High;
            evtIdx++;
        }
        for (int i = 0; i < sampleCount; i++)
        {
            long sampleCycle = startCycle + (long)i * CpuFrequencyHz / sampleRate;
            while (evtIdx < _edges.Count && _edges[evtIdx].Cycle <= sampleCycle)
            {
                currentHigh = _edges[evtIdx].High;
                evtIdx++;
            }
            pcm[i] = currentHigh ? (short)amplitude : (short)-amplitude;
        }
        return pcm;
    }

    /// <summary>Drop edges older than <paramref name="keepFromCycle"/>,
    /// preserving the most recent pre-cutoff edge so RenderPcm starts
    /// with the correct initial level.  Use this in long live runs to
    /// keep memory bounded.</summary>
    public void Trim(long keepFromCycle)
    {
        int dropTo = 0;
        for (int i = 0; i < _edges.Count; i++)
        {
            if (_edges[i].Cycle >= keepFromCycle) { dropTo = Math.Max(0, i - 1); break; }
            dropTo = i + 1;
        }
        if (dropTo > 0) _edges.RemoveRange(0, dropTo);
    }
}
