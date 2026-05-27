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

/// <summary>
/// Renders a 16×16 sprite stored as four 8-byte column-major quadrants
/// — the format used by the entity-type sprite banks at <c>$B8F4</c>
/// onwards in Subterranean Stryker.
///
/// The 32 source bytes are laid out as:
///   bytes  0– 7: top-left 8 rows × 1 byte
///   bytes  8–15: top-right 8 rows × 1 byte
///   bytes 16–23: bottom-left 8 rows × 1 byte
///   bytes 24–31: bottom-right 8 rows × 1 byte
/// </summary>
public static class QuadrantSpriteRenderer
{
    public const int BytesPerSprite = 32;
    public const int Width = 16;
    public const int Height = 16;

    /// <summary>
    /// Decode a single 32-byte sprite into a 16×16 RGBA buffer.
    /// </summary>
    public static byte[] RenderRgba(
        ReadOnlySpan<byte> sprite,
        (byte r, byte g, byte b) inkRgb,
        (byte r, byte g, byte b) paperRgb)
    {
        if (sprite.Length < BytesPerSprite)
        {
            throw new ArgumentException(
                $"Sprite must be at least {BytesPerSprite} bytes; got {sprite.Length}.");
        }
        var output = new byte[Width * Height * 4];
        for (int row = 0; row < Height; row++)
        {
            int half = row < 8 ? 0 : 16;
            int sub = row & 7;
            byte left = sprite[half + sub];
            byte right = sprite[half + 8 + sub];
            for (int col = 0; col < Width; col++)
            {
                byte src = col < 8 ? left : right;
                int bit = 7 - (col & 7);
                bool on = (src & (1 << bit)) != 0;
                int idx = (row * Width + col) * 4;
                var (r, g, b) = on ? inkRgb : paperRgb;
                output[idx + 0] = r;
                output[idx + 1] = g;
                output[idx + 2] = b;
                output[idx + 3] = 0xFF;
            }
        }
        return output;
    }

    /// <summary>
    /// Render all sprites in a buffer (assumed contiguous 32-byte
    /// frames) as a sheet of <paramref name="columns"/> columns.
    /// </summary>
    public static RenderedImage RenderBank(
        ReadOnlySpan<byte> bank,
        int columns,
        (byte r, byte g, byte b) inkRgb,
        (byte r, byte g, byte b) paperRgb,
        (byte r, byte g, byte b) gridRgb)
    {
        int frameCount = bank.Length / BytesPerSprite;
        int rows = (frameCount + columns - 1) / columns;
        int gap = 1;
        int imgW = columns * (Width + gap) + gap;
        int imgH = rows * (Height + gap) + gap;
        var img = new byte[imgW * imgH * 4];
        for (int i = 0; i < imgW * imgH; i++)
        {
            img[i * 4 + 0] = gridRgb.r;
            img[i * 4 + 1] = gridRgb.g;
            img[i * 4 + 2] = gridRgb.b;
            img[i * 4 + 3] = 0xFF;
        }
        for (int idx = 0; idx < frameCount; idx++)
        {
            int gx = idx % columns;
            int gy = idx / columns;
            int x0 = gx * (Width + gap) + gap;
            int y0 = gy * (Height + gap) + gap;
            var cell = RenderRgba(
                bank.Slice(idx * BytesPerSprite, BytesPerSprite),
                inkRgb,
                paperRgb);
            for (int y = 0; y < Height; y++)
            {
                Buffer.BlockCopy(cell, y * Width * 4,
                    img, ((y0 + y) * imgW + x0) * 4, Width * 4);
            }
        }
        return new RenderedImage(img, imgW, imgH);
    }
}

/// <summary>An RGBA image: byte buffer plus dimensions.</summary>
public readonly record struct RenderedImage(byte[] Rgba, int Width, int Height)
{
    /// <summary>
    /// Return a new image scaled up by an integer factor using nearest-neighbour
    /// (so pixels stay crisp — useful for chunky retro art).
    /// </summary>
    public RenderedImage UpscaleNearest(int factor)
    {
        if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor));
        if (factor == 1) return this;
        int newW = Width * factor;
        int newH = Height * factor;
        var output = new byte[newW * newH * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int sIdx = (y * Width + x) * 4;
                byte r = Rgba[sIdx + 0];
                byte g = Rgba[sIdx + 1];
                byte b = Rgba[sIdx + 2];
                byte a = Rgba[sIdx + 3];
                for (int dy = 0; dy < factor; dy++)
                {
                    int rowStart = ((y * factor + dy) * newW + x * factor) * 4;
                    for (int dx = 0; dx < factor; dx++)
                    {
                        output[rowStart + dx * 4 + 0] = r;
                        output[rowStart + dx * 4 + 1] = g;
                        output[rowStart + dx * 4 + 2] = b;
                        output[rowStart + dx * 4 + 3] = a;
                    }
                }
            }
        }
        return new RenderedImage(output, newW, newH);
    }
}
