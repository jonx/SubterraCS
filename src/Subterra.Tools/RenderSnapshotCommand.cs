using Subterra.Spectrum;

namespace Subterra.Tools;

internal static class RenderSnapshotCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: render-snapshot <path/to/file.z80>");
            return 2;
        }
        var srcPath = args[0];
        var snap = Z80SnapshotReader.Load(srcPath);
        var screen = new byte[SpectrumScreen.ScrBytes];
        Array.Copy(snap.Ram48K, 0, screen, 0, SpectrumScreen.ScrBytes);
        var rgba = SpectrumScreen.DecodeRgba(screen);

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var descriptor = $"snapshot-{Path.GetFileNameWithoutExtension(srcPath).ToLowerInvariant()}";
        var outPath = RenderTarget.ForPng(repoRoot, descriptor);
        PngWriter.WriteRgba(outPath, rgba, SpectrumScreen.Width, SpectrumScreen.Height);
        Console.WriteLine(outPath);
        return 0;
    }
}
