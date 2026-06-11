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
    private readonly KeyMap _keyMap;
    public bool QuitRequested { get; private set; }

    public Sdl2InputPump(GameInput input, KeyMap? keyMap = null)
    {
        _input = input;
        // keymap.cfg lives at the repo root (next to assets/) so it's
        // easy to find and survives rebuilds; created with a commented
        // default template on first run.
        _keyMap = keyMap ?? KeyMap.LoadOrCreate(
            Path.Combine(RenderTarget.FindRepoRoot(AppContext.BaseDirectory), "keymap.cfg"));
    }

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

            // Fixed system keys — not remappable so a broken keymap.cfg
            // can't lock the user out.
            switch (sym)
            {
                case Sdl2.KeyEscape:    if (down) quit = true; break;
                case Sdl2.KeyP:         if (first) pause = true; break;
                case Sdl2.KeyR:         if (first) reset = true; break;
                case Sdl2.KeyF11:       if (first) fullscreen = true; break;

                // Title-menu digits 1..5 — the cassette's control-
                // scheme selection keys (see docs/disasm/title-menu.md).
                case >= 0x31 and <= 0x35:
                    _input.MenuDigit = down ? sym - 0x30 : 0;
                    break;
            }

            // Game actions via the user-editable keymap (keymap.cfg —
            // see KeyMap.cs).  Defaults match the original layout:
            // Q/A up/down, L horizontal, arrows, Enter/Space fire,
            // Shift = port-only precision modifier.
            foreach (var action in _keyMap.ActionsFor(sym))
            {
                switch (action)
                {
                    case KeyMap.GameAction.Up:         _input.Up = down; break;
                    case KeyMap.GameAction.Down:       _input.Down = down; break;
                    case KeyMap.GameAction.Horizontal: _input.Horizontal = down; break;
                    case KeyMap.GameAction.Left:       _input.Left = down; _input.Horizontal = down; break;
                    case KeyMap.GameAction.Right:      _input.Right = down; _input.Horizontal = down; break;
                    case KeyMap.GameAction.Fire:       _input.Fire = down; break;
                    case KeyMap.GameAction.Shift:      _input.Shift = down; break;
                }
            }
        }

        if (quit) QuitRequested = true;
        return new PumpResult(quit, pause, fullscreen, reset);
    }
}
