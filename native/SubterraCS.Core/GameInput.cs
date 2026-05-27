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
    public bool Horizontal;  // strafe (L / Left or Right)
    public bool Fire;        // shoot (Enter / Space)
}
