namespace SubterraCS.Core;

/// <summary>
/// One live entity in the game.  Same shape as the original's 8-byte
/// slot at <c>($F1B9)</c>, but unwrapped into named fields plus a few
/// extras we need on top (HP, lifetime, cached MaxFrames) to drive the
/// per-type AI table in <see cref="EntityAI"/>.
///
/// <para><b>WorldX</b> is the record's +1 byte (its world-byte position
/// along the 256-byte-wide level).  The current screen X is computed
/// per-frame as <c>(WorldX − ScrollCursor) * 8</c> and the entity is
/// only drawn / collidable when that offset is in [0, 31] — matching
/// the original's $F222 SUB B / CP $1F / RET NC gate.</para>
/// </summary>
public sealed class EntityInstance
{
    public int TypeId;       // index into the EntityTypeTable
    public int X;            // pixel column (0..255) — recomputed per-frame from WorldX
    public int Y;            // pixel row (0..191) — fixed at level-load from TopAddr
    public int WorldX;       // record's +1 byte: world byte position (0..255)
    public int Frame;        // 0..MaxFrames-1
    public int DX, DY;       // signed per-frame velocity
    public int FrameTick;    // counts up; advance frame when > threshold
    public int AgeFrames;    // ticks since spawn — drives lifetimes / state
    public int Hp;           // hits to destroy (1 for fragile decor)
    public int MaxFrames;    // cached from EntityType for the AI tick path
    public bool Alive;       // true if slot in use
    public bool Visible;     // true if WorldX is within the current scroll window
}

/// <summary>The bullet/laser-beam list — port of the 4-slot table at
/// <c>$E46B</c> (4 bytes × 4 slots).  Each laser is a horizontal beam
/// up to <see cref="MaxLength"/> bytes (= 120 pixels) wide.  The HEAD
/// (far end from ship) is anchored at fire time; per-frame the TAIL
/// (ship-side end) recedes outward toward the head (matching $DEF0).
/// </summary>
public sealed class Bullet
{
    public const int MaxLength = 15;

    /// <summary>Fire-time anchor X (= ship's exit-side edge).  The
    /// head end is computed as X + (Span-1)*8*dir.</summary>
    public int X, Y, DX, DY;
    public bool Alive;
    /// <summary>Bitmap byte the beam paints (e.g. $EF = 7 lit pixels).</summary>
    public byte Pattern;
    /// <summary>Bytes painted at fire time (≤ 15) — the beam
    /// self-limits at scenery ($DEDA) and at the screen edge.</summary>
    public int Span;
    /// <summary>Remaining beam length in bytes (0..Span).  Each frame
    /// the tail recedes one byte toward the fixed head ($DEF0); the
    /// beam expires when Length reaches 0.</summary>
    public int Length;
    /// <summary>Spectrum attribute byte (bright | ink) — randomized per shot.</summary>
    public byte Attr;
}
