using System.Runtime.InteropServices;
using SubterraCS.Core;

namespace SubterraCS.Platform;

/// <summary>
/// SDL2 audio backend that streams 16-bit mono PCM produced on the fly
/// by a <see cref="BeeperSynth"/>.  Uses the legacy
/// <c>SDL_OpenAudio</c> + callback path — keeps the runtime light and
/// gives us tight control over the buffer fill, which matters for the
/// "Follin slide" timbre tricks.
/// </summary>
public sealed class Sdl2BeeperAudio : IDisposable
{
    private readonly BeeperSynth _synth;
    private readonly Sdl2AudioCallback _callback;
    private GCHandle _callbackHandle;

    public int SampleRate { get; }

    public Sdl2BeeperAudio(BeeperSynth synth, int sampleRate = 22050)
    {
        _synth = synth;
        SampleRate = sampleRate;

        Sdl2.SDL_InitSubSystem(Sdl2.InitAudio);

        _callback = AudioCallback;
        _callbackHandle = GCHandle.Alloc(_callback);

        var desired = new Sdl2.AudioSpec
        {
            Freq = sampleRate,
            Format = Sdl2.AudioS16Sys,
            Channels = 1,
            Samples = 1024,
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        if (Sdl2.SDL_OpenAudio(ref desired, IntPtr.Zero) != 0)
        {
            throw new InvalidOperationException(
                $"SDL_OpenAudio failed: {Sdl2.GetError()}");
        }
        Sdl2.SDL_PauseAudio(0); // start playing immediately
    }

    private void AudioCallback(IntPtr _, IntPtr stream, int byteLen)
    {
        // 16-bit mono = 2 bytes per sample.
        int sampleCount = byteLen / 2;
        Span<short> buffer = sampleCount <= 1024
            ? stackalloc short[sampleCount]
            : new short[sampleCount];

        _synth.Render(buffer, SampleRate);

        unsafe
        {
            fixed (short* src = buffer)
            {
                Buffer.MemoryCopy((void*)src, (void*)stream, byteLen, byteLen);
            }
        }
    }

    public void Dispose()
    {
        Sdl2.SDL_CloseAudio();
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
    }
}

/// <summary>SDL_AudioCallback delegate signature.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void Sdl2AudioCallback(IntPtr userdata, IntPtr stream, int len);
