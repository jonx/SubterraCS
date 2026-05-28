namespace SubterraCS.Core;

/// <summary>
/// The six hand-authored spawn schedules from the original tape at
/// <c>$E69D</c>.  Each is 32 bytes (8 × 4-byte entries) describing the
/// (timer, type, flags) recipe for one page of the cave.  Re-rendered
/// faithfully here so the first six pages of the native port play the
/// *same* level the 1985 cassette delivered.
///
/// We re-scale the original 16-bit timers down to something tractable
/// on our straight 50 Hz decrement (the original executor at
/// <c>$EF02</c> ran a multi-pass slicing scheme — see RE-LOG §16 — so
/// its raw 16-bit countdowns actually elapsed much faster than once
/// per frame).  Empirically <c>raw / 32</c> reproduces a comparable
/// spawn cadence.
/// </summary>
public static class OriginalLevels
{
    public const int Count = 6;

    public static SpawnSchedule[] Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        if (raw.Length < Count * 32)
        {
            throw new InvalidDataException(
                $"level-schedules file must be at least {Count * 32} bytes; got {raw.Length}.");
        }
        var pages = new SpawnSchedule[Count];
        for (int p = 0; p < Count; p++)
        {
            var entries = new ScheduleEntry[SpawnSchedule.Slots];
            for (int i = 0; i < SpawnSchedule.Slots; i++)
            {
                int o = p * 32 + i * 4;
                ushort rawTimer = (ushort)(raw[o] | (raw[o + 1] << 8));
                // Rescale: the original ran multi-pass decrement; we tick
                // once per 50 Hz frame.  Clamp to a reasonable window so
                // the first entry on each page fires within a couple of
                // seconds rather than minutes.
                ushort timer = (ushort)Math.Clamp((rawTimer >> 5) + 16, 16, 512);
                byte type = raw[o + 2];
                byte flags = raw[o + 3];
                entries[i] = new ScheduleEntry(timer, type, flags);
            }
            pages[p] = new SpawnSchedule(entries);
        }
        return pages;
    }
}
