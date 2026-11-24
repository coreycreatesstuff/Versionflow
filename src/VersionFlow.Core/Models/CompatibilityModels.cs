namespace VersionFlow.Core.Models;

public sealed record CompatibilityFinding
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string FeatureName { get; init; }
    public string? MatchName { get; init; }
    public string? Location { get; init; }
    public required FeatureClassification Classification { get; init; }
    public required string Description { get; init; }
    public string? ProposedTransformation { get; init; }
    public bool? IsReversible { get; init; }
    public string? VisualOrFunctionalImpact { get; init; }
    public bool RequiresUserApproval { get; init; }
}

public sealed record CompatibilityReport
{
    public required string SourcePath { get; init; }
    public required VersionDetectionResult SourceVersion { get; init; }
    public required AfterEffectsVersionInfo TargetVersion { get; init; }
    public required IReadOnlyList<CompatibilityFinding> Findings { get; init; }
    public int CompatibleCount { get; init; }
    public int ConvertibleCount { get; init; }
    public int WarningCount { get; init; }
    public int UnsupportedCount { get; init; }
    public int CriticalCount { get; init; }
    public int UnknownCount { get; init; }
    public bool HasUninspectedOpaqueRegions { get; init; }
    public string? UninspectedSummary { get; init; }
    public ConversionMethod? RecommendedMethod { get; init; }
    public string SummaryText { get; init; } = string.Empty;
    public DateTimeOffset AnalysedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AppliedTransformation
{
    public required string RuleId { get; init; }
    public required string Description { get; init; }
    public required string Location { get; init; }
    public string? BeforeSummary { get; init; }
    public string? AfterSummary { get; init; }
    public bool IsLossy { get; init; }
    public string? Notes { get; init; }
}
