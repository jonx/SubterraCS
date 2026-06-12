using SubterraCS.Core;
using SubterraCS.Platform;

namespace SubterraCS.Game;

/// <summary>
/// The interactive runner: opens an SDL2 window, polls keyboard, runs
/// the game loop at 50 Hz, presents the framebuffer.  Drives the
/// <see cref="BeeperSynth"/> via two paths:
///   1) <see cref="SfxQueue"/> — discrete game events (fire, hit, …).
///   2) <see cref="MusicPlayer"/> — background Follin music stream.
/// </summary>
internal static class Sdl2Runner
{
    public static byte[] MusicData { get; set; } = Array.Empty<byte>();

    /// <summary>Captured cassette SFX (optional) — see SfxWavBank.
    /// When an effect exists here it plays instead of the synth tone.</summary>
    public static SfxWavBank SfxBank { get; set; } = new();

    private static bool _titleTuneWasPlaying;

    /// <summary>Lost Sounds mode (N key, default OFF = faithful):
    /// when ON, events whose cassette sounds were queued but never
    /// played (the vestigial $F8xx messages — sound.md, CURIOSITIES.md)
    /// play their lost-*.wav reconstructions.</summary>
    private static bool _lostSounds;

    // ─── In-game key-remap screen (K) ───────────────────────────────
    private static bool _remapOpen;
    private static int _remapCursor;
    private static bool _remapCapturing;
    private static int _remapFrame;

    private static readonly KeyMap.GameAction[] RemapActions =
        Enum.GetValues<KeyMap.GameAction>();

    private static readonly string[] RemapLabels =
        { "THRUST UP", "THRUST DOWN", "MOVE", "FACE LEFT", "FACE RIGHT", "FIRE", "PRECISION" };

    /// <summary>Remap-screen input: fixed physical keys (arrows to
    /// navigate, Enter to rebind, Esc or K to save and close); while
    /// capturing, the first bindable keypress replaces the selected
    /// action's bindings.</summary>
    private static void TickRemap(Sdl2InputPump pump, GameInput input, int rawKey)
    {
        _remapFrame++;
        if (rawKey == 0) return;

        if (_remapCapturing)
        {
            if (rawKey == KeyMap.KeyEscape) { _remapCapturing = false; return; }
            if (!KeyMap.IsBindable(rawKey)) return;   // ignore F-keys etc.
            pump.Map.SetSingleBinding(RemapActions[_remapCursor], rawKey);
            _remapCapturing = false;
            return;
        }

        switch (rawKey)
        {
            case KeyMap.KeyUp:
                _remapCursor = (_remapCursor + RemapActions.Length - 1) % RemapActions.Length;
                break;
            case KeyMap.KeyDown:
                _remapCursor = (_remapCursor + 1) % RemapActions.Length;
                break;
            case KeyMap.KeyReturn:
            case KeyMap.KeySpace:
                _remapCapturing = true;
                break;
            case KeyMap.KeyEscape:
            case 0x6B:   // K closes too
                pump.Map.Save(pump.KeyMapPath);
                Console.WriteLine($"  key bindings saved to {pump.KeyMapPath}");
                _remapOpen = false;
                pump.SystemKeysEnabled = true;
                // Drop any held-flag state so a key held when the
                // screen opened doesn't stay stuck on.
                input.Up = input.Down = input.Horizontal = false;
                input.Left = input.Right = input.Fire = input.Shift = false;
                input.MenuDigit = 0;
                break;
        }
    }

    /// <summary>Overlay the remap UI on the last game frame.</summary>
    private static void DrawRemap(Framebuffer fb, KeyMap map)
    {
        // Dim the playfield: clear a centered panel area.
        for (int y = 8; y < 184; y++)
            for (int col = 1; col < 31; col++)
                fb.Bitmap[Framebuffer.BitmapAddress(col * 8, y)] = 0;
        for (int i = 0; i < fb.Attributes.Length; i++) fb.Attributes[i] = 0x07;

        MiniFont.DrawCentered(fb, 16, "KEY BINDINGS", 0x46);
        for (int i = 0; i < RemapActions.Length; i++)
        {
            var keys = string.Join(" ", map.KeysFor(RemapActions[i]).Select(KeyMap.KeyName));
            bool sel = i == _remapCursor;
            string line = $"{RemapLabels[i],-12} {keys}";
            byte attr = sel
                ? (_remapCapturing && (_remapFrame & 16) < 8 ? (byte)0x68 : (byte)0x45)
                : (byte)0x47;
            MiniFont.DrawCentered(fb, 40 + i * 12, sel && _remapCapturing ? $"{RemapLabels[i],-12} PRESS KEY" : line, attr);
        }
        MiniFont.DrawCentered(fb, 140, "ARROWS SELECT  ENTER REBIND", 0x44);
        MiniFont.DrawCentered(fb, 152, "ESC OR K SAVE : EXIT", 0x44);
    }

    public static int Run(World world)
    {
        const int FrameMs = 20; // 50 Hz

        using var window = new Sdl2Window(
            "SubterraCS — Subterranean Stryker (native)",
            Framebuffer.Width, Framebuffer.Height, scale: 3);

        var input = new GameInput();
        var pump = new Sdl2InputPump(input);

        BeeperSynth? synth = null;
        Sdl2BeeperAudio? audio = null;
        try
        {
            synth = new BeeperSynth();
            audio = new Sdl2BeeperAudio(synth);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  audio unavailable: {ex.Message} (continuing silently)");
        }

        var music = new MusicPlayer(MusicData, framesPerNote: 8);
        var fb = new Framebuffer();
        uint nextFrameTicks = Sdl2Time.GetTicks();
        bool paused = false;

        while (!pump.QuitRequested)
        {
            var ev = pump.Poll();
            if (ev.Quit) break;
            if (ev.TogglePause) paused = !paused;
            if (ev.ToggleFullscreen) window.ToggleFullscreen();
            if (ev.ToggleLostSounds)
            {
                _lostSounds = !_lostSounds;
                Console.WriteLine($"  Lost Sounds {(_lostSounds ? "ON — playing the reconstructed never-played $F8xx messages" : "OFF — faithful (those events are silent on the cassette)")}");
            }
            if (ev.Reset)
            {
                Console.WriteLine("  reset requested — press Esc to quit, restart for a new seed.");
            }

            // K — the in-game key-remap screen.  While open: the world
            // is frozen, the pump stops interpreting system keys and
            // game actions (we own the keyboard via RawKeyDown), and
            // the UI overlays the last-drawn frame.
            if (ev.ToggleRemap && !_remapOpen)
            {
                _remapOpen = true;
                _remapCursor = 0;
                _remapCapturing = false;
                pump.SystemKeysEnabled = false;
            }
            else if (_remapOpen)
            {
                TickRemap(pump, input, ev.RawKeyDown);
            }

            if (!paused && !_remapOpen)
            {
                world.Tick(input);

                // Forward game-event SFX.  Authentic captured WAVs
                // exist only for the cassette's REAL effects (the
                // direct OUT routines: hit click, bar-fill, spawn-in).
                // The $F8xx "message" SFX family turned out to be
                // vestigial — queued but never played by the original
                // (see sound.md §The message system is vestigial) —
                // so everything else uses the PORT-ONLY synth tones.
                if (synth != null)
                {
                    while (world.Sfx.TryDequeue(out var s))
                    {
                        string? wav = s switch
                        {
                            SfxKind.Hit or SfxKind.Damage => "hit",
                            // Lost Sounds mode: the reconstructed
                            // never-played $F8xx messages, mapped to
                            // the events the cassette queued them for.
                            SfxKind.BossAlert when _lostSounds => "lost-bossalert",
                            SfxKind.Pickup    when _lostSounds => "lost-pickup",
                            SfxKind.FuelLow   when _lostSounds => "lost-fuellow",
                            SfxKind.ShieldLow when _lostSounds => "lost-shieldlow",
                            SfxKind.Explode   when _lostSounds => "lost-shipkill",
                            SfxKind.GameOver  when _lostSounds => "lost-gameover",
                            SfxKind.LevelUp   when _lostSounds => $"lost-fanfare{Math.Clamp(world.Depth, 1, 5)}",
                            _                             => null,
                        };
                        if (wav is not null && SfxBank.TryGet(wav, out var pcm))
                        {
                            synth.PlayPcm(pcm);
                            continue;
                        }
                        var (hz, frames, slide) = SfxQueue.Voice(s);
                        if (hz > 0) synth.Tone(hz, frames, slide);
                    }
                    // Title music: the AUTHENTIC cassette tune (captured
                    // via run-emu -wav-from while holding M past the
                    // $F637 gate), looped while the title is showing.
                    // The cassette has NO in-game music — the Follin
                    // player only ticks in the title loop ($F64E/$F65D);
                    // the in-game MusicPlayer below is a port-only
                    // embellishment (see sound.md).
                    if (world.State is GameState.Title or GameState.Splash or GameState.HallOfFame)
                    {
                        if (!synth.PcmActive && SfxBank.TryGet("titletune", out var tune))
                            synth.PlayPcm(tune);
                    }
                    else if (_titleTuneWasPlaying && world.State == GameState.Playing)
                    {
                        synth.StopPcm();   // leaving title mid-tune
                    }
                    _titleTuneWasPlaying = world.State is GameState.Title or GameState.Splash or GameState.HallOfFame;

                    // Background music ticks slower than SFX, only when
                    // no SFX is currently sounding (let SFX preempt).
                    if (world.State == GameState.Playing)
                    {
                        music.Tick(synth);
                    }
                }
            }
            world.Draw(fb);
            if (_remapOpen) DrawRemap(fb, pump.Map);
            var rgba = fb.ToRgba();
            window.Present(rgba);

            // Fixed-step 50 Hz pacing.
            nextFrameTicks += FrameMs;
            uint now = Sdl2Time.GetTicks();
            int wait = (int)(nextFrameTicks - now);
            if (wait > 0 && wait < 1000) Sdl2Time.Delay((uint)wait);
            else nextFrameTicks = now;
        }

        audio?.Dispose();
        return 0;
    }
}
