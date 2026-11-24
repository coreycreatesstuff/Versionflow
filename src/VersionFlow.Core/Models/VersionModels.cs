namespace VersionFlow.Core.Models;

public sealed record AfterEffectsVersionInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortName { get; init; }
    public required int ReleaseYear { get; init; }
    public required int AppMajorVersion { get; init; }
    public required string SupportedMinorVersions { get; init; }
    public required int FormatMajorVersion { get; init; }
    public int FormatMinorVersion { get; init; } = 1;
    public uint? CanonicalSvapWord { get; init; }
    public required CompatibilityStatus CompatibilityStatus { get; init; }
    public required string ParserSupport { get; init; }
    public required string WriterSupport { get; init; }
    public required string MigrationSupport { get; init; }
    public required IReadOnlyList<string> KnownFeatureDifferences { get; init; }
    public required IReadOnlyList<ConversionMethod> AvailableMethods { get; init; }
    public required IReadOnlyList<string> ValidationRequirements { get; init; }
    public required IReadOnlyList<string> ResearchReferences { get; init; }
    public bool IsSupportedTarget { get; init; }
    public bool IsSupportedSource { get; init; } = true;
}

public sealed record VersionDetectionResult
{
    public required ProjectFormatKind FormatKind { get; init; }
    public int? FormatMajorVersion { get; init; }
    public int? FormatMinorVersion { get; init; }
    public uint? SvapWord { get; init; }
    public AfterEffectsVersionInfo? PrimaryMatch { get; init; }
    public IReadOnlyList<AfterEffectsVersionInfo> CandidateReleases { get; init; } = [];
    public required string DisplayLabel { get; init; }
    public required DetectionConfidence Confidence { get; init; }
    public required string DetectionMethod { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
