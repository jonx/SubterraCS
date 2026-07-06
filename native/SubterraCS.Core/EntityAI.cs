namespace SubterraCS.Core;

/// <summary>
/// System-A entity behaviour.  The full <c>$F1EF</c> disasm verdict
/// (docs/disasm/entities.md) is that the cassette has NO per-type AI:
/// the only record field ever written back is the frame byte, so every
/// entity is a looping animation inside a fixed 16×16 box — eternal,
/// never moved, never consumed.  <see cref="Tick"/> is that, exactly.
///
/// The two small tables below exist ONLY for the modern mode's
/// laser-vs-decor extension (historic mode never consults them — the
/// cassette's beam cannot hurt decor: $F2BC blits with no $EF check).
/// Type ids follow the verified $F5A0 table (entities.md §Type-to-
/// sprite mapping): $01 decor, $02 stalactite, $08 lava/sparks,
/// $09 drip, $0A drone, $12 electric arc.
/// </summary>
public static class EntityAI
{
    /// <summary>MODERN ONLY: points for destroying a decor entity
    /// with the laser.  No cassette counterpart (the beam kills only
    /// ships +15 and boss +20 — laser.md §$E9F0).</summary>
    public static int ShootScore(int typeId) => typeId switch
    {
        0x0A => 40,   // drone
        0x02 => 20,   // stalactite
        0x08 => 10,   // lava/sparks
        0x09 => 10,   // drip
        _    => 10,
    };

    /// <summary>MODERN ONLY: entities the laser passes through.
    /// The electric arc ($12) guards the exit and must not be
    /// shootable; type 0 records are left untouchable too.</summary>
    public static bool IsBulletProof(int typeId) =>
        typeId == 0x12 || typeId == 0x00;

    /// <summary>
    /// Advance an entity one frame — faithful port of the full
    /// <c>$F1EF</c> processor.  The frame byte is the only mutable
    /// state ($F209..$F210, advanced on time-slice 0 of the 4-frame
    /// $F593 cycle, masked with (maxFrames−1)).
    /// </summary>
    public static void Tick(EntityInstance e)
    {
        e.AgeFrames++;
        // $F593 slice 0 advances the frame: once per 4 ticks.
        e.FrameTick++;
        if (e.FrameTick >= 4)
        {
            e.FrameTick = 0;
            if (e.MaxFrames > 0) e.Frame = (e.Frame + 1) % e.MaxFrames;
        }
    }
}
