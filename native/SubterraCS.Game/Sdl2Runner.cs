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
            if (ev.Reset)
            {
                Console.WriteLine("  reset requested — press Esc to quit, restart for a new seed.");
            }

            if (!paused)
            {
                world.Tick(input);

                // Forward game-event SFX — authentic captured cassette
                // WAVs when available (see SfxWavBank), synth fallback
                // otherwise.
                if (synth != null)
                {
                    while (world.Sfx.TryDequeue(out var s))
                    {
                        string? wav = s switch
                        {
                            SfxKind.Hit or SfxKind.Damage => "hit",
                            SfxKind.Pickup                => "pickup",
                            SfxKind.LevelUp               => $"fanfare{Math.Clamp(world.Depth, 1, 5)}",
                            SfxKind.Explode               => "bossalert",
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
