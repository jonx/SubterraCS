namespace SubterraCS.Core;

/// <summary>
/// Frame-level player input — mutated by the platform layer, consumed
/// by <see cref="GameLoop"/>.  Continuous (held) keys are booleans; one-
/// shot signals (fire press) ride on top of the same booleans and the
/// game loop debounces.
/// </summary>
public sealed class GameInput
{
    public bool Up;          // climb (Q / Up arrow)
    public bool Down;        // dive  (A / Down arrow)
    public bool Horizontal;  // strafe (L)
    public bool Left;        // explicit face-left (Left arrow / O)
    public bool Right;       // explicit face-right (Right arrow / P)
    public bool Fire;        // shoot (Enter / Space)
    /// <summary>Port-only precision modifier (held Shift).  When set,
    /// each direction key advances ONE step per press-edge instead of
    /// the cassette's per-frame acceleration ramp — release + repress
    /// to step again.  Useful for pixel-precise navigation.  Not a
    /// cassette key (the original game treats Shift as part of the
    /// keyboard scheme's LEFT key-group; we override that for the
    /// port).</summary>
    public bool Shift;

    /// <summary>Menu digit currently held: 0 = none, 1..5 = the keys
    /// the cassette's title menu accepts to pick a control scheme
    /// from the $F741 table and start the game (see
    /// docs/disasm/title-menu.md).  In the port, scheme choice is
    /// cosmetic — host keys map directly to this struct — but the
    /// title screen honours 1..5 as start keys like the original.</summary>
    public int MenuDigit;
}
