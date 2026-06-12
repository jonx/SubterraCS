namespace SubterraCS.Core;

/// <summary>
/// Discrete one-shot game-event sounds.  The interactive runner forwards
/// these to the <see cref="BeeperSynth"/>; the headless runner can ignore
/// them.  Centralising the table here lets the World stay pure (no audio
/// dependency) while still driving rich SFX.
///
/// Frequencies & slides hand-tuned to evoke the 1985 beeper without
/// re-implementing the exact Z80 sound routines (which are scattered
/// across $E8FD's effect handler chain).
/// </summary>
public enum SfxKind
{
    None,
    Fire,
    Hit,
    Explode,
    Pickup,
    Thrust,
    Damage,
    GameOver,
    LevelUp,
    // Events whose cassette sounds exist only as never-played $F8xx
    // messages (sound.md §vestigial).  Silent in faithful mode; the
    // runner's Lost Sounds toggle maps them to the lost-*.wav
    // reconstructions.
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

    public static (double hz, int frames, double slide) Voice(SfxKind s) => s switch
    {
        SfxKind.Fire     => (1200, 3, -200),
        SfxKind.Hit      => ( 480, 5, +120),
        SfxKind.Explode  => ( 110, 10, -40),
        SfxKind.Pickup   => ( 880, 6, +200),
        SfxKind.Thrust   => ( 220, 12, -60),
        SfxKind.Damage   => ( 180, 8, -80),
        SfxKind.GameOver => (  90, 50, -20),
        SfxKind.LevelUp  => ( 660, 12, +320),
        // FuelLow/ShieldLow keep the old warning tone in faithful
        // mode; BossAlert is silent there (the cassette plays nothing
        // at boss spawn — its alert message is vestigial).
        SfxKind.FuelLow or SfxKind.ShieldLow => (180, 8, -80),
        _                => (   0, 0,    0),
    };
}
