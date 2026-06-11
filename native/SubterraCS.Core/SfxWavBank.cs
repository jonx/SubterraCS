namespace SubterraCS.Core;

/// <summary>
/// Bank of authentic cassette sound effects, rendered to WAV by the
/// <c>subterra sfx-render</c> tool (which runs the ORIGINAL Z80 sound
/// routines in the emulator and captures the beeper — see
/// docs/disasm/sound.md).  Files live in <c>assets/extracted/sfx/</c>
/// as mono 16-bit PCM at the native audio device rate (22 050 Hz) so
/// samples play back 1:1 with no resampling.
///
/// The bank is optional: if the directory is missing the game falls
/// back to the synthesised <see cref="BeeperSynth"/> tones.
/// </summary>
public sealed class SfxWavBank
{
    private readonly Dictionary<string, short[]> _samples = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => _samples.Count == 0;

    public static SfxWavBank Load(string dir)
    {
        var bank = new SfxWavBank();
        if (!Directory.Exists(dir)) return bank;
        foreach (var path in Directory.EnumerateFiles(dir, "*.wav"))
        {
            var pcm = ReadMono16Wav(path);
            if (pcm.Length > 0)
                bank._samples[Path.GetFileNameWithoutExtension(path)] = pcm;
        }
        return bank;
    }

    public bool TryGet(string name, out short[] pcm)
        => _samples.TryGetValue(name, out pcm!);

    /// <summary>Minimal reader for the WAVs our own WavWriter produces
    /// (44-byte canonical header, mono, 16-bit PCM).  Returns empty on
    /// anything unexpected rather than throwing — a malformed file
    /// just means that effect falls back to the synth.</summary>
    private static short[] ReadMono16Wav(string path)
    {
        var raw = File.ReadAllBytes(path);
        if (raw.Length < 44) return Array.Empty<short>();
        // "RIFF" .... "WAVE" "fmt " — sanity only; we trust our writer.
        if (raw[0] != 'R' || raw[1] != 'I' || raw[8] != 'W' || raw[12] != 'f')
            return Array.Empty<short>();
        int channels = raw[22] | (raw[23] << 8);
        int bits = raw[34] | (raw[35] << 8);
        if (channels != 1 || bits != 16) return Array.Empty<short>();
        int dataSize = raw[40] | (raw[41] << 8) | (raw[42] << 16) | (raw[43] << 24);
        int count = Math.Min(dataSize, raw.Length - 44) / 2;
        var pcm = new short[count];
        for (int i = 0; i < count; i++)
            pcm[i] = (short)(raw[44 + i * 2] | (raw[45 + i * 2] << 8));
        return pcm;
    }
}
