namespace SubterraCS.Core;

/// <summary>
/// 8-entry × 4-byte spawn schedule, matching the original level format
/// at <c>$E69D</c>.  Each entry: 16-bit countdown, entity-type id,
/// flag bits.
/// </summary>
public readonly record struct ScheduleEntry(ushort Timer, byte TypeId, byte Flags);

public sealed class SpawnSchedule
{
    public const int Slots = 8;
    public ScheduleEntry[] Entries { get; }

    public SpawnSchedule(ScheduleEntry[] entries)
    {
        if (entries.Length != Slots)
        {
            throw new ArgumentException($"Spawn schedule must have {Slots} entries.");
        }
        Entries = entries;
    }

    public static SpawnSchedule From32Bytes(ReadOnlySpan<byte> raw)
    {
        var arr = new ScheduleEntry[Slots];
        for (int i = 0; i < Slots; i++)
        {
            ushort timer = (ushort)(raw[i * 4] | (raw[i * 4 + 1] << 8));
            byte type = raw[i * 4 + 2];
            byte flags = raw[i * 4 + 3];
            arr[i] = new ScheduleEntry(timer, type, flags);
        }
        return new SpawnSchedule(arr);
    }
}
