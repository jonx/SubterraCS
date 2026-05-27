using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class RenderScrCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: render-scr <path/to/file.scr>");
            return 2;
        }
        var srcPath = args[0];
        var raw = File.ReadAllBytes(srcPath);
        if (raw.Length < SpectrumScreen.ScrBytes)
        {
            Console.Error.WriteLine(
                $"{srcPath}: expected at least {SpectrumScreen.ScrBytes} bytes, got {raw.Length}.");
            return 1;
        }

        var rgba = SpectrumScreen.DecodeRgba(raw);
        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var descriptor = $"scr-{Path.GetFileNameWithoutExtension(srcPath).ToLowerInvariant()}";
        var outPath = RenderTarget.ForPng(repoRoot, descriptor);
        PngWriter.WriteRgba(outPath, rgba, SpectrumScreen.Width, SpectrumScreen.Height);
        Console.WriteLine(outPath);
        return 0;
    }
}
