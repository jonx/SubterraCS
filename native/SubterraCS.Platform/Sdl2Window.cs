using System.Runtime.InteropServices;
using SubterraCS.Core;

namespace SubterraCS.Platform;

/// <summary>
/// Window + GPU texture + integer-scale letterbox presenter.  Takes an
/// already-composed RGBA framebuffer per frame (256×192 RGBA8888) and
/// blits it to the window.
///
/// Modelled on Hota's <c>Sdl2Window</c>; this one is slimmer: no
/// upscaling filter (we rely on integer-scale nearest-neighbour),
/// no debug overlay (the game's HUD does that natively).
/// </summary>
public sealed class Sdl2Window : IDisposable
{
    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _texture;
    private readonly int _texWidth;
    private readonly int _texHeight;
    private bool _fullscreen;

    // The pinned ARGB scratch buffer we upload from each frame.
    private readonly uint[] _argb;
    private readonly GCHandle _argbHandle;

    public Sdl2Window(string title, int width, int height, int scale = 3)
    {
        _texWidth = width;
        _texHeight = height;
        _argb = new uint[width * height];
        _argbHandle = GCHandle.Alloc(_argb, GCHandleType.Pinned);

        if (Sdl2.SDL_Init(Sdl2.InitVideo | Sdl2.InitEvents) != 0)
        {
            throw new InvalidOperationException($"SDL_Init failed: {Sdl2.GetError()}");
        }

        _window = Sdl2.SDL_CreateWindow(
            title,
            x: 0x1FFF0000, y: 0x1FFF0000,
            w: width * scale, h: height * scale,
            Sdl2.WindowShown | Sdl2.WindowResizable);
        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SDL_CreateWindow failed: {Sdl2.GetError()}");
        }

        _renderer = Sdl2.SDL_CreateRenderer(
            _window, -1, Sdl2.RendererAccelerated | Sdl2.RendererVsync);
        if (_renderer == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SDL_CreateRenderer failed: {Sdl2.GetError()}");
        }

        _texture = Sdl2.SDL_CreateTexture(
            _renderer, Sdl2.PixelFormatArgb8888,
            Sdl2.TextureAccessStreaming, width, height);
        if (_texture == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SDL_CreateTexture failed: {Sdl2.GetError()}");
        }
    }

    public int Width  => _texWidth;
    public int Height => _texHeight;
    public bool IsFullscreen => _fullscreen;

    public void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        Sdl2.SDL_SetWindowFullscreen(_window,
            _fullscreen ? Sdl2.WindowFullscreenDesktop : 0u);
    }

    /// <summary>
    /// Compute the centered integer-scale destination rect for the
    /// current window size.
    /// </summary>
    private Sdl2.Rect ComputeDst()
    {
        Sdl2.SDL_GetRendererOutputSize(_renderer, out int winW, out int winH);
        int n = Math.Min(winW / _texWidth, winH / _texHeight);
        if (n < 1) n = 1;
        int dstW = _texWidth * n;
        int dstH = _texHeight * n;
        return new Sdl2.Rect
        {
            X = (winW - dstW) / 2,
            Y = (winH - dstH) / 2,
            W = dstW,
            H = dstH,
        };
    }

    /// <summary>
    /// Present an RGBA framebuffer (one byte each for R, G, B, A; row
    /// stride = Width × 4).  The bytes are packed into ARGB8888 for SDL.
    /// </summary>
    public void Present(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != _texWidth * _texHeight * 4)
        {
            throw new ArgumentException(
                $"rgba length {rgba.Length} != expected {_texWidth * _texHeight * 4}");
        }
        for (int i = 0; i < _argb.Length; i++)
        {
            byte r = rgba[i * 4 + 0];
            byte g = rgba[i * 4 + 1];
            byte b = rgba[i * 4 + 2];
            // A in ARGB8888 is the top byte; force opaque (255).
            _argb[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        Sdl2.SDL_UpdateTexture(_texture, IntPtr.Zero,
            _argbHandle.AddrOfPinnedObject(), _texWidth * 4);

        var dst = ComputeDst();
        Sdl2.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        Sdl2.SDL_RenderClear(_renderer);
        Sdl2.SDL_RenderCopyRect(_renderer, _texture, IntPtr.Zero, ref dst);
        Sdl2.SDL_RenderPresent(_renderer);
    }

    public void Dispose()
    {
        if (_argbHandle.IsAllocated) _argbHandle.Free();
        if (_texture  != IntPtr.Zero) Sdl2.SDL_DestroyTexture(_texture);
        if (_renderer != IntPtr.Zero) Sdl2.SDL_DestroyRenderer(_renderer);
        if (_window   != IntPtr.Zero) Sdl2.SDL_DestroyWindow(_window);
        Sdl2.SDL_Quit();
        _texture = _renderer = _window = IntPtr.Zero;
    }
}
