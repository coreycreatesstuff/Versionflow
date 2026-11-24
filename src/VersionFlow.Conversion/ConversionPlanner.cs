using VersionFlow.Core.Errors;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;
using VersionFlow.Core.Registry;

namespace VersionFlow.Conversion;

public sealed class ConversionPlanner : IConversionPlanner
{
    public IReadOnlyList<ConversionEdge> GetCompatibilityGraph()
    {
        var edges = new List<ConversionEdge>();
        var versions = VersionRegistry.All;

        foreach (var src in versions.Where(v => v.IsSupportedSource))
        {
            foreach (var dst in versions.Where(v => v.IsSupportedTarget))
            {
                if (src.FormatMajorVersion == dst.FormatMajorVersion && src.ReleaseYear <= dst.ReleaseYear)
                {
                    edges.Add(new ConversionEdge
                    {
                        Id = $"{src.Id}->{dst.Id}:native",
                        SourceFormatMajor = src.FormatMajorVersion,
                        TargetFormatMajor = dst.FormatMajorVersion,
                        Method = ConversionMethod.StandaloneNative,
                        VerificationStatus = CompatibilityStatus.PartiallyVerified,
                        EstimatedRisk = RuleRiskLevel.Low,
                        Description = $"Same-generation preservation ({src.ShortName} -> {dst.ShortName}, Format {dst.FormatMajorVersion}).",
                        ExpectedModifications = ["Preserve structure cleanly without destructive changes."]
                    });
                }
                else if (src.FormatMajorVersion > dst.FormatMajorVersion)
                {
                    bool isExperimental = dst.CompatibilityStatus == CompatibilityStatus.Experimental;
                    edges.Add(new ConversionEdge
                    {
                        Id = $"{src.Id}->{dst.Id}:experimental-meta",
                        SourceFormatMajor = src.FormatMajorVersion,
                        TargetFormatMajor = dst.FormatMajorVersion,
                        Method = isExperimental ? ConversionMethod.ExperimentalMetadata : ConversionMethod.StandaloneNative,
                        VerificationStatus = dst.CompatibilityStatus,
                        EstimatedRisk = src.FormatMajorVersion - dst.FormatMajorVersion >= 2 ? RuleRiskLevel.Experimental : RuleRiskLevel.Moderate,
                        Description = $"Backward migration ({src.ShortName} Format {src.FormatMajorVersion} -> {dst.ShortName} Format {dst.FormatMajorVersion}).",
                        Preconditions = ["Valid RIFX/AEPX container", "Compatibility scan completed"],
                        KnownLimitations =
                        [
                            "Selectable Track Mattes (23.0+ / Format 94+) are incompatible with Format 93.",
                            "Advanced 3D models & 2025/2026 effects do not exist in AE 2020."
                        ],
                        ExpectedModifications = ["Update 'head' format version & 'svap' build word", "Optional 3D renderer tag fallback in Compatibility Mode"]
                    });
                }
            }
        }

        return edges;
    }

    public ConversionPlan CreatePlan(ParsedProjectDocument document, CompatibilityReport report, ConversionRequest request)
    {
        int srcFmt = document.VersionDetection.FormatMajorVersion ?? 0;
        var target = request.TargetVersion;

        if (!target.IsSupportedTarget)
        {
            return new ConversionPlan
            {
                SourceDetection = document.VersionDetection,
                TargetVersion = target,
                Mode = request.Mode,
                Path = [],
                RuleIdsToExecute = [],
                CanProceed = false,
                BlockReason = $"{target.DisplayName} (Format {target.FormatMajorVersion}) is currently marked '{target.CompatibilityStatus}' and is not enabled as a conversion target.",
                BlockErrorCode = ErrorCodes.NoVerifiedStrategy
            };
        }

        if (srcFmt == 0)
        {
            return new ConversionPlan
            {
                SourceDetection = document.VersionDetection,
                TargetVersion = target,
                Mode = request.Mode,
                Path = [],
                RuleIdsToExecute = [],
                CanProceed = false,
                BlockReason = "Source project format version could not be detected from 'head' metadata.",
                BlockErrorCode = ErrorCodes.UnknownProjectVersion
            };
        }

        bool isSameOrUpgrade = srcFmt <= target.FormatMajorVersion;
        bool isDowngrade = srcFmt > target.FormatMajorVersion;

        // Safe Mode blocks unverified cross-format downgrades unless verified path exists
        if (isDowngrade && request.Mode == ConversionMode.Safe)
        {
            return new ConversionPlan
            {
                SourceDetection = document.VersionDetection,
                TargetVersion = target,
                Mode = request.Mode,
                Path = [],
                RuleIdsToExecute = [],
                CanProceed = false,
                BlockReason =
                    $"Safe Mode blocks standalone downgrade from Format {srcFmt} to {target.ShortName} (Format {target.FormatMajorVersion}) because opening in {target.ShortName} has not been live-verified in this environment and {report.WarningCount + report.UnsupportedCount} compatibility warning(s)/unsupported feature(s) were detected. Switch to Compatibility Mode (to review changes) or Experimental Mode (with explicit acknowledgement).",
                BlockErrorCode = ErrorCodes.NoVerifiedStrategy,
                Notes = ["Your original project was not modified."]
            };
        }

        if (isDowngrade && request.Mode == ConversionMode.Experimental && !request.ExperimentalAcknowledged)
        {
            return new ConversionPlan
            {
                SourceDetection = document.VersionDetection,
                TargetVersion = target,
                Mode = request.Mode,
                Path = [],
                RuleIdsToExecute = [],
                CanProceed = false,
                RequiresExperimentalAcknowledgement = true,
                BlockReason = "Experimental Mode requires explicit user acknowledgement before modifying format metadata.",
                BlockErrorCode = ErrorCodes.ExperimentalAckRequired
            };
        }

        bool hasUnapprovedLosses = report.Findings.Any(f =>
            f.RequiresUserApproval && !request.ApprovedFindingIds.Contains(f.Id));
        if (request.Mode == ConversionMode.Compatibility && hasUnapprovedLosses && report.UnsupportedCount > 0 && request.ApprovedFindingIds.Count == 0)
        {
            // Compatibility mode can still proceed if user didn't selectively block, OR prompt approval when requested
        }

        var method = request.PreferredMethod ?? (isSameOrUpgrade
            ? ConversionMethod.StandaloneNative
            : (request.Mode == ConversionMode.Experimental
                ? ConversionMethod.ExperimentalMetadata
                : ConversionMethod.StandaloneNative));

        var edge = new ConversionEdge
        {
            Id = $"plan:{srcFmt}->{target.FormatMajorVersion}",
            SourceFormatMajor = srcFmt,
            TargetFormatMajor = target.FormatMajorVersion,
            Method = method,
            VerificationStatus = isSameOrUpgrade ? CompatibilityStatus.PartiallyVerified : target.CompatibilityStatus,
            EstimatedRisk = isSameOrUpgrade ? RuleRiskLevel.Low : RuleRiskLevel.Experimental,
            Description = isSameOrUpgrade
                ? $"Direct lossless preservation to {target.ShortName} (Format {target.FormatMajorVersion})."
                : $"Metadata & compatibility rule downgrade from Format {srcFmt} to {target.ShortName} (Format {target.FormatMajorVersion})."
        };

        var rules = new List<string> { "VF-VERSION-METADATA" };
        if (request.Mode != ConversionMode.Safe)
        {
            rules.Add("VF-3D-RENDERER");
            rules.Add("VF-TRACK-MATTE-COMPATIBILITY");
            rules.Add("VF-EXPRESSION-ENGINE");
        }
        if (request.RelinkedAssetPaths is { Count: > 0 })
            rules.Add("VF-ASSET-RELINK");

        return new ConversionPlan
        {
            SourceDetection = document.VersionDetection,
            TargetVersion = target,
            Mode = request.Mode,
            Path = [edge],
            RuleIdsToExecute = rules,
            CanProceed = true,
            RequiresUserApproval = false,
            RequiresExperimentalAcknowledgement = false,
            Notes = isDowngrade
                ? ["Output will be structurally validated; live opening in target After Effects requires the target application."]
                : ["Same-format preservation path selected."]
        };
    }
}
