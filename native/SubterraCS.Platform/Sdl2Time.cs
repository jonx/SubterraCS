namespace SubterraCS.Platform;

/// <summary>
/// Thin public façade over SDL_GetTicks / SDL_Delay so the game-side
/// runner can pace its loop without taking a direct dependency on the
/// internal P/Invokes.
/// </summary>
public static class Sdl2Time
{
    public static uint GetTicks() => Sdl2.SDL_GetTicks();
    public static void Delay(uint ms) => Sdl2.SDL_Delay(ms);
}
