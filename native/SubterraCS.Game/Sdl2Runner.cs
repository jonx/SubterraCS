using SubterraCS.Core;
using SubterraCS.Platform;

namespace SubterraCS.Game;

/// <summary>
/// The interactive runner: opens an SDL2 window, polls keyboard, runs
/// the game loop at 50 Hz, presents the framebuffer.  Optional beeper
/// audio (driven by simple per-event sound effects).
/// </summary>
internal static class Sdl2Runner
{
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

        var fb = new Framebuffer();
        uint nextFrameTicks = Sdl2Time.GetTicks();
        bool paused = false;
        int prevScore = 0, prevDepth = 0;

        while (!pump.QuitRequested)
        {
            var ev = pump.Poll();
            if (ev.Quit) break;
            if (ev.TogglePause) paused = !paused;
            if (ev.ToggleFullscreen) window.ToggleFullscreen();
            if (ev.Reset)
            {
                // Easiest reset: throw the world away and start a fresh one
                // with a new seed.  Caller can rebind by exiting and re-launching.
                Console.WriteLine("  reset requested — press Esc to quit, restart for a new seed.");
            }

            if (!paused)
            {
                world.Tick(input);

                // Quick SFX hook-up: chirp on score increase, deep blip on
                // depth advance.  Keeps audio interesting without us having
                // to fully reverse-engineer the original sound effects.
                if (synth != null)
                {
                    if (world.Score > prevScore) synth.Tone(880.0, 4, slide: 8.0);
                    if (world.Depth > prevDepth) synth.Tone(220.0, 16, slide: -4.0);
                }
                prevScore = world.Score;
                prevDepth = world.Depth;
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
