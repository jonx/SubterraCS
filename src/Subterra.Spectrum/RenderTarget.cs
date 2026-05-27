using System.Globalization;

namespace Subterra.Spectrum;

/// <summary>
/// Resolves where a render should be written and what filename it should
/// take. Every render in the project goes through this so we end up with
/// a timestamped audit trail under <c>renders/</c> — see
/// docs/RE-LOG.md and the project's renders folder for the rationale.
/// </summary>
public static class RenderTarget
{
    /// <summary>
    /// Build a path of the form
    /// <c>{root}/renders/{descriptor}_{yyyyMMdd-HHmmss}.png</c>, creating
    /// the directory if needed. <paramref name="descriptor"/> should be a
    /// short slug describing the render (no extension, no spaces).
    /// </summary>
    public static string ForPng(string repoRoot, string descriptor)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture);
        return ForExtension(repoRoot, descriptor, "png", stamp);
    }

    /// <summary>Build a timestamped output path with an arbitrary extension.</summary>
    public static string ForExtension(
        string repoRoot, string descriptor, string extension, string? stamp = null)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            throw new ArgumentException("Descriptor must be non-empty.",
                nameof(descriptor));
        }
        stamp ??= DateTime.Now.ToString("yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture);

        var dir = Path.Combine(repoRoot, "renders");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{descriptor}_{stamp}.{extension}");
    }

    /// <summary>
    /// Walk up from <paramref name="start"/> until we find a directory
    /// containing a file or directory named <paramref name="marker"/>.
    /// We use this to locate the repo root from inside the build output.
    /// </summary>
    public static string FindRepoRoot(string start, string marker = "SubterraneanStryker.slnx")
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, marker))
                || Directory.Exists(Path.Combine(dir.FullName, marker)))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not find marker '{marker}' walking up from '{start}'.");
    }
}
