// Minimal SDL2 P/Invoke layer for SubterraCS — modelled on the Hota
// project's Sdl2.cs (same author).  Just the surface area we need:
// window + streaming texture + keyboard polling + audio callback.

using System.Runtime.InteropServices;

namespace SubterraCS.Platform;

internal static class Sdl2
{
    private const string Lib = "SDL2";

    static Sdl2()
    {
        NativeLibrary.SetDllImportResolver(typeof(Sdl2).Assembly, Resolve);
    }

    // macOS strips DYLD_LIBRARY_PATH under SIP, so we have to probe Homebrew
    // locations ourselves.  Apple-Silicon Homebrew installs at
    // /opt/homebrew; Intel Homebrew at /usr/local.
    private static IntPtr Resolve(string name, System.Reflection.Assembly _, DllImportSearchPath? __)
    {
        string[]? candidates = name switch
        {
            "SDL2" => OperatingSystem.IsMacOS()
                ? new[]
                  {
                      "/opt/homebrew/lib/libSDL2.dylib",
                      "/opt/homebrew/lib/libSDL2-2.0.0.dylib",
                      "/usr/local/lib/libSDL2.dylib",
                      "/usr/local/lib/libSDL2-2.0.0.dylib",
                      "libSDL2.dylib",
                  }
                : OperatingSystem.IsLinux()
                ? new[] { "libSDL2-2.0.so.0", "libSDL2.so" }
                : new[] { "SDL2.dll" },
            _ => null,
        };
        if (candidates == null) return IntPtr.Zero;
        foreach (var path in candidates)
        {
            if (NativeLibrary.TryLoad(path, out var handle)) return handle;
        }
        return IntPtr.Zero;
    }

    // --- Init flags --------------------------------------------------

    public const uint InitVideo  = 0x00000020;
    public const uint InitAudio  = 0x00000010;
    public const uint InitEvents = 0x00004000;

    // --- Window flags ------------------------------------------------

    public const uint WindowShown             = 0x00000004;
    public const uint WindowResizable         = 0x00000020;
    public const uint WindowFullscreenDesktop = 0x00001001;

    // --- Renderer flags ----------------------------------------------

    public const uint RendererAccelerated = 0x00000002;
    public const uint RendererVsync       = 0x00000004;

    public const uint PixelFormatArgb8888 = 0x16362004;
    public const int  TextureAccessStreaming = 1;

    // --- Event types -------------------------------------------------

    public const uint EventQuit    = 0x100;
    public const uint EventWindow  = 0x200;
    public const uint EventKeyDown = 0x300;
    public const uint EventKeyUp   = 0x301;

    // --- Key codes (the ones we map) ---------------------------------

    public const int KeyEscape = 0x1B;
    public const int KeyReturn = 0x0D;
    public const int KeySpace  = 0x20;
    public const int KeyUp     = 0x40000052;
    public const int KeyDown   = 0x40000051;
    public const int KeyLeft   = 0x40000050;
    public const int KeyRight  = 0x4000004F;
    public const int KeyF11    = 0x40000044;
    public const int KeyR      = 0x72;
    public const int KeyP      = 0x70;
    public const int KeyLShift = 0x400000E1;     // SDLK_LSHIFT — port-only precision modifier
    public const int KeyRShift = 0x400000E5;     // SDLK_RSHIFT
    // Letter keys we forward 1:1 (a..z = 0x61..0x7A; digits 0x30..0x39).

    // --- Audio format (signed 16-bit) --------------------------------

    public const ushort AudioS16Sys = 0x8010;  // signed, native-endian

    // --- Structs -----------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct Keysym { public int Scancode; public int Sym; public ushort Mod; public uint Unused; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardEvent
    {
        public uint Type;
        public uint Timestamp;
        public uint WindowID;
        public byte State;
        public byte Repeat;
        public byte Padding2, Padding3;
        public Keysym Keysym;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowEvent
    {
        public uint Type;
        public uint Timestamp;
        public uint WindowID;
        public byte EventId;
        public byte Pad1, Pad2, Pad3;
        public int Data1, Data2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int X, Y, W, H; }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(0)] public KeyboardEvent Key;
        [FieldOffset(0)] public WindowEvent Window;
    }

    // SDL_AudioSpec — only the leading fields we touch; the callback
    // pointer is what makes the streaming work.
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioSpec
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

    // --- P/Invokes ---------------------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_Init(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_InitSubSystem(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_Quit();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern IntPtr SDL_CreateWindow(string title, int x, int y, int w, int h, uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyWindow(IntPtr window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyRenderer(IntPtr renderer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int w, int h);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyTexture(IntPtr texture);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_RenderClear(IntPtr renderer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_RenderCopy(IntPtr renderer, IntPtr texture, IntPtr srcRect, IntPtr dstRect);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_RenderCopy")]
    public static extern int SDL_RenderCopyRect(IntPtr renderer, IntPtr texture, IntPtr srcRect, ref Rect dstRect);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SetRenderDrawColor(IntPtr renderer, byte r, byte g, byte b, byte a);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_GetRendererOutputSize(IntPtr renderer, out int w, out int h);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_SetWindowFullscreen(IntPtr window, uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_RenderPresent(IntPtr renderer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_PollEvent(out Event evt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_Delay(uint ms);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetTicks();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SDL_GetError();

    public static string GetError()
        => Marshal.PtrToStringAnsi(SDL_GetError()) ?? "<unknown>";

    // Audio.  We use the legacy SDL_OpenAudio with a callback so the
    // game-side beeper synthesiser feeds samples on demand.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int SDL_OpenAudio(ref AudioSpec desired, IntPtr obtained);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_CloseAudio();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_PauseAudio(int pauseOn);
}
