using Subterra.Spectrum;

namespace Subterra.Assets;

/// <summary>
/// A grid of 1-bit sprite cells decoded from a chunk of Spectrum memory.
///
/// A sprite cell is a rectangular region of the bitmap, stored as
/// <see cref="WidthBytes"/> × <see cref="Height"/> bytes (one bit per
/// pixel, MSB on the left, like the Spectrum bitmap itself).  Cells are
/// laid out contiguously in memory: the next cell starts at the byte
/// immediately after the previous one.
/// </summary>
public sealed class SpriteSheet
{
    public int CellWidthBytes { get; }
    public int CellHeight { get; }
    public int CellCount { get; }
    public int BytesPerCell => CellWidthBytes * CellHeight;
    public int CellWidthPixels => CellWidthBytes * 8;
    public int CellHeightPixels => CellHeight;

    private readonly byte[] _data;
    private readonly int _baseAddress;

    public SpriteSheet(
        ReadOnlySpan<byte> source, int baseAddress,
        int cellWidthBytes, int cellHeight, int cellCount)
    {
        if (cellWidthBytes <= 0 || cellHeight <= 0 || cellCount <= 0)
        {
            throw new ArgumentException("Sprite dimensions must be positive.");
        }
        int needed = cellWidthBytes * cellHeight * cellCount;
        if (source.Length < needed)
        {
            throw new ArgumentException(
                $"Source has {source.Length} bytes, need {needed} for {cellCount} cells of {cellWidthBytes}×{cellHeight}.");
        }
        CellWidthBytes = cellWidthBytes;
        CellHeight = cellHeight;
        CellCount = cellCount;
        _baseAddress = baseAddress;
        _data = source[..needed].ToArray();
    }

    /// <summary>Spectrum address of the first byte of cell <paramref name="index"/>.</summary>
    public ushort AddressOf(int index)
        => (ushort)(_baseAddress + index * BytesPerCell);

    /// <summary>Render one cell as 1-bit RGBA (white pixels on transparent).</summary>
    public byte[] RenderCellRgba(int index, (byte r, byte g, byte b) inkRgb, (byte r, byte g, byte b) paperRgb, bool transparentPaper = false)
    {
        int w = CellWidthPixels;
        int h = CellHeightPixels;
        var output = new byte[w * h * 4];
        int o = 0;
        int cellStart = index * BytesPerCell;
        for (int y = 0; y < h; y++)
        {
            for (int xb = 0; xb < CellWidthBytes; xb++)
            {
                byte row = _data[cellStart + y * CellWidthBytes + xb];
                for (int bit = 7; bit >= 0; bit--)
                {
                    bool on = (row & (1 << bit)) != 0;
                    if (on)
                    {
                        output[o++] = inkRgb.r;
                        output[o++] = inkRgb.g;
                        output[o++] = inkRgb.b;
                        output[o++] = 0xFF;
                    }
                    else if (transparentPaper)
                    {
                        output[o++] = 0;
                        output[o++] = 0;
                        output[o++] = 0;
                        output[o++] = 0;
                    }
                    else
                    {
                        output[o++] = paperRgb.r;
                        output[o++] = paperRgb.g;
                        output[o++] = paperRgb.b;
                        output[o++] = 0xFF;
                    }
                }
            }
        }
        return output;
    }

    /// <summary>
    /// Render the whole sheet as a single image: cells laid out in a
    /// grid of <paramref name="columns"/> columns, with a 1-pixel gap
    /// between cells so they don't visually run together.
    /// </summary>
    public RenderedImage RenderSheetRgba(
        int columns,
        (byte r, byte g, byte b) inkRgb,
        (byte r, byte g, byte b) paperRgb,
        (byte r, byte g, byte b) gridRgb)
    {
        int rows = (CellCount + columns - 1) / columns;
        int cellW = CellWidthPixels;
        int cellH = CellHeightPixels;
        int gap = 1;
        int imgW = columns * (cellW + gap) + gap;
        int imgH = rows * (cellH + gap) + gap;
        var img = new byte[imgW * imgH * 4];

        // Background = grid colour.
        for (int i = 0; i < imgW * imgH; i++)
        {
            img[i * 4 + 0] = gridRgb.r;
            img[i * 4 + 1] = gridRgb.g;
            img[i * 4 + 2] = gridRgb.b;
            img[i * 4 + 3] = 0xFF;
        }

        for (int idx = 0; idx < CellCount; idx++)
        {
            int gx = idx % columns;
            int gy = idx / columns;
            int x0 = gx * (cellW + gap) + gap;
            int y0 = gy * (cellH + gap) + gap;
            var cell = RenderCellRgba(idx, inkRgb, paperRgb);
            for (int y = 0; y < cellH; y++)
            {
                Buffer.BlockCopy(cell, y * cellW * 4,
                    img, ((y0 + y) * imgW + x0) * 4, cellW * 4);
            }
        }
        return new RenderedImage(img, imgW, imgH);
    }
}

/// <summary>An RGBA image: byte buffer plus dimensions.</summary>
public readonly record struct RenderedImage(byte[] Rgba, int Width, int Height);
