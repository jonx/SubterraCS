namespace SubterraCS.Core;

/// <summary>
/// One entry in the entity-type table at <c>$F5A0</c>:
/// pointer to the 16-frame bank, frame count, attribute byte.
/// </summary>
public readonly record struct EntityType(ushort SpritePointer, byte MaxFrames, byte Attribute);

public sealed class EntityTypeTable
{
    public EntityType[] Types { get; }

    public EntityTypeTable(EntityType[] types) => Types = types;

    public static EntityTypeTable Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        if (raw.Length < 4 || raw.Length % 4 != 0)
        {
            throw new InvalidDataException(
                $"entity-types file must be a multiple of 4 bytes; got {raw.Length}.");
        }
        var list = new List<EntityType>();
        for (int i = 0; i + 3 < raw.Length; i += 4)
        {
            ushort ptr = (ushort)(raw[i] | (raw[i + 1] << 8));
            byte frames = raw[i + 2];
            byte attr = raw[i + 3];
            // The table runs out into unrelated bytes after ~22 entries;
            // stop when the pointer leaves a plausible RAM region.
            if (ptr < 0x4000 || ptr >= 0xE000) break;
            list.Add(new EntityType(ptr, frames, attr));
        }
        return new EntityTypeTable(list.ToArray());
    }
}
