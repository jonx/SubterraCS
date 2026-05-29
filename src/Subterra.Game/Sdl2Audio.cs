using System;
using System.Runtime.InteropServices;

namespace Subterra.Game;

/// <summary>
/// Minimal SDL2 audio output for the Avalonia emulator window.  We
/// only need the push-mode API (no callback): the emulator runs a
/// frame, renders that frame's worth of PCM from the
/// <c>Subterra.Spectrum.BeeperRecorder</c> at the audio sample rate,
/// and `SDL_QueueAudio` pushes it into the device's internal queue.
/// SDL's audio thread drains the queue at the device rate — no
/// thread-shared state in our own code.
///
/// All P/Invokes are inline here so this file is self-contained and
/// doesn't drag a platform-specific dep into the cross-platform
/// emulator core in <c>Subterra.Spectrum</c>.
/// </summary>
public sealed class Sdl2Audio : IDisposable
{
    private const string Lib = "SDL2";

    // SDL2 flags / formats
    private const uint InitAudio = 0x00000010;
    private const ushort AudioS16Sys = 0x8010;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioSpec
    {
        public int Freq;
        public ushort Format;
        public byte Channels;
        public byte Silence;
        public ushort Samples;
        public ushort Padding;
        public uint Size;
        public IntPtr Callback;
        public IntPtr UserData;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_InitSubSystem(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern uint SDL_OpenAudioDevice(IntPtr device, int iscapture, ref AudioSpec desired, IntPtr obtained, int allowedChanges);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_CloseAudioDevice(uint dev);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_QueueAudio(uint dev, IntPtr data, uint len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint SDL_GetQueuedAudioSize(uint dev);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_PauseAudioDevice(uint dev, int pauseOn);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();

    private uint _device;
    private readonly int _sampleRate;
    public int SampleRate => _sampleRate;
    public bool Ready => _device != 0;

    public Sdl2Audio(int sampleRate = 44100)
    {
        _sampleRate = sampleRate;
        if (SDL_InitSubSystem(InitAudio) != 0)
        {
            // Audio init failed — leave Ready false; caller can still
            // run the emulator without sound.
            return;
        }
        var desired = new AudioSpec
        {
            Freq = sampleRate,
            Format = AudioS16Sys,
            Channels = 1,
            Samples = 1024,
            Callback = IntPtr.Zero,   // push mode (use SDL_QueueAudio)
            UserData = IntPtr.Zero,
        };
        _device = SDL_OpenAudioDevice(IntPtr.Zero, 0, ref desired, IntPtr.Zero, 0);
        if (_device == 0)
        {
            var msg = Marshal.PtrToStringAnsi(SDL_GetError()) ?? "<unknown>";
            Console.Error.WriteLine($"[audio] SDL_OpenAudioDevice failed: {msg}");
            return;
        }
        SDL_PauseAudioDevice(_device, 0);   // start the device
    }

    /// <summary>Push <paramref name="samples"/> into the SDL audio
    /// device's internal queue.  Caller controls cadence — typically
    /// one frame's worth of PCM per emulator tick.</summary>
    public void Queue(ReadOnlySpan<short> samples)
    {
        if (_device == 0 || samples.Length == 0) return;
        unsafe
        {
            fixed (short* p = samples)
            {
                int byteLen = samples.Length * sizeof(short);
                if (SDL_QueueAudio(_device, (IntPtr)p, (uint)byteLen) != 0)
                {
                    var msg = Marshal.PtrToStringAnsi(SDL_GetError()) ?? "<unknown>";
                    Console.Error.WriteLine($"[audio] SDL_QueueAudio failed: {msg}");
                }
            }
        }
    }

    /// <summary>Bytes currently queued waiting to play.  Use it to
    /// decide whether to throttle queueing (avoid runaway buffer).</summary>
    public uint QueuedBytes => _device != 0 ? SDL_GetQueuedAudioSize(_device) : 0;

    public void Dispose()
    {
        if (_device != 0)
        {
            SDL_CloseAudioDevice(_device);
            _device = 0;
        }
    }
}
