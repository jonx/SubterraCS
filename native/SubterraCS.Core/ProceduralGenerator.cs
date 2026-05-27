namespace SubterraCS.Core;

/// <summary>
/// Generates fresh spawn schedules so the game can keep going for ever.
/// The original ships six hard-coded 32-byte schedules at <c>$E69D</c>;
/// once you've been through them all, this generator takes over and
/// produces deterministic-but-varied new pages keyed on the depth.
///
/// Design choices:
///
/// * **Difficulty curve.** Each successive page squeezes a few cycles
///   off the spawn timers, so the cave gets denser as the player dives
///   deeper.
/// * **Mix of types.** Each slot picks a type from a weighted pool
///   skewed toward whatever's interesting at this depth — early pages
///   favour falling rocks and stalactites; deeper pages add lava,
///   creatures, and explosions.
/// * **Deterministic seeding.** Pages are seeded from
///   `(baseSeed, depth)` so a given playthrough is reproducible. Pass
///   `Environment.TickCount` to <see cref="ProceduralGenerator(int)"/>
///   for a fresh experience each run.
/// </summary>
public sealed class ProceduralGenerator
{
    private readonly int _baseSeed;

    public ProceduralGenerator(int baseSeed) => _baseSeed = baseSeed;

    /// <summary>
    /// Build the spawn schedule for the page at the given depth index
    /// (0-based, monotonically increasing).
    /// </summary>
    public SpawnSchedule Page(int depth)
    {
        var rng = new Random(HashCode.Combine(_baseSeed, depth));

        // Difficulty: timers shrink by ~10 % every 4 pages, clamped.
        // Keep them short enough that something is always spawning at
        // 50 Hz — the original ran on a Z80 polling each timer; we
        // just decrement-and-wrap.
        int curve = depth / 4;
        int baseTimer = Math.Max(0x0020, 0x0080 - curve * 0x0008);

        // Weighted type pool — type ids match the original entity-type
        // table.  We keep "shovels/workers" (type 0) rare because they
        // are visually small and act as the "rescue" entity; we lean on
        // the dramatic decor types instead.
        ReadOnlySpan<int> typePool = stackalloc int[]
        {
            // Early pages
            1, 2, 3,    // lava, stalactites, falling rocks (3× each)
            1, 2, 3,
            1, 2, 3,
            4,          // flying drones
            5,          // mine cart
            7,          // dust / sparks
            // Later types creep in as depth increases
            8, 9, 10, 11, 12,
        };
        // Bias toward later entries as depth grows.
        int poolMin = 0;
        int poolMax = Math.Min(typePool.Length, 3 + depth / 2);

        var entries = new ScheduleEntry[SpawnSchedule.Slots];
        for (int i = 0; i < SpawnSchedule.Slots; i++)
        {
            // Timer scattered around baseTimer ± 12 %.
            int jitter = rng.Next(-baseTimer / 8, baseTimer / 8);
            ushort timer = (ushort)Math.Max(8, baseTimer + jitter);
            int type = typePool[rng.Next(poolMin, poolMax)];
            byte flags = (byte)(rng.Next(0, 2) == 0 ? 0x40 : 0x00);
            entries[i] = new ScheduleEntry(timer, (byte)type, flags);
        }
        return new SpawnSchedule(entries);
    }
}
