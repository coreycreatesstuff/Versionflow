namespace VersionFlow.Core.Models;

public sealed record ConversionRequest
{
    public required string SourcePath { get; init; }
    public required AfterEffectsVersionInfo TargetVersion { get; init; }
    public ConversionMode Mode { get; init; } = ConversionMode.Compatibility;
    public ConversionMethod? PreferredMethod { get; init; }
    public string? OutputDirectory { get; init; }
    public string? OutputFileNameTemplate { get; init; }
    public ProjectFormatKind? OutputFormatOverride { get; init; }
    public bool GenerateReport { get; init; } = true;
    public bool RetainIntermediateFiles { get; init; }
    public bool VerifyWithInstalledAfterEffects { get; init; }
    public bool ExperimentalAcknowledged { get; init; }
    public IReadOnlySet<string> ApprovedFindingIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string>? RelinkedAssetPaths { get; init; }
    public bool PackageAssets { get; init; }
    public AfterEffectsVersionInfo? ManualSourceOverride { get; init; }
}

public sealed record ConversionStepProgress
{
    public required ConversionJobState State { get; init; }
    public required string StageLabel { get; init; }
    public string? Detail { get; init; }
    public double? ProgressRatio { get; init; }
    public TimeSpan Elapsed { get; init; }
}

public sealed record ConversionEdge
{
    public required string Id { get; init; }
    public required int SourceFormatMajor { get; init; }
    public required int TargetFormatMajor { get; init; }
    public required ConversionMethod Method { get; init; }
    public required CompatibilityStatus VerificationStatus { get; init; }
    public required RuleRiskLevel EstimatedRisk { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> Preconditions { get; init; } = [];
    public IReadOnlyList<string> KnownLimitations { get; init; } = [];
    public IReadOnlyList<string> ExpectedModifications { get; init; } = [];
}

public sealed record ConversionPlan
{
    public required VersionDetectionResult SourceDetection { get; init; }
    public required AfterEffectsVersionInfo TargetVersion { get; init; }
    public required ConversionMode Mode { get; init; }
    public required IReadOnlyList<ConversionEdge> Path { get; init; }
    public required IReadOnlyList<string> RuleIdsToExecute { get; init; }
    public required bool CanProceed { get; init; }
    public string? BlockReason { get; init; }
    public string? BlockErrorCode { get; init; }
    public bool RequiresUserApproval { get; init; }
    public bool RequiresExperimentalAcknowledgement { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record ValidationResult
{
    public required bool IsValid { get; init; }
    public required VerificationLevel AchievedLevel { get; init; }
    public string? ErrorCode { get; init; }
    public IReadOnlyList<string> ChecksPassed { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public SemanticComparisonResult? SemanticComparison { get; init; }
    public string? TargetAppVerificationSummary { get; init; }
}

public sealed record SemanticComparisonResult
{
    public int SourceCompositions { get; init; }
    public int TargetCompositions { get; init; }
    public int SourceLayers { get; init; }
    public int TargetLayers { get; init; }
    public int SourceEffects { get; init; }
    public int TargetEffects { get; init; }
    public int SourceFootageItems { get; init; }
    public int TargetFootageItems { get; init; }
    public int SourceExpressions { get; init; }
    public int TargetExpressions { get; init; }
    public bool StructureMatches { get; init; }
    public IReadOnlyList<string> Differences { get; init; } = [];
}

public sealed record ConversionResult
{
    public required string JobId { get; init; }
    public required bool Succeeded { get; init; }
    public required ConversionJobState FinalState { get; init; }
    public required string SourcePath { get; init; }
    public string? OutputPath { get; init; }
    public string? ReportHtmlPath { get; init; }
    public string? ReportJsonPath { get; init; }
    public string? SourceSha256 { get; init; }
    public string? OutputSha256 { get; init; }
    public bool SourceConfirmedUnmodified { get; init; }
    public VersionDetectionResult? SourceVersion { get; init; }
    public AfterEffectsVersionInfo? TargetVersion { get; init; }
    public ConversionMethod? UsedMethod { get; init; }
    public TimeSpan Duration { get; init; }
    public CompatibilityReport? Compatibility { get; init; }
    public ValidationResult? Validation { get; init; }
    public IReadOnlyList<AppliedTransformation> AppliedTransformations { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? ErrorCode { get; init; }
    public string? UserErrorMessage { get; init; }
    public string? TechnicalDetails { get; init; }
    public IReadOnlyList<string> RecoveryActions { get; init; } = [];
}
