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
            // Prefer a post-game RAM dump if present — that has the in-game
            // tile bank and UDGs populated; the raw snapshot only shows the
            // boot-time state (PAUSE 0 on the title screen).
            var postGame = Path.Combine(_repoRoot, "build", "post-game.bin");
            if (File.Exists(postGame))
            {
                var ram = File.ReadAllBytes(postGame);
                if (ram.Length == 0xC000)
                {
                    _snapshot = new Z80Snapshot(default, ram, Z80SnapshotKind.V1);
                    SnapshotPath.Text = postGame + "  (post-game RAM dump)";
                }
            }
            if (_snapshot is null)
            {
                var snapPath = Path.Combine(_repoRoot, "original", "dumps", "SUBSTRYK.Z80");
                _snapshot = Z80SnapshotReader.Load(snapPath);
                SnapshotPath.Text = snapPath + "  (boot snapshot — run the game to get a richer dump)";
            }
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Could not auto-load snapshot: {ex.Message}";
        }

        RefreshButton.Click += (_, __) => RefreshSheet();
        SaveButton.Click += (_, __) => SaveSheet();
        PresetTileBank.Click    += (_, __) => SetPreset(0xB0F4, 1, 8, 384, cols: 32);
        PresetIngameUdg.Click   += (_, __) => SetPreset(0xE62B, 1, 8, 21, cols: 8);
        PresetMusic.Click       += (_, __) => SetPreset(0x6000, 2, 16, 64, cols: 8);
        PresetTitleString.Click += (_, __) => SetPreset(0xF82B, 1, 8, 64, cols: 16);
        PresetE000.Click        += (_, __) => SetPreset(0xE000, 2, 16, 128, cols: 8);

        AddressBox.KeyDown += (_, e) => { if (e.Key == Key.Return) RefreshSheet(); };
        WidthBox.ValueChanged += (_, __) => RefreshSheet();
        HeightBox.ValueChanged += (_, __) => RefreshSheet();
        CountBox.ValueChanged += (_, __) => RefreshSheet();
        ColumnsBox.ValueChanged += (_, __) => RefreshSheet();
        SheetImage.PointerMoved += OnSheetPointerMoved;

        // Map tab — level-map viewer/editor over the decoded
        // level-minimaps.bin (16 rows × 256 cols of tile indices per
        // level) rendered through the tiles-b0f4.bin bank.
        LoadMapAssets();
        MapLevelBox.ValueChanged += (_, __) => RefreshMap();
        MapZoomBox.ValueChanged += (_, __) => RefreshMap();
        MapApplyButton.Click += (_, __) => ApplyMapCell();
        MapSaveButton.Click += (_, __) => SaveMap();
        MapImage.PointerPressed += OnMapPointerPressed;
        RefreshMap();

        RefreshSheet();
    }

    private void SetPreset(int addr, int w, int h, int count, int cols = -1)
    {
        AddressBox.Text = addr.ToString("X4", CultureInfo.InvariantCulture);
        WidthBox.Value = w;
        HeightBox.Value = h;
        CountBox.Value = count;
        if (cols > 0) ColumnsBox.Value = cols;
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
        string tileNote = _currentBase == 0xB0F4
            ? $"   (sprite tile #${idx:X2} = {idx})"
            : "";
        HoverInfo.Text = $"cell {idx,3}   addr ${cellAddr:X4}   ({_currentSheet.CellWidthPixels}×{_currentSheet.CellHeightPixels} px){tileNote}";

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

    // ─── Map tab — level-map viewer/editor ─────────────────────────
    //
    // Renders a level's 16×256 tile-index buffer (level-minimaps.bin,
    // see docs/disasm/assets.md) through the master tile bank
    // (tiles-b0f4.bin) at 2048×128 px, ×2 scaled.  Click selects a
    // cell; "Apply to cell" writes the chosen tile index into the
    // in-memory buffer; "Save map" writes the whole file back.
    private const int MapRows = 16, MapCols = 256, MapBufSize = MapRows * MapCols;
    private int MapScale => (int)(MapZoomBox.Value ?? 2);
    private byte[] _mapBuffers = Array.Empty<byte>();   // all 6 × 4096
    private byte[] _mapTiles = Array.Empty<byte>();     // tile bank
    private string _mapPath = "";
    private WriteableBitmap? _mapBitmap;
    private int _mapSelRow = -1, _mapSelCol = -1;

    private void LoadMapAssets()
    {
        try
        {
            _mapPath = Path.Combine(_repoRoot, "assets", "extracted", "level-minimaps.bin");
            var tilesPath = Path.Combine(_repoRoot, "assets", "extracted", "tiles-b0f4.bin");
            if (File.Exists(_mapPath)) _mapBuffers = File.ReadAllBytes(_mapPath);
            if (File.Exists(tilesPath)) _mapTiles = File.ReadAllBytes(tilesPath);
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Map assets unavailable: {ex.Message}";
        }
    }

    private int MapLevel => (int)(MapLevelBox.Value ?? 1);

    private void RefreshMap()
    {
        if (_mapBuffers.Length < (MapLevel + 1) * MapBufSize || _mapTiles.Length == 0)
        {
            MapCellInfo.Text = "level-minimaps.bin / tiles-b0f4.bin missing — run extract-all";
            return;
        }
        int baseOff = MapLevel * MapBufSize;
        int w = MapCols * 8, h = MapRows * 8;
        var rgba = new byte[w * h * 4];
        for (int row = 0; row < MapRows; row++)
        {
            for (int col = 0; col < MapCols; col++)
            {
                byte tile = _mapBuffers[baseOff + row * MapCols + col];
                bool selected = row == _mapSelRow && col == _mapSelCol;
                for (int sl = 0; sl < 8; sl++)
                {
                    byte bits = tile != 0 && tile * 8 + sl < _mapTiles.Length
                        ? _mapTiles[tile * 8 + sl] : (byte)0;
                    int y = row * 8 + sl;
                    for (int px = 0; px < 8; px++)
                    {
                        int x = col * 8 + px;
                        int o = (y * w + x) * 4;
                        bool ink = (bits & (0x80 >> px)) != 0;
                        // Selected cell tinted red; ink pure white, paper dark.
                        (byte r, byte g, byte b) = ink
                            ? (selected ? ((byte)0xFF, (byte)0x60, (byte)0x60) : ((byte)0xFF, (byte)0xFF, (byte)0xFF))
                            : (selected ? ((byte)0x50, (byte)0x10, (byte)0x10) : ((byte)0x10, (byte)0x10, (byte)0x20));
                        rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 0xFF;
                    }
                }
            }
        }

        _mapBitmap = new WriteableBitmap(
            new Avalonia.PixelSize(w, h),
            new Avalonia.Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Opaque);
        using (var fb = _mapBitmap.Lock())
        {
            unsafe
            {
                fixed (byte* src = rgba)
                {
                    byte* dst = (byte*)fb.Address;
                    int rowBytes = w * 4;
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy(src + y * rowBytes, dst + y * fb.RowBytes, fb.RowBytes, rowBytes);
                }
            }
        }
        MapImage.Width = w * MapScale;
        MapImage.Height = h * MapScale;
        MapImage.Source = _mapBitmap;
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mapBuffers.Length == 0) return;
        var p = e.GetPosition(MapImage);
        int col = (int)(p.X / (8 * MapScale));
        int row = (int)(p.Y / (8 * MapScale));
        if (col < 0 || col >= MapCols || row < 0 || row >= MapRows) return;
        _mapSelRow = row; _mapSelCol = col;
        byte tile = _mapBuffers[MapLevel * MapBufSize + row * MapCols + col];
        MapTileBox.Value = tile;
        MapCellInfo.Text = $"L{MapLevel} row {row} col {col}   tile ${tile:X2} ({tile})";
        RefreshMap();
    }

    private void ApplyMapCell()
    {
        if (_mapSelRow < 0 || _mapBuffers.Length == 0) return;
        byte tile = (byte)(MapTileBox.Value ?? 0);
        _mapBuffers[MapLevel * MapBufSize + _mapSelRow * MapCols + _mapSelCol] = tile;
        MapCellInfo.Text = $"L{MapLevel} row {_mapSelRow} col {_mapSelCol}   tile ${tile:X2} ({tile})   (unsaved)";
        RefreshMap();
    }

    private void SaveMap()
    {
        if (_mapBuffers.Length == 0 || _mapPath.Length == 0) return;
        File.WriteAllBytes(_mapPath, _mapBuffers);
        StatusBar.Text = $"Saved {_mapPath}";
        MapCellInfo.Text = MapCellInfo.Text.Replace("   (unsaved)", "");
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
