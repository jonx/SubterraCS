using System.Globalization;

namespace SubterraCS.Core;

/// <summary>
/// Same convention as the original solution: every render goes into the
/// repository's <c>renders/</c> directory with a timestamp suffix so
/// nothing ever overwrites a previous frame.  Keeps the visual changelog
/// continuous across both solutions.
/// </summary>
public static class RenderTarget
{
    public static string ForPng(string repoRoot, string descriptor)
    {
        var stamp = DateTime.Now.ToString(
            "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var dir = Path.Combine(repoRoot, "renders");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{descriptor}_{stamp}.png");
    }

    public static string FindRepoRoot(string start)
    {
        // The repo root is the one and only directory holding both the
        // original solution and the asset bins.  Looking for the
        // SubterraneanStryker.slnx alone uniquely identifies it (the
        // native/ subdirectory has its own README.md so we can't use
        // that as a marker).
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SubterraneanStryker.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return Environment.CurrentDirectory;
    }
}
