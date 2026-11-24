using VersionFlow.Core.Models;

namespace VersionFlow.Core.Interfaces;

public interface IProjectParser
{
    bool CanHandleExtension(string filePath);
    Task<ParsedProjectDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface IProjectWriter
{
    Task WriteAsync(ParsedProjectDocument document, string outputPath, CancellationToken cancellationToken = default);
}

public interface IVersionDetector
{
    VersionDetectionResult Detect(ParsedProjectDocument document);
}

public interface ICompatibilityAnalyzer
{
    CompatibilityReport Analyse(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion);
}

public interface IMigrationRule
{
    string Id { get; }
    string Description { get; }
    RuleRiskLevel RiskLevel { get; }
    bool IsLossy { get; }
    string ResearchReference { get; }
    CompatibilityStatus VerificationStatus { get; }
    bool AppliesTo(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion, ConversionMode mode);
    IReadOnlyList<AppliedTransformation> Apply(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion, ConversionMode mode, IReadOnlySet<string> approvedFindingIds);
}

public interface IConversionStrategy
{
    ConversionMethod Method { get; }
    string DisplayName { get; }
    bool CanExecute(ConversionPlan plan, ConversionRequest request);
    Task<IReadOnlyList<AppliedTransformation>> ExecuteAsync(
        ParsedProjectDocument workingCopy,
        ConversionPlan plan,
        ConversionRequest request,
        string workspaceDirectory,
        IProgress<ConversionStepProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IConversionPlanner
{
    IReadOnlyList<ConversionEdge> GetCompatibilityGraph();
    ConversionPlan CreatePlan(ParsedProjectDocument document, CompatibilityReport report, ConversionRequest request);
}

public interface IConversionEngine
{
    Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionStepProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IProjectValidator
{
    Task<ValidationResult> ValidateAsync(
        string outputPath,
        ParsedProjectDocument? originalSource,
        AfterEffectsVersionInfo targetVersion,
        bool verifyWithInstalledAe,
        CancellationToken cancellationToken = default);
}

public interface IAdobeInstallationService
{
    Task<IReadOnlyList<AdobeInstallationInfo>> DetectInstallationsAsync(CancellationToken cancellationToken = default);
    Task<AdobeInstallationInfo?> ValidateAndRegisterManualPathAsync(string executablePath, CancellationToken cancellationToken = default);
}

public interface IConversionHistoryService
{
    Task<IReadOnlyList<ConversionHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task AddEntryAsync(ConversionHistoryEntry entry, CancellationToken cancellationToken = default);
    Task RemoveEntryAsync(string jobId, CancellationToken cancellationToken = default);
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
}

public interface IAssetResolver
{
    IReadOnlyList<AssetInspectionItem> InspectAssets(ParsedProjectDocument document);
    IReadOnlyList<AssetInspectionItem> AutoSearchDirectory(IReadOnlyList<AssetInspectionItem> items, string searchRoot);
}

public interface ISettingsService
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<AppSettings> ResetAsync(CancellationToken cancellationToken = default);
}
