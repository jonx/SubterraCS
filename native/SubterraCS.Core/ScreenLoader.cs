namespace SubterraCS.Core;

/// <summary>
/// Loads a raw Spectrum SCREEN$ (6912 bytes: 6144 bitmap + 768 attrs)
/// straight into a <see cref="Framebuffer"/>.  The original game's
/// loading splash at <c>$4000..$5AFF</c> ships in
/// <c>original/dumps/SCRSHOT/SUBSTRYK.SCR</c> as exactly this
/// layout — same bit/byte order the ULA reads, so we just copy it
/// across.
/// </summary>
public static class ScreenLoader
{
    public const int ScrSize = 6912;

    public static Framebuffer LoadIntoFramebuffer(byte[] scr)
    {
        if (scr is null || scr.Length < ScrSize)
        {
            throw new ArgumentException(
                $"SCREEN$ must be {ScrSize} bytes; got {scr?.Length ?? 0}.");
        }
        var fb = new Framebuffer();
        Buffer.BlockCopy(scr, 0, fb.Bitmap, 0, Framebuffer.BitmapBytes);
        Buffer.BlockCopy(scr, Framebuffer.BitmapBytes, fb.Attributes, 0, Framebuffer.AttributeBytes);
        return fb;
    }

    public static void OverwriteFramebuffer(Framebuffer fb, byte[] scr)
    {
        if (scr is null || scr.Length < ScrSize) return;
        Buffer.BlockCopy(scr, 0, fb.Bitmap, 0, Framebuffer.BitmapBytes);
        Buffer.BlockCopy(scr, Framebuffer.BitmapBytes, fb.Attributes, 0, Framebuffer.AttributeBytes);
    }
}
