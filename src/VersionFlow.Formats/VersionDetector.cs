using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;
using VersionFlow.Core.Registry;

namespace VersionFlow.Formats;

public sealed class VersionDetector : IVersionDetector
{
    public VersionDetectionResult Detect(ParsedProjectDocument document)
    {
        var evidence = new List<string>();
        var warnings = new List<string>();

        int? formatMajor = document.Summary.FormatMajorVersion;
        int? formatMinor = document.Summary.FormatMinorVersion;
        uint? svap = document.Summary.SvapWord;

        if (formatMajor is null)
        {
            warnings.Add("No 'head' format version marker was found in the project container.");
            return new VersionDetectionResult
            {
                FormatKind = document.FormatKind,
                SvapWord = svap,
                DisplayLabel = "Unknown After Effects version",
                Confidence = DetectionConfidence.Unknown,
                DetectionMethod = "Missing 'head' chunk",
                Evidence = evidence,
                Warnings = warnings
            };
        }

        evidence.Add($"Internal head format version: {formatMajor}.{formatMinor ?? 0}");
        if (svap.HasValue)
            evidence.Add($"Saving application build word (svap): 0x{svap.Value:X8}");

        var candidates = VersionRegistry.FindByFormatMajor(formatMajor.Value);
        if (candidates.Count == 0)
        {
            warnings.Add($"Internal format major version {formatMajor.Value} is not in VersionFlow's verified registry.");
            return new VersionDetectionResult
            {
                FormatKind = document.FormatKind,
                FormatMajorVersion = formatMajor,
                FormatMinorVersion = formatMinor,
                SvapWord = svap,
                DisplayLabel = $"Unknown release (Format {formatMajor.Value}.{formatMinor ?? 0})",
                Confidence = DetectionConfidence.Unknown,
                DetectionMethod = "head chunk format inspection",
                Evidence = evidence,
                Warnings = warnings
            };
        }

        if (candidates.Count == 1)
        {
            var exact = candidates[0];
            evidence.Add($"Unique format mapping: Format {formatMajor.Value} -> {exact.DisplayName}");
            return new VersionDetectionResult
            {
                FormatKind = document.FormatKind,
                FormatMajorVersion = formatMajor,
                FormatMinorVersion = formatMinor,
                SvapWord = svap,
                PrimaryMatch = exact,
                CandidateReleases = candidates,
                DisplayLabel = exact.ShortName,
                Confidence = DetectionConfidence.Exact,
                DetectionMethod = "head chunk internal format version",
                Evidence = evidence,
                Warnings = warnings
            };
        }

        // Format 93 maps to AE 2020 – AE 2022; report honest range rather than fabricating one release
        var newestYear = candidates.Max(c => c.ReleaseYear);
        var oldestYear = candidates.Min(c => c.ReleaseYear);
        var defaultMatch = candidates.FirstOrDefault(c => c.Id == "ae-2020") ?? candidates[0];
        evidence.Add($"Format {formatMajor.Value} is shared across {string.Join(", ", candidates.Select(c => c.ShortName))}");
        warnings.Add($"Internal format {formatMajor.Value} is shared by AE {oldestYear}–{newestYear}; exact minor release cannot be distinguished from format major alone.");

        return new VersionDetectionResult
        {
            FormatKind = document.FormatKind,
            FormatMajorVersion = formatMajor,
            FormatMinorVersion = formatMinor,
            SvapWord = svap,
            PrimaryMatch = defaultMatch,
            CandidateReleases = candidates,
            DisplayLabel = $"AE {oldestYear}–{newestYear} (Format {formatMajor.Value})",
            Confidence = DetectionConfidence.FormatRange,
            DetectionMethod = "head chunk format family mapping",
            Evidence = evidence,
            Warnings = warnings
        };
    }
}
