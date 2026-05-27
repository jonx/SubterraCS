namespace SubterraCS.Core;

/// <summary>
/// One live entity in the game.  Same shape as the original's 8-byte
/// slot at <c>($F1B9)</c>, but unwrapped into named fields.
/// </summary>
public sealed class EntityInstance
{
    public int TypeId;       // index into the EntityTypeTable
    public int X;            // pixel column (0..255)
    public int Y;            // pixel row (0..191)
    public int Frame;        // 0..MaxFrames-1
    public int DX, DY;       // signed per-frame velocity
    public int FrameTick;    // counts up; advance frame when > threshold
    public bool Alive;
}

/// <summary>The 8-slot bullet / particle list (matches <c>$E881</c>).</summary>
public sealed class Bullet
{
    public int X, Y, DX, DY;
    public bool Alive;
    public byte Pattern;     // single-byte XOR pattern (e.g. 0x80, 0x40, …)
}
