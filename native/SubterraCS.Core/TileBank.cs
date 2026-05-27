namespace SubterraCS.Core;

/// <summary>
/// The 8×8 master tile bank.  Loaded from
/// <c>assets/extracted/tiles-b0f4.bin</c>; 384 contiguous tiles of 8
/// bytes each (one row of 8 pixels per byte, MSB on the left).
/// </summary>
public sealed class TileBank
{
    public byte[] Data { get; }
    public int TileCount => Data.Length / 8;

    public TileBank(byte[] data) => Data = data;

    public static TileBank Load(string path)
        => new(File.ReadAllBytes(path));

    /// <summary>Get the 8 row-bytes for the tile at <paramref name="index"/>.</summary>
    public ReadOnlySpan<byte> this[int index]
        => Data.AsSpan(index * 8, 8);
}

/// <summary>
/// 21-cell UDG bank (8×8 each).  Loaded from <c>udgs-e62b.bin</c>.
/// Same format as <see cref="TileBank"/>.
/// </summary>
public sealed class UdgBank
{
    public byte[] Data { get; }
    public int Count => Data.Length / 8;

    public UdgBank(byte[] data) => Data = data;

    public static UdgBank Load(string path) => new(File.ReadAllBytes(path));

    public ReadOnlySpan<byte> this[int index]
        => Data.AsSpan(index * 8, 8);
}
