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
    /// [<paramref name="startCycle"/>, <paramref name="endCycle"/>),
    /// using <b>area sampling</b>: each output sample equals the
    /// time-weighted average of the beeper level over that sample's
    /// cycle window, scaled to ±<paramref name="amplitude"/>.
    ///
    /// This is a perfect box-filter low-pass — anything above the
    /// Nyquist frequency that would otherwise alias as harsh
    /// distortion gets averaged out instead.  Tim Follin's
    /// pulse-width-modulation trick still works: a window that's
    /// 70% HIGH and 30% LOW produces a sample at +0.4·amp, exactly
    /// what the Spectrum's natural speaker low-pass would produce.
    ///
    /// (Nearest-neighbor sampling — which an earlier version used —
    /// produced very harsh aliasing for any signal above ~5 kHz,
    /// which the cassette emits constantly via the Follin player.)
    /// </summary>
    public short[] RenderPcm(long startCycle, long endCycle, int sampleRate, int amplitude = 5000)
    {
        if (endCycle <= startCycle) return Array.Empty<short>();
        long span = endCycle - startCycle;
        long sampleCount64 = span * sampleRate / CpuFrequencyHz;
        int sampleCount = (int)Math.Min(sampleCount64, int.MaxValue);
        if (sampleCount <= 0) return Array.Empty<short>();
        var pcm = new short[sampleCount];

        // Initial level from the most recent edge BEFORE startCycle.
        int evtIdx = 0;
        bool currentHigh = false;
        while (evtIdx < _edges.Count && _edges[evtIdx].Cycle < startCycle)
        {
            currentHigh = _edges[evtIdx].High;
            evtIdx++;
        }

        long prevSampleCycle = startCycle;
        for (int i = 0; i < sampleCount; i++)
        {
            long sampleEndCycle = startCycle + (long)(i + 1) * CpuFrequencyHz / sampleRate;
            long sampleSpan = sampleEndCycle - prevSampleCycle;
            if (sampleSpan <= 0)
            {
                pcm[i] = currentHigh ? (short)amplitude : (short)-amplitude;
                continue;
            }

            // Sum the HIGH-time within the sample window.  Walk every
            // edge with cycle ∈ [prevSampleCycle, sampleEndCycle); each
            // edge ends a HIGH or LOW stretch and starts the opposite.
            long highCycles = 0;
            long lastCycle = prevSampleCycle;
            while (evtIdx < _edges.Count && _edges[evtIdx].Cycle < sampleEndCycle)
            {
                long edgeCycle = _edges[evtIdx].Cycle;
                if (edgeCycle < lastCycle) edgeCycle = lastCycle;   // clamp pre-window edges
                if (currentHigh) highCycles += edgeCycle - lastCycle;
                lastCycle = edgeCycle;
                currentHigh = _edges[evtIdx].High;
                evtIdx++;
            }
            if (currentHigh) highCycles += sampleEndCycle - lastCycle;

            // Duty cycle ∈ [0, 1] → output ∈ [-amp, +amp].
            //   duty = highCycles / sampleSpan
            //   pcm  = amp · (2·duty − 1)
            //        = amp · (2·highCycles − sampleSpan) / sampleSpan
            long pcmValue = amplitude * (2 * highCycles - sampleSpan) / sampleSpan;
            pcm[i] = (short)pcmValue;
            prevSampleCycle = sampleEndCycle;
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
