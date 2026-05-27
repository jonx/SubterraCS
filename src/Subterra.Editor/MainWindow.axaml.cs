using System;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Subterra.Assets;
using Subterra.Spectrum;

namespace Subterra.Editor;

public partial class MainWindow : Window
{
    private Z80Snapshot? _snapshot;
    private string _repoRoot = "";
    private WriteableBitmap? _sheetBitmap;
    private SpriteSheet? _currentSheet;
    private int _currentBase;
    private int _currentCellWidthBytes;
    private int _currentCellHeight;
    private int _currentColumns;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
            var snapPath = Path.Combine(_repoRoot, "original", "dumps", "SUBSTRYK.Z80");
            _snapshot = Z80SnapshotReader.Load(snapPath);
            SnapshotPath.Text = snapPath;
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Could not auto-load snapshot: {ex.Message}";
        }

        RefreshButton.Click += (_, __) => RefreshSheet();
        SaveButton.Click += (_, __) => SaveSheet();
        PresetTitleString.Click += (_, __) => SetPreset(0xF82B, 1, 8, 64);
        PresetTopRam.Click += (_, __) => SetPreset(0x6000, 2, 16, 64);
        PresetE000.Click += (_, __) => SetPreset(0xE000, 2, 16, 128);
        PresetUdg.Click += (_, __) => SetPreset(0xFF58, 1, 8, 21);

        AddressBox.KeyDown += (_, e) => { if (e.Key == Key.Return) RefreshSheet(); };
        WidthBox.ValueChanged += (_, __) => RefreshSheet();
        HeightBox.ValueChanged += (_, __) => RefreshSheet();
        CountBox.ValueChanged += (_, __) => RefreshSheet();
        ColumnsBox.ValueChanged += (_, __) => RefreshSheet();
        SheetImage.PointerMoved += OnSheetPointerMoved;

        RefreshSheet();
    }

    private void SetPreset(int addr, int w, int h, int count)
    {
        AddressBox.Text = addr.ToString("X4", CultureInfo.InvariantCulture);
        WidthBox.Value = w;
        HeightBox.Value = h;
        CountBox.Value = count;
        RefreshSheet();
    }

    private void RefreshSheet()
    {
        if (_snapshot is null) return;
        if (!int.TryParse(AddressBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int addr))
        {
            StatusBar.Text = $"Bad address '{AddressBox.Text}'.";
            return;
        }
        if (addr < 0x4000 || addr > 0xFFFF)
        {
            StatusBar.Text = "Address must be in $4000..$FFFF (snapshot RAM).";
            return;
        }
        int w = (int)(WidthBox.Value ?? 2);
        int h = (int)(HeightBox.Value ?? 16);
        int count = (int)(CountBox.Value ?? 64);
        int cols = (int)(ColumnsBox.Value ?? 8);
        if (addr + w * h * count > 0x10000)
        {
            count = Math.Max(1, (0x10000 - addr) / (w * h));
        }
        int bytesNeeded = w * h * count;

        var slice = new byte[bytesNeeded];
        Array.Copy(_snapshot.Ram48K, addr - 0x4000, slice, 0, bytesNeeded);
        _currentSheet = new SpriteSheet(slice, addr, w, h, count);
        _currentBase = addr;
        _currentCellWidthBytes = w;
        _currentCellHeight = h;
        _currentColumns = cols;

        var rendered = _currentSheet.RenderSheetRgba(
            cols,
            inkRgb: (0xE0, 0xE0, 0xFF),
            paperRgb: (0x10, 0x10, 0x20),
            gridRgb: (0x40, 0x40, 0x50));

        _sheetBitmap = new WriteableBitmap(
            new Avalonia.PixelSize(rendered.Width, rendered.Height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Opaque);
        using (var fb = _sheetBitmap.Lock())
        {
            unsafe
            {
                fixed (byte* src = rendered.Rgba)
                {
                    byte* dst = (byte*)fb.Address;
                    int rowBytes = rendered.Width * 4;
                    for (int y = 0; y < rendered.Height; y++)
                    {
                        Buffer.MemoryCopy(
                            src + y * rowBytes,
                            dst + y * fb.RowBytes,
                            fb.RowBytes,
                            rowBytes);
                    }
                }
            }
        }
        // Scale up for readability so individual pixels are visible.
        SheetImage.Width = rendered.Width * 3;
        SheetImage.Height = rendered.Height * 3;
        SheetImage.Source = _sheetBitmap;
        StatusBar.Text = string.Format(CultureInfo.InvariantCulture,
            "Sheet: {0} cells of {1}×{2} bytes from ${3:X4} ({4} bytes total)",
            count, w, h, addr, bytesNeeded);
    }

    private void OnSheetPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentSheet is null || _sheetBitmap is null) return;
        var p = e.GetPosition(SheetImage);
        double scaleX = SheetImage.Width / _sheetBitmap.Size.Width;
        double scaleY = SheetImage.Height / _sheetBitmap.Size.Height;
        int srcX = (int)(p.X / scaleX);
        int srcY = (int)(p.Y / scaleY);
        int cellW = _currentSheet.CellWidthPixels + 1;
        int cellH = _currentSheet.CellHeightPixels + 1;
        int gx = (srcX - 1) / cellW;
        int gy = (srcY - 1) / cellH;
        if (gx < 0 || gx >= _currentColumns) return;
        int idx = gy * _currentColumns + gx;
        if (idx < 0 || idx >= _currentSheet.CellCount) return;
        ushort cellAddr = _currentSheet.AddressOf(idx);
        HoverInfo.Text = $"cell {idx,3}   addr ${cellAddr:X4}   ({_currentSheet.CellWidthPixels}×{_currentSheet.CellHeightPixels} px)";

        var sb = new StringBuilder();
        int bpc = _currentSheet.BytesPerCell;
        var data = new byte[bpc];
        if (_snapshot is not null)
        {
            Array.Copy(_snapshot.Ram48K, cellAddr - 0x4000, data, 0, bpc);
        }
        for (int row = 0; row < _currentCellHeight; row++)
        {
            for (int b = 0; b < _currentCellWidthBytes; b++)
            {
                sb.Append(data[row * _currentCellWidthBytes + b].ToString("X2", CultureInfo.InvariantCulture));
                sb.Append(' ');
            }
            sb.Append('\n');
        }
        HoverBytes.Text = sb.ToString();
    }

    private void SaveSheet()
    {
        if (_currentSheet is null) return;
        var rendered = _currentSheet.RenderSheetRgba(
            _currentColumns,
            inkRgb: (0xFF, 0xFF, 0xFF),
            paperRgb: (0x00, 0x00, 0x00),
            gridRgb: (0x30, 0x30, 0x30));
        var descriptor = string.Format(CultureInfo.InvariantCulture,
            "sprites-${0:X4}-{1}x{2}-n{3}",
            _currentBase,
            _currentCellWidthBytes * 8,
            _currentCellHeight,
            _currentSheet.CellCount);
        var outPath = RenderTarget.ForPng(_repoRoot, descriptor);
        PngWriter.WriteRgba(outPath, rendered.Rgba, rendered.Width, rendered.Height);
        StatusBar.Text = $"Saved {outPath}";
    }
}
