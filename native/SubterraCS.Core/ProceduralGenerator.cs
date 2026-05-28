namespace SubterraCS.Core;

/// <summary>
/// Generates fresh spawn schedules so the game can keep going for ever.
/// The original ships six hard-coded 32-byte schedules at <c>$E69D</c>;
/// once you've been through them all, this generator takes over and
/// produces deterministic-but-varied new pages keyed on the depth.
///
/// Each generated page picks a <see cref="Theme"/> first, which biases
/// its entity mix — a "Drone Swarm" page is heavy on flying type-4s
/// and the occasional mine cart; a "Rescue Mission" page sprinkles
/// workers to recover; a "Lava Chamber" page is mostly type-1 + type-9
/// drips with a thin background of stalactites.  This gives the
/// infinite mode a sense of place that the original's six fixed pages
/// already had — instead of just "more falling rocks", each new level
/// feels like a new room.
///
/// Difficulty still rises with depth: timers shrink and the theme pool
/// expands, so deeper pages get faster and meaner.
///
/// Deterministic: seeded from (baseSeed, depth), so re-running with the
/// same `--seed=` flag reproduces the same sequence of themes.
/// </summary>
public sealed class ProceduralGenerator
{
    private readonly int _baseSeed;

    public ProceduralGenerator(int baseSeed) => _baseSeed = baseSeed;

    public enum Theme
    {
        RockFall,        // baseline cave — rocks + stalactites
        LavaChamber,     // lava drips + flame drops
        DroneSwarm,      // flying drones + robots
        RescueMission,   // workers everywhere, fewer hazards
        DeepCreatures,   // creatures + bow-ties + force-fields
        MineYard,        // mine carts + wagons rolling through
        Bubbles,         // bubbles + sparks — fuel-positive page
    }

    public Theme ThemeForDepth(int depth)
    {
        // Pages 6..11 cycle each archetype once so the player gets to
        // see each room.  After that we pick from a depth-broadened
        // pool seeded so a given playthrough is reproducible.
        Theme[] intro =
        [
            Theme.RockFall,
            Theme.RescueMission,
            Theme.LavaChamber,
            Theme.DroneSwarm,
            Theme.MineYard,
            Theme.Bubbles,
            Theme.DeepCreatures,
        ];
        if (depth < intro.Length) return intro[depth];

        var rng = new Random(HashCode.Combine(_baseSeed, depth, 0xBEEF));
        int span = Math.Min(7, 3 + (depth - intro.Length) / 4);
        return (Theme)rng.Next(0, span);
    }

    /// <summary>
    /// Build the spawn schedule for the page at the given depth index
    /// (0-based, monotonically increasing — note depth 0..5 are served
    /// by the original cassette schedules, so the procedural generator
    /// only sees depth 6+ in practice).
    /// </summary>
    public SpawnSchedule Page(int depth)
    {
        var rng = new Random(HashCode.Combine(_baseSeed, depth));
        var theme = ThemeForDepth(depth);

        // Difficulty: timers shrink by ~10 % every 4 pages, clamped.
        int curve = depth / 4;
        int baseTimer = Math.Max(0x0018, 0x0070 - curve * 0x0008);

        var pool = PoolForTheme(theme);
        var entries = new ScheduleEntry[SpawnSchedule.Slots];
        for (int i = 0; i < SpawnSchedule.Slots; i++)
        {
            int jitter = rng.Next(-baseTimer / 6, baseTimer / 6);
            ushort timer = (ushort)Math.Max(8, baseTimer + jitter);
            int type = pool[rng.Next(0, pool.Length)];
            byte flags = (byte)(rng.Next(0, 2) == 0 ? 0x40 : 0x00);
            entries[i] = new ScheduleEntry(timer, (byte)type, flags);
        }
        return new SpawnSchedule(entries);
    }

    private static int[] PoolForTheme(Theme theme) => theme switch
    {
        // Type ids match the entity-type table at $F5A0 / EntityAI.For.
        Theme.RockFall       => [2, 2, 3, 3, 3, 7, 11],
        Theme.LavaChamber    => [1, 1, 1, 9, 9, 2, 7],
        Theme.DroneSwarm     => [4, 4, 4, 16, 16, 7, 3],
        Theme.RescueMission  => [0, 0, 0, 0, 2, 3, 10],   // workers ×4
        Theme.DeepCreatures  => [11, 11, 13, 15, 15, 3, 8],
        Theme.MineYard       => [5, 5, 6, 6, 3, 14, 2],
        Theme.Bubbles        => [12, 12, 12, 7, 10, 1, 2],
        _                    => [1, 2, 3, 7],
    };
}
