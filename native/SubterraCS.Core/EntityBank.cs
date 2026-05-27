namespace SubterraCS.Core;

/// <summary>
/// The big asset blob holding every entity type's sprite frames.
/// In-game these live at <c>$B8F4..$D6F4</c>; we load them as a single
/// flat byte array (<c>assets/extracted/entity-banks-b8f4.bin</c>),
/// and offset by <c>(typePointer - $B8F4)</c> to find a given type's
/// bank.  Each type occupies 16 frames × 32 bytes = 512 bytes.
/// </summary>
public sealed class EntityBank
{
    public byte[] Data { get; }
    public const ushort BaseAddress = 0xB8F4;

    public EntityBank(byte[] data) => Data = data;

    public static EntityBank Load(string path) => new(File.ReadAllBytes(path));

    /// <summary>
    /// Get the 32 bytes for one frame of one entity type.
    /// <paramref name="typePointer"/> is the original Spectrum address
    /// (e.g. $B8F4 for type 0).
    /// </summary>
    public ReadOnlySpan<byte> Frame(ushort typePointer, int frameIndex)
    {
        int baseOffset = typePointer - BaseAddress;
        if (baseOffset < 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }
        int offset = baseOffset + frameIndex * 32;
        if (offset + 32 > Data.Length)
        {
            return ReadOnlySpan<byte>.Empty;
        }
        return Data.AsSpan(offset, 32);
    }
}
