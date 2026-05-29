using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Subterra.Spectrum;

namespace Subterra.Game;

public partial class MainWindow : Window
{
    private readonly Spectrum48? _machine;
    private readonly WriteableBitmap _bitmap;
    private readonly DispatcherTimer _timer;
    private readonly Sdl2Audio? _audio;
    private long _lastPcmCycle;
    private int _framesRun;

    public MainWindow()
    {
        InitializeComponent();

        _bitmap = new WriteableBitmap(
            new Avalonia.PixelSize(SpectrumScreen.Width, SpectrumScreen.Height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Opaque);
        Screen.Source = _bitmap;

        try
        {
            _machine = LoadMachine();
            // Open SDL2 audio device for live beeper playback.  Best-
            // effort — if SDL2 isn't installed or the device fails to
            // open we keep running silently.  See Sdl2Audio.cs +
            // Subterra.Spectrum.BeeperRecorder for the design.
            try
            {
                _audio = new Sdl2Audio(sampleRate: 44100);
                if (!_audio.Ready) _audio = null;
            }
            catch (DllNotFoundException)
            {
                // SDL2 shared lib not installed — emulator still runs.
                _audio = null;
            }
            BlitFrame();
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20),
                DispatcherPriority.Background,
                OnTick);
            _timer.Start();
        }
        catch (Exception ex)
        {
            _timer = new DispatcherTimer();
            StatusBar.Text = $"Failed to load: {ex.Message}";
        }

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Focusable = true;
    }

    private static Spectrum48 LoadMachine()
    {
        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(repoRoot, "original", "rom", "48k.rom");
        var snapPath = Path.Combine(repoRoot, "original", "dumps", "SUBSTRYK.Z80");
        var rom = File.ReadAllBytes(romPath);
        var snap = Z80SnapshotReader.Load(snapPath);
        var sys = new Spectrum48(rom);
        sys.LoadSnapshot(snap);
        return sys;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_machine is null) return;
        long startCycle = _machine.Cpu.Cycles;
        _machine.RunFrame();
        _framesRun++;
        BlitFrame();

        // Drain this frame's beeper edges into PCM, push to SDL2.
        // The cassette's $5E88 Follin player + every $F8B4/$F8D8/$F8F9/
        // $F90E/$F93A/$F974/$F99F SFX entry runs inside RunFrame() and
        // writes its OUT $FE,A; we capture every transition with its
        // CPU cycle stamp and resample to 44.1 kHz mono 16-bit PCM.
        if (_audio is not null)
        {
            // Catch up from any previously-rendered cycle to now.
            long from = Math.Max(_lastPcmCycle, startCycle);
            long to = _machine.Cpu.Cycles;
            if (to > from)
            {
                var pcm = _machine.Beeper.RenderPcm(from, to, _audio.SampleRate);
                // Keep the device queue from running ahead by more than
                // ~200 ms — if we're behind, skip queueing this frame
                // (acceptable: 50 Hz frame = 20 ms of audio).
                uint queuedBytes = _audio.QueuedBytes;
                int safeBytesPerSec = _audio.SampleRate * sizeof(short);
                if (queuedBytes < safeBytesPerSec / 5)   // ≤ 200 ms backlog
                {
                    _audio.Queue(pcm);
                }
                _lastPcmCycle = to;
            }
            // Trim the edge log behind us so it doesn't grow forever.
            _machine.Beeper.Trim(Math.Max(0, _machine.Cpu.Cycles - 2 * BeeperRecorder.CpuFrequencyHz));
        }

        StatusBar.Text = $"frame {_framesRun}   PC=${_machine.Cpu.PC:X4}   cycles={_machine.Cpu.Cycles}   audio={(_audio?.Ready == true ? "on" : "off")}";
    }

    private void BlitFrame()
    {
        if (_machine is null) return;
        var rgba = SpectrumScreen.DecodeRgba(_machine.RamView().Slice(0, SpectrumScreen.ScrBytes));
        using var fb = _bitmap.Lock();
        unsafe
        {
            fixed (byte* src = rgba)
            {
                byte* dst = (byte*)fb.Address;
                int rowBytes = SpectrumScreen.Width * 4;
                for (int y = 0; y < SpectrumScreen.Height; y++)
                {
                    Buffer.MemoryCopy(
                        src + y * rowBytes,
                        dst + y * fb.RowBytes,
                        fb.RowBytes,
                        rowBytes);
                }
            }
        }
        Screen.InvalidateVisual();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_machine is null) return;
        if (e.Key == Key.Escape) { Close(); return; }
        foreach (var key in MapKey(e.Key))
        {
            _machine.PressKey(key);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_machine is null) return;
        foreach (var key in MapKey(e.Key))
        {
            _machine.ReleaseKey(key);
        }
    }

    /// <summary>
    /// Map an Avalonia <see cref="Key"/> to one or more Spectrum keys.
    /// Cursor keys map to the canonical "Cursor type" joystick (CAPS+5/6/7/8),
    /// which is the layout the game's own menu offers.
    /// </summary>
    private static SpectrumKey[] MapKey(Key k) => k switch
    {
        Key.A => new[] { SpectrumKey.A },
        Key.B => new[] { SpectrumKey.B },
        Key.C => new[] { SpectrumKey.C },
        Key.D => new[] { SpectrumKey.D },
        Key.E => new[] { SpectrumKey.E },
        Key.F => new[] { SpectrumKey.F },
        Key.G => new[] { SpectrumKey.G },
        Key.H => new[] { SpectrumKey.H },
        Key.I => new[] { SpectrumKey.I },
        Key.J => new[] { SpectrumKey.J },
        Key.K => new[] { SpectrumKey.K },
        Key.L => new[] { SpectrumKey.L },
        Key.M => new[] { SpectrumKey.M },
        Key.N => new[] { SpectrumKey.N },
        Key.O => new[] { SpectrumKey.O },
        Key.P => new[] { SpectrumKey.P },
        Key.Q => new[] { SpectrumKey.Q },
        Key.R => new[] { SpectrumKey.R },
        Key.S => new[] { SpectrumKey.S },
        Key.T => new[] { SpectrumKey.T },
        Key.U => new[] { SpectrumKey.U },
        Key.V => new[] { SpectrumKey.V },
        Key.W => new[] { SpectrumKey.W },
        Key.X => new[] { SpectrumKey.X },
        Key.Y => new[] { SpectrumKey.Y },
        Key.Z => new[] { SpectrumKey.Z },
        Key.D0 or Key.NumPad0 => new[] { SpectrumKey.D0 },
        Key.D1 or Key.NumPad1 => new[] { SpectrumKey.D1 },
        Key.D2 or Key.NumPad2 => new[] { SpectrumKey.D2 },
        Key.D3 or Key.NumPad3 => new[] { SpectrumKey.D3 },
        Key.D4 or Key.NumPad4 => new[] { SpectrumKey.D4 },
        Key.D5 or Key.NumPad5 => new[] { SpectrumKey.D5 },
        Key.D6 or Key.NumPad6 => new[] { SpectrumKey.D6 },
        Key.D7 or Key.NumPad7 => new[] { SpectrumKey.D7 },
        Key.D8 or Key.NumPad8 => new[] { SpectrumKey.D8 },
        Key.D9 or Key.NumPad9 => new[] { SpectrumKey.D9 },
        Key.Space => new[] { SpectrumKey.Space },
        Key.Enter or Key.Return => new[] { SpectrumKey.Enter },
        Key.LeftShift or Key.RightShift => new[] { SpectrumKey.CapsShift },
        Key.LeftCtrl or Key.RightCtrl => new[] { SpectrumKey.SymbolShift },
        Key.Left  => new[] { SpectrumKey.CapsShift, SpectrumKey.D5 },
        Key.Down  => new[] { SpectrumKey.CapsShift, SpectrumKey.D6 },
        Key.Up    => new[] { SpectrumKey.CapsShift, SpectrumKey.D7 },
        Key.Right => new[] { SpectrumKey.CapsShift, SpectrumKey.D8 },
        _ => Array.Empty<SpectrumKey>(),
    };
}
