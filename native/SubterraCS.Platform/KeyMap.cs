namespace SubterraCS.Platform;

/// <summary>
/// User-configurable keyboard mapping for the native game.  Loaded
/// from <c>keymap.cfg</c> at the repo root; if the file doesn't exist
/// a commented template with the defaults is written there so the
/// user can edit it in place.  Format, one action per line:
///
///   action = key [, key ...]
///
/// Actions: up, down, horizontal, left, right, fire, shift.
/// Keys: single letters/digits (q, a, l, …) or the named keys
/// space, enter, up, down, left, right, tab, lshift, rshift.
///
/// System keys stay fixed (Esc quit, P pause, R reset, F11
/// fullscreen, 1–5 title-menu digits) so a broken config can't lock
/// the user out.
/// </summary>
public sealed class KeyMap
{
    public enum GameAction { Up, Down, Horizontal, Left, Right, Fire, Shift }

    private readonly Dictionary<int, List<GameAction>> _byKey = new();

    public IReadOnlyList<GameAction> ActionsFor(int sdlKeycode)
        => _byKey.TryGetValue(sdlKeycode, out var list) ? list : Array.Empty<GameAction>();

    private void Bind(int keycode, GameAction action)
    {
        if (!_byKey.TryGetValue(keycode, out var list))
            _byKey[keycode] = list = new List<GameAction>();
        if (!list.Contains(action)) list.Add(action);
    }

    private static readonly (GameAction Action, string Keys)[] Defaults =
    {
        (GameAction.Up,         "q, up"),
        (GameAction.Down,       "a, down"),
        (GameAction.Horizontal, "l"),
        (GameAction.Left,       "left"),
        (GameAction.Right,      "right"),
        (GameAction.Fire,       "enter, space"),
        (GameAction.Shift,      "lshift, rshift"),
    };

    public static KeyMap LoadOrCreate(string path)
    {
        var map = new KeyMap();
        if (!File.Exists(path))
        {
            try { File.WriteAllText(path, DefaultTemplate()); }
            catch { /* read-only checkout — run with defaults */ }
        }

        var assigned = new HashSet<GameAction>();
        if (File.Exists(path))
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!TryParseAction(line[..eq].Trim(), out var action)) continue;
                foreach (var keyName in line[(eq + 1)..].Split(','))
                {
                    if (TryParseKey(keyName.Trim(), out int keycode))
                    {
                        map.Bind(keycode, action);
                        assigned.Add(action);
                    }
                }
            }
        }
        // Any action the file leaves unbound falls back to its default
        // keys, so a partial config stays playable.
        foreach (var (action, keys) in Defaults)
        {
            if (assigned.Contains(action)) continue;
            foreach (var keyName in keys.Split(','))
            {
                if (TryParseKey(keyName.Trim(), out int keycode))
                    map.Bind(keycode, action);
            }
        }
        return map;
    }

    private static bool TryParseAction(string name, out GameAction action)
    {
        switch (name.ToLowerInvariant())
        {
            case "up":         action = GameAction.Up; return true;
            case "down":       action = GameAction.Down; return true;
            case "horizontal": action = GameAction.Horizontal; return true;
            case "left":       action = GameAction.Left; return true;
            case "right":      action = GameAction.Right; return true;
            case "fire":       action = GameAction.Fire; return true;
            case "shift":      action = GameAction.Shift; return true;
            default:           action = default; return false;
        }
    }

    /// <summary>Key name → SDL keycode.  Single chars map to their
    /// ASCII lowercase; named keys cover the SDL scancode-based range.</summary>
    private static bool TryParseKey(string name, out int keycode)
    {
        name = name.ToLowerInvariant();
        switch (name)
        {
            case "space":  keycode = Sdl2.KeySpace; return true;
            case "enter":  keycode = Sdl2.KeyReturn; return true;
            case "up":     keycode = Sdl2.KeyUp; return true;
            case "down":   keycode = Sdl2.KeyDown; return true;
            case "left":   keycode = Sdl2.KeyLeft; return true;
            case "right":  keycode = Sdl2.KeyRight; return true;
            case "lshift": keycode = Sdl2.KeyLShift; return true;
            case "rshift": keycode = Sdl2.KeyRShift; return true;
            case "tab":    keycode = 0x09; return true;
        }
        if (name.Length == 1 && name[0] is >= 'a' and <= 'z' or >= '0' and <= '9')
        {
            keycode = name[0];
            return true;
        }
        keycode = 0;
        return false;
    }

    private static string DefaultTemplate() =>
        """
        # SubterraCS key bindings.  One action per line:
        #   action = key [, key ...]
        # Keys: letters/digits (q, a, l, ...) or space, enter, up, down,
        # left, right, tab, lshift, rshift.
        # Fixed system keys (not remappable): Esc quit, P pause, R reset,
        # F11 fullscreen, 1-5 title-menu digits.

        up         = q, up
        down       = a, down
        horizontal = l
        left       = left
        right      = right
        fire       = enter, space
        shift      = lshift, rshift
        """ + Environment.NewLine;
}
