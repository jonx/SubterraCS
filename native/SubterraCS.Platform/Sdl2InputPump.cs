using SubterraCS.Core;

namespace SubterraCS.Platform;

/// <summary>
/// Drains SDL's event queue per frame, mapping keyboard events into
/// the engine's <see cref="GameInput"/> state.  Single-tap signals
/// (pause, reset, fullscreen toggle) come back as edge flags on
/// <see cref="PumpResult"/>.
/// </summary>
public readonly record struct PumpResult(
    bool Quit,
    bool TogglePause,
    bool ToggleFullscreen,
    bool Reset);

public sealed class Sdl2InputPump
{
    private readonly GameInput _input;
    public bool QuitRequested { get; private set; }

    public Sdl2InputPump(GameInput input) => _input = input;

    public PumpResult Poll()
    {
        bool quit = false, pause = false, fullscreen = false, reset = false;

        while (Sdl2.SDL_PollEvent(out var evt) != 0)
        {
            if (evt.Type == Sdl2.EventQuit) { quit = true; continue; }
            if (evt.Type != Sdl2.EventKeyDown && evt.Type != Sdl2.EventKeyUp) continue;

            bool down = evt.Type == Sdl2.EventKeyDown;
            bool first = down && evt.Key.Repeat == 0;
            int sym = evt.Key.Keysym.Sym;
            switch (sym)
            {
                case Sdl2.KeyEscape:    if (down) quit = true; break;
                case Sdl2.KeyP:         if (first) pause = true; break;
                case Sdl2.KeyR:         if (first) reset = true; break;
                case Sdl2.KeyF11:       if (first) fullscreen = true; break;

                // Movement — same as the Avalonia game: Q/A for up/down,
                // L for horizontal, Enter/Space for fire.
                case 0x71:              _input.Up = down; break;        // q
                case 0x61:              _input.Down = down; break;       // a
                case 0x6C:              _input.Horizontal = down; break; // l (scroll in current facing)
                case Sdl2.KeyReturn:    _input.Fire = down; break;
                case Sdl2.KeySpace:     _input.Fire = down; break;

                // Cursor-key alternate mapping for the same actions, for
                // anyone who prefers arrows.
                case Sdl2.KeyUp:        _input.Up = down; break;
                case Sdl2.KeyDown:      _input.Down = down; break;
                case Sdl2.KeyLeft:      _input.Left = down; _input.Horizontal = down; break;
                case Sdl2.KeyRight:     _input.Right = down; _input.Horizontal = down; break;

                // Port-only precision modifier — hold Shift to make
                // each direction key fire ONE step per press-edge
                // instead of accelerating while held.
                case Sdl2.KeyLShift:
                case Sdl2.KeyRShift:    _input.Shift = down; break;
            }
        }

        if (quit) QuitRequested = true;
        return new PumpResult(quit, pause, fullscreen, reset);
    }
}
