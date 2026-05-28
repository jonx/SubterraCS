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
        _  => Kind.Generic,
    };

    /// <summary>How the entity damages or rewards the player on contact.</summary>
    public readonly record struct CollisionRule(
        int ShieldDelta,   // negative = damage; positive = pickup heal
        int FuelDelta,
        int ScoreOnContact,
        int RescuedDelta,
        bool ConsumedOnContact);

    public static CollisionRule Collision(Kind kind) => kind switch
    {
        Kind.Worker      => new CollisionRule(+5,   0,   50, +1, true),  // pick up
        Kind.Sparks      => new CollisionRule( 0,   0,    0,  0, true),
        Kind.Vine        => new CollisionRule( 0,   0,    0,  0, false), // pass-through
        Kind.Pipe        => new CollisionRule(-2,   0,    0,  0, false), // grazes
        Kind.Bubble      => new CollisionRule( 0,  +2,    0,  0, true),  // small fuel boost
        Kind.Explosion   => new CollisionRule(-3,   0,    0,  0, false),
        Kind.Lava        => new CollisionRule(-8,  -5,    0,  0, true),
        Kind.FlameDrip   => new CollisionRule(-6,   0,    0,  0, true),
        Kind.Stalactite  => new CollisionRule(-10,  0,    0,  0, true),
        Kind.FallingRock => new CollisionRule(-8,   0,    0,  0, true),
        Kind.Drone       => new CollisionRule(-12,  0,    0,  0, true),
        Kind.MineCart    => new CollisionRule(-15, -5,    0,  0, false),
        Kind.Wagon       => new CollisionRule(-15, -5,    0,  0, false),
        Kind.Creature    => new CollisionRule(-12,  0,    0,  0, true),
        Kind.ForceField  => new CollisionRule(-4,   0,    0,  0, false),
        Kind.Bowtie      => new CollisionRule(-6,   0,    0,  0, true),
        Kind.Robot       => new CollisionRule(-10,  0,    0,  0, true),
        _                => new CollisionRule(-5,   0,    0,  0, true),
    };

    /// <summary>Points awarded for shooting this entity.</summary>
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
    /// Advance an entity one frame.  Returns false when the entity
    /// should be removed (off-screen or natural lifetime exhausted).
    ///
    /// In the original game most entities placed by the per-level
    /// list at $F2E8 are STATIONARY — they sit at their placed (x, y)
    /// and only the player navigates around them (by altitude change
    /// and horizontal scroll).  A few types do move (Drones, Falling
    /// Rocks, MineCarts, Wagons).  Sparks/Explosion are short-lived
    /// effects.
    /// </summary>
    public static bool Tick(
        EntityInstance e, Kind kind, int playerX, int playerY,
        Random rng)
    {
        e.AgeFrames++;
        switch (kind)
        {
            // ─── Moving hazards (the few that actually patrol) ───────
            case Kind.FallingRock:
                e.Y += e.DY;
                break;

            case Kind.Drone:
            case Kind.Robot:
                e.X += e.DX;
                break;

            case Kind.MineCart:
            case Kind.Wagon:
                e.X += e.DX;
                break;

            // ─── Short-lived effects ────────────────────────────────
            case Kind.Sparks:
                if (e.AgeFrames > 24) return false;
                break;

            case Kind.Explosion:
                if (e.AgeFrames > 18) return false;
                break;

            // ─── Everything else is stationary ──────────────────────
            // Workers, Lava, Stalactite, FlameDrip, Vine, Creature,
            // Bubble, ForceField, Pipe, Bowtie, Generic — they all sit
            // at their placed (x, y) position.  Per the user's
            // feedback "they should stay at a specific spot."
            default:
                break;
        }

        // Animate the frame counter (sprite cycle) even for stationary
        // entities — many have multi-frame idle animations.
        e.FrameTick++;
        if (e.FrameTick >= 4)
        {
            e.FrameTick = 0;
            if (e.MaxFrames > 0) e.Frame = (e.Frame + 1) % e.MaxFrames;
        }

        // Off-screen culling — only for the moving kinds; static
        // entities can be off-screen during scroll and still exist.
        if (kind is Kind.Drone or Kind.Robot or Kind.MineCart or Kind.Wagon
                  or Kind.FallingRock)
        {
            if (e.Y < -32 || e.Y > 200 || e.X < -64 || e.X > 320) return false;
        }
        return true;
    }
}
