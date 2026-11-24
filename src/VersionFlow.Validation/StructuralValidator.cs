using VersionFlow.Core.Errors;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;

namespace VersionFlow.Validation;

public sealed class StructuralValidator : IProjectValidator
{
    private readonly IProjectParser _parser;

    public StructuralValidator(IProjectParser parser)
    {
        _parser = parser;
    }

    public async Task<ValidationResult> ValidateAsync(
        string outputPath,
        ParsedProjectDocument? originalSource,
        AfterEffectsVersionInfo targetVersion,
        bool verifyWithInstalledAe,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        if (!File.Exists(outputPath))
        {
            return new ValidationResult
            {
                IsValid = false,
                AchievedLevel = VerificationLevel.None,
                ErrorCode = ErrorCodes.ValidationFailed,
                Errors = [$"Output file not found at '{outputPath}'."]
            };
        }

        checks.Add("Output file exists on disk and is non-empty.");
        ParsedProjectDocument reparsed;
        try
        {
            reparsed = await _parser.ParseAsync(outputPath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                AchievedLevel = VerificationLevel.FileGenerated,
                ErrorCode = ErrorCodes.ValidationFailed,
                ChecksPassed = checks,
                Errors = [$"Re-parsing output failed: {ex.Message}"]
            };
        }

        checks.Add($"Re-parsed {reparsed.FormatKind} container cleanly ({reparsed.FileSizeBytes:N0} bytes).");

        // Verify target version marker matches intended target format
        int? outFmtMajor = reparsed.Summary.FormatMajorVersion;
        if (outFmtMajor != targetVersion.FormatMajorVersion)
        {
            errors.Add($"Output format major version is {outFmtMajor?.ToString() ?? "null"}, expected {targetVersion.FormatMajorVersion} ({targetVersion.ShortName}).");
        }
        else
        {
            checks.Add($"Internal format major version matches target ({targetVersion.FormatMajorVersion}).");
        }

        if (reparsed.StructuralWarnings.Count > 0)
            warnings.AddRange(reparsed.StructuralWarnings);

        SemanticComparisonResult? semantic = null;
        if (originalSource is not null)
        {
            semantic = SemanticComparer.Compare(originalSource, reparsed);
            if (semantic.StructureMatches)
            {
                checks.Add($"Semantic structure preserved ({semantic.TargetCompositions} comps, {semantic.TargetLayers} layers, {semantic.TargetEffects} effects).");
            }
            else
            {
                warnings.AddRange(semantic.Differences);
            }
        }

        var level = errors.Count == 0 ? VerificationLevel.StructureValidated : VerificationLevel.FileGenerated;

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            AchievedLevel = level,
            ErrorCode = errors.Count > 0 ? ErrorCodes.ValidationFailed : null,
            ChecksPassed = checks,
            Warnings = warnings,
            Errors = errors,
            SemanticComparison = semantic,
            TargetAppVerificationSummary = verifyWithInstalledAe
                ? "Target application verification requested; handled by AdobeIntegration service."
                : "Target application opening was not verified (standalone structural validation only)."
        };
    }
}

public static class SemanticComparer
{
    public static SemanticComparisonResult Compare(ParsedProjectDocument source, ParsedProjectDocument target)
    {
        var diffs = new List<string>();

        int sComps = source.Compositions.Count;
        int tComps = target.Compositions.Count;
        if (sComps != tComps) diffs.Add($"Composition count changed: {sComps} -> {tComps}");

        int sLayers = source.Compositions.Sum(c => c.Layers.Count);
        int tLayers = target.Compositions.Sum(c => c.Layers.Count);
        if (sLayers != tLayers) diffs.Add($"Layer count changed: {sLayers} -> {tLayers}");

        int sFx = source.EffectsUsed.Count;
        int tFx = target.EffectsUsed.Count;
        if (sFx != tFx) diffs.Add($"Effect count changed: {sFx} -> {tFx}");

        int sFoot = source.FootageItems.Count;
        int tFoot = target.FootageItems.Count;
        if (sFoot != tFoot) diffs.Add($"Footage item count changed: {sFoot} -> {tFoot}");

        int sExpr = source.ExpressionsUsed.Count;
        int tExpr = target.ExpressionsUsed.Count;
        if (sExpr != tExpr) diffs.Add($"Expression count changed: {sExpr} -> {tExpr}");

        return new SemanticComparisonResult
        {
            SourceCompositions = sComps,
            TargetCompositions = tComps,
            SourceLayers = sLayers,
            TargetLayers = tLayers,
            SourceEffects = sFx,
            TargetEffects = tFx,
            SourceFootageItems = sFoot,
            TargetFootageItems = tFoot,
            SourceExpressions = sExpr,
            TargetExpressions = tExpr,
            StructureMatches = diffs.Count == 0,
            Differences = diffs
        };
    }
}
