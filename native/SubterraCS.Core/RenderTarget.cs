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
        // Walk upward looking for a directory containing both the
        // original solution and the new native one — that's our root.
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SubterraneanStryker.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "README.md")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return Environment.CurrentDirectory;
    }
}
