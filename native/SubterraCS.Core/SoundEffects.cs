namespace SubterraCS.Core;

/// <summary>
/// Discrete one-shot game-event sounds.  The interactive runner
/// forwards these to the audio layer; the headless runner ignores
/// them.
///
/// The cassette plays exactly THREE sounds in-game (sound.md): the
/// $DDC4 hit click (player damage), the $E419 bar-fill, and the $E135
/// spawn-in.  Those map to <see cref="Damage"/>, <see cref="BarFill"/>
/// and <see cref="SpawnIn"/> — captured as WAVs and played in every
/// mode.  Every other kind is an event the cassette left SILENT (its
/// $F8xx message system is vestigial — queued, never played); they
/// only sound in the Designed / Historical modes, or as modern synth
/// flavour.
/// </summary>
public enum SfxKind
{
    None,
    // The cassette's real in-game sounds:
    Damage,     // $DDC4 hit click
    BarFill,    // $E419 refill (level start, respawn, fuel station)
    SpawnIn,    // $E135 dots-converge
    // Events silent on the cassette — Designed/Historical/modern only:
    Fire,
    Hit,
    Explode,
    Pickup,
    GameOver,
    LevelUp,
    BossAlert,
    FuelLow,
    ShieldLow,
}

public sealed class SfxQueue
{
    private readonly Queue<SfxKind> _q = new();

    public void Trigger(SfxKind s)
    {
        if (s != SfxKind.None) _q.Enqueue(s);
    }

    public bool TryDequeue(out SfxKind s)
    {
        if (_q.Count == 0) { s = SfxKind.None; return false; }
        s = _q.Dequeue();
        return true;
    }

    /// <summary>Synth fallback tones for the modern/Designed paths
    /// when no WAV exists.  Never used for the cassette's real sounds
    /// (those always play their captured WAV).</summary>
    public static (double hz, int frames, double slide) Voice(SfxKind s) => s switch
    {
        SfxKind.Fire     => (1200, 3, -200),
        SfxKind.Hit      => ( 480, 5, +120),
        SfxKind.Explode  => ( 110, 10, -40),
        SfxKind.Pickup   => ( 880, 6, +200),
        SfxKind.Damage   => ( 180, 8, -80),
        SfxKind.GameOver => (  90, 50, -20),
        SfxKind.LevelUp  => ( 660, 12, +320),
        SfxKind.FuelLow or SfxKind.ShieldLow => (180, 8, -80),
        _                => (   0, 0,    0),
    };
}
