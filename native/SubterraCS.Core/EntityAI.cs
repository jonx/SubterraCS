namespace SubterraCS.Core;

/// <summary>
/// Per-entity-type behaviour and spawn-pattern table.  The original game
/// has individual AI subroutines per type id, dispatched from the entity
/// dispatcher at <c>$F1A5</c>; we approximate each archetype here based on
/// the decoded entity-bank renders (MEMORY-MAP §entity-type table).
///
/// Each <see cref="EntityKind"/> bundles spawn position, initial velocity,
/// per-tick update, on-shoot reward, and on-collide cost.  Anything we
/// haven't reverse-engineered precisely falls back to "drift downward",
/// which matches enough of the original cave-decor flavour to look right.
/// </summary>
public static class EntityAI
{
    public enum Kind
    {
        Worker,        // type 0 — rescuable, walks at the bottom
        Lava,          // type 1 — drips downward then puddles
        Stalactite,    // type 2 — clings to ceiling, drops when player passes
        FallingRock,   // type 3 — drops with horizontal sway
        Drone,         // type 4 — flies horizontally across the screen
        MineCart,      // type 5 — rolls along the cave floor
        Wagon,         // type 6 — like mine-cart, opposite direction
        Sparks,        // type 7 — short-lived particle burst, harmless
        Explosion,     // type 8 — expanding decor, harmless after a beat
        FlameDrip,     // type 9 — flame drips from ceiling
        Vine,          // type 10 — static decor (trees / roots)
        Creature,      // type 11 — chases player slowly
        Bubble,        // type 12 — rises upward
        ForceField,    // type 13 — slow vertical pillar
        Pipe,          // type 14 — static pipe segment, blocks bullets
        Bowtie,        // type 15 — drifts in a sine pattern
        Robot,         // type 16 — chases horizontally only
        ElectricArc,   // type 18 ($12) — door guard, drains shield, blocks until rescued
        Generic,       // fallback
    }

    public static Kind For(int typeId) => typeId switch
    {
        0  => Kind.Worker,
        1  => Kind.Lava,
        2  => Kind.Stalactite,
        3  => Kind.FallingRock,
        4  => Kind.Drone,
        5  => Kind.MineCart,
        6  => Kind.Wagon,
        7  => Kind.Sparks,
        8  => Kind.Explosion,
        9  => Kind.FlameDrip,
        10 => Kind.Vine,
        11 => Kind.Creature,
        12 => Kind.Bubble,
        13 => Kind.ForceField,
        14 => Kind.Pipe,
        15 => Kind.Bowtie,
        16 => Kind.Robot,
        18 => Kind.ElectricArc,
        _  => Kind.Generic,
    };

    // NOTE: an earlier port had a per-kind CollisionRule table here
    // (touch-damage, pickups, ConsumedOnContact).  Removed: the full
    // $F1EF disasm + $DD4D walker analysis (docs/disasm/entities.md,
    // damages.md) show the cassette has NO coord-based entity-vs-
    // player interaction — decor entities hurt the player solely via
    // the $DCF5 XOR pixel-overlap, and are never consumed.

    /// <summary>Points awarded for shooting this entity.
    /// PORT-ONLY: System-A decor draws by overwrite ($F2BC) with no
    /// $EF beam check, so the cassette's laser can't hurt DECOR —
    /// ships/boss are different, see laser.md §$E9F0.</summary>
    public static int ShootScore(Kind kind) => kind switch
    {
        Kind.Worker      =>   0,   // do not shoot the workers!
        Kind.Sparks      =>   0,
        Kind.Vine        =>   0,
        Kind.Pipe        =>   0,
        Kind.Bubble      =>   5,
        Kind.Stalactite  =>  20,
        Kind.FallingRock =>  15,
        Kind.Drone       =>  40,
        Kind.MineCart    => 100,
        Kind.Wagon       => 100,
        Kind.Creature    =>  60,
        Kind.FlameDrip   =>  15,
        Kind.Explosion   =>   5,
        Kind.Lava        =>  10,
        Kind.ForceField  =>  25,
        Kind.Bowtie      =>  30,
        Kind.Robot       =>  80,
        _                =>  10,
    };

    /// <summary>True if a bullet should pass through this kind without effect.</summary>
    public static bool IsBulletProof(Kind kind) =>
        kind == Kind.Worker || kind == Kind.Vine || kind == Kind.Pipe || kind == Kind.Sparks;

    /// <summary>
    /// Set up freshly-spawned entity's starting state.
    /// </summary>
    public static void InitSpawn(
        EntityInstance e, Kind kind, Random rng, byte flags,
        int playerX, int playerY)
    {
        e.AgeFrames = 0;
        e.Hp = kind switch
        {
            Kind.Drone or Kind.MineCart or Kind.Wagon or Kind.Robot or Kind.Creature => 2,
            Kind.ForceField => 3,
            _ => 1,
        };
        switch (kind)
        {
            case Kind.Worker:
                // Walks along the bottom; spawn just above the floor.
                e.X = rng.Next(24, 232);
                e.Y = 160;
                e.DX = rng.Next(0, 2) == 0 ? -1 : 1;
                e.DY = 0;
                break;
            case Kind.Lava:
            case Kind.FlameDrip:
            case Kind.Stalactite:
                e.X = rng.Next(16, 240);
                e.Y = 16;
                e.DX = 0;
                e.DY = 0;        // starts still, drops on its own timer
                break;
            case Kind.FallingRock:
                e.X = rng.Next(16, 240);
                e.Y = -8;
                e.DX = rng.Next(-1, 2);
                e.DY = 2;
                break;
            case Kind.Drone:
            case Kind.Robot:
                {
                    bool fromLeft = rng.Next(0, 2) == 0;
                    e.X = fromLeft ? -16 : 272;
                    e.Y = 40 + rng.Next(0, 80);
                    e.DX = fromLeft ? 2 : -2;
                    e.DY = 0;
                }
                break;
            case Kind.MineCart:
                e.X = -16;
                e.Y = 152;
                e.DX = 3;
                e.DY = 0;
                break;
            case Kind.Wagon:
                e.X = 272;
                e.Y = 152;
                e.DX = -3;
                e.DY = 0;
                break;
            case Kind.Sparks:
            case Kind.Explosion:
                e.X = rng.Next(16, 240);
                e.Y = rng.Next(24, 152);
                e.DX = 0;
                e.DY = 0;
                break;
            case Kind.Vine:
                e.X = rng.Next(0, 2) == 0 ? 8 : 240;  // hug a wall
                e.Y = rng.Next(24, 152);
                e.DX = 0;
                e.DY = 0;
                break;
            case Kind.Creature:
                e.X = rng.Next(16, 240);
                e.Y = -16;
                e.DX = 0;
                e.DY = 1;
                break;
            case Kind.Bubble:
                e.X = rng.Next(16, 240);
                e.Y = 160;
                e.DX = (rng.Next(0, 2) == 0 ? -1 : 1);
                e.DY = -1;
                break;
            case Kind.ForceField:
                e.X = rng.Next(40, 216);
                e.Y = -16;
                e.DX = 0;
                e.DY = 1;
                break;
            case Kind.Pipe:
                e.X = rng.Next(16, 240);
                e.Y = -16;
                e.DX = 0;
                e.DY = 1;
                break;
            case Kind.Bowtie:
                e.X = rng.Next(16, 240);
                e.Y = -16;
                e.DX = 0;
                e.DY = 1;
                break;
            default:
                e.X = rng.Next(16, 240);
                e.Y = -16;
                e.DX = (flags & 0x40) != 0
                    ? (rng.Next(0, 2) == 0 ? -1 : 1)
                    : 0;
                e.DY = 1 + rng.Next(0, 2);
                break;
        }
    }

    /// <summary>
    /// Advance an entity one frame — faithful port of the full
    /// <c>$F1EF</c> processor (see docs/disasm/entities.md).  Verdict
    /// from the end-to-end disasm: the cassette NEVER moves System-A
    /// entities.  The frame byte at record +2 is the only field ever
    /// written back ($F209..$F210, advanced on time-slice 0 of the
    /// 4-frame $F593 cycle = every 4th frame, with
    /// frame = (frame+1) AND (maxFrames-1)); every "falling rock" or
    /// "flying drone" is a 16-frame animation playing inside a fixed
    /// 16×16 box.  Entities never expire and are never consumed — the
    /// records live for the whole level.
    ///
    /// (An earlier port moved Drones/Rocks/Carts and expired
    /// Sparks/Explosions — all invented; removed once $F1EF was
    /// decoded end-to-end.  The per-kind movement in
    /// <see cref="InitSpawn"/> remains only as flavour for the
    /// port-only procedural levels at depth 6+, where there is no
    /// cassette ground truth.)
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
