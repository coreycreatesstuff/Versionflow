using System.Text.RegularExpressions;
using VersionFlow.Core.Models;

namespace VersionFlow.Conversion;

public static partial class OutputPathHelper
{
    public static string ResolveUniqueOutputPath(
        string sourcePath,
        AfterEffectsVersionInfo targetVersion,
        string? outputDirectory,
        string? template,
        ProjectFormatKind? formatOverride = null)
    {
        string sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Environment.CurrentDirectory;
        string targetDir = string.IsNullOrWhiteSpace(outputDirectory) ? sourceDir : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(targetDir);

        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string ext = formatOverride switch
        {
            ProjectFormatKind.AepxXml => ".aepx",
            ProjectFormatKind.AepBinary => ".aep",
            _ => Path.GetExtension(sourcePath)
        };
        if (string.IsNullOrEmpty(ext)) ext = ".aep";

        string effectiveTemplate = string.IsNullOrWhiteSpace(template) ? "{name}_AE{year}" : template;
        string renderedName = effectiveTemplate
            .Replace("{name}", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", targetVersion.ReleaseYear.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{major}", targetVersion.AppMajorVersion.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", targetVersion.FormatMajorVersion.ToString(), StringComparison.OrdinalIgnoreCase);

        // Sanitize invalid filename characters
        foreach (char c in Path.GetInvalidFileNameChars())
            renderedName = renderedName.Replace(c, '_');

        renderedName = renderedName.Trim();
        if (string.IsNullOrEmpty(renderedName))
            renderedName = $"{baseName}_AE{targetVersion.ReleaseYear}";

        string candidate = Path.Combine(targetDir, $"{renderedName}{ext}");
        if (!File.Exists(candidate) && !PathsEqual(candidate, sourcePath))
            return candidate;

        int suffix = 2;
        while (true)
        {
            string numbered = Path.Combine(targetDir, $"{renderedName} ({suffix}){ext}");
            if (!File.Exists(numbered) && !PathsEqual(numbered, sourcePath))
                return numbered;
            suffix++;
        }
    }

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public static string EnsureSafeDirectoryPath(string baseDir, string subName)
    {
        var combined = Path.GetFullPath(Path.Combine(baseDir, subName));
        var normalizedBase = Path.GetFullPath(baseDir);
        if (!combined.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Directory traversal detected.");
        return combined;
    }
}
