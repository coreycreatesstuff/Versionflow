using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VersionFlow.Conversion.Rules;
using VersionFlow.Core.Errors;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;
using VersionFlow.Formats.Aepx;
using VersionFlow.Formats.Rifx;

namespace VersionFlow.Conversion;

public sealed class ConversionEngine : IConversionEngine
{
    private readonly IProjectParser _parser;
    private readonly ICompatibilityAnalyzer _analyzer;
    private readonly IConversionPlanner _planner;
    private readonly IProjectValidator _validator;
    private readonly IEnumerable<IMigrationRule> _rules;
    private readonly string _tempRoot;

    public ConversionEngine(
        IProjectParser parser,
        ICompatibilityAnalyzer analyzer,
        IConversionPlanner planner,
        IProjectValidator validator,
        IEnumerable<IMigrationRule>? rules = null,
        string? tempRoot = null)
    {
        _parser = parser;
        _analyzer = analyzer;
        _planner = planner;
        _validator = validator;
        _rules = rules ??
        [
            new VersionMetadataMigrationRule(),
            new ThreeDRendererCompatibilityRule(),
            new TrackMatteCompatibilityRule()
        ];
        _tempRoot = tempRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VersionFlow", "Temp");
    }

    public async Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionStepProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string jobId = Guid.NewGuid().ToString("N")[..12];
        string jobWorkspace = Path.Combine(_tempRoot, $"job_{jobId}");
        string? tempOutputOnTargetVol = null;

        void Report(ConversionJobState state, string label, string? detail = null, double? ratio = null) =>
            progress?.Report(new ConversionStepProgress
            {
                State = state,
                StageLabel = label,
                Detail = detail,
                ProgressRatio = ratio,
                Elapsed = sw.Elapsed
            });

        string? sourceShaBefore = null;
        ParsedProjectDocument? sourceDoc = null;
        CompatibilityReport? compat = null;

        try
        {
            // 1. Validate source & compute checksum (read-only)
            Report(ConversionJobState.ReadingSource, "Reading & hashing source project", request.SourcePath, 0.08);
            if (!File.Exists(request.SourcePath))
                throw new VersionFlowException(ErrorCodes.SourceUnreadable, $"Source file not found: {request.SourcePath}");

            sourceShaBefore = await ComputeSha256Async(request.SourcePath, cancellationToken);

            // 2. Create isolated job workspace
            Directory.CreateDirectory(jobWorkspace);

            // 3. Parse source
            Report(ConversionJobState.DetectingVersion, "Parsing & detecting project version", Path.GetFileName(request.SourcePath), 0.22);
            sourceDoc = await _parser.ParseAsync(request.SourcePath, cancellationToken);

            // 4. Compatibility scan
            Report(ConversionJobState.AnalysingProject, "Analyzing feature compatibility", $"Target: {request.TargetVersion.DisplayName}", 0.36);
            compat = _analyzer.Analyse(sourceDoc, request.TargetVersion);

            // 5. Plan conversion
            Report(ConversionJobState.PlanningConversion, "Planning migration path", request.Mode.ToString(), 0.48);
            var plan = _planner.CreatePlan(sourceDoc, compat, request);
            if (!plan.CanProceed)
            {
                var blockedState = plan.BlockErrorCode == ErrorCodes.ExperimentalAckRequired || plan.BlockErrorCode == ErrorCodes.ApprovalRequired
                    ? ConversionJobState.AwaitingApproval
                    : ConversionJobState.Blocked;

                return new ConversionResult
                {
                    JobId = jobId,
                    Succeeded = false,
                    FinalState = blockedState,
                    SourcePath = request.SourcePath,
                    SourceSha256 = sourceShaBefore,
                    SourceConfirmedUnmodified = true,
                    SourceVersion = sourceDoc.VersionDetection,
                    TargetVersion = request.TargetVersion,
                    Duration = sw.Elapsed,
                    Compatibility = compat,
                    ErrorCode = plan.BlockErrorCode ?? ErrorCodes.NoVerifiedStrategy,
                    UserErrorMessage = plan.BlockReason,
                    RecoveryActions =
                    [
                        "Select Compatibility Mode or Experimental Mode (with acknowledgement) if converting across unverified format boundaries.",
                        "Review detailed compatibility findings before retrying.",
                        "If available, use an installed After Effects release via Settings > After Effects."
                    ]
                };
            }

            // 6. Parse a clean working copy for mutation (never mutate source snapshot)
            Report(ConversionJobState.TransformingData, "Applying deterministic migration rules", null, 0.62);
            var workingCopy = await _parser.ParseAsync(request.SourcePath, cancellationToken);
            var applied = new List<AppliedTransformation>();

            foreach (var rule in _rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rule.AppliesTo(workingCopy, request.TargetVersion, request.Mode))
                {
                    applied.AddRange(rule.Apply(workingCopy, request.TargetVersion, request.Mode, request.ApprovedFindingIds));
                }
            }

            if (request.RelinkedAssetPaths is { Count: > 0 })
            {
                applied.AddRange(AssetRelinkMigrationRule.ApplyRelinks(workingCopy, request.RelinkedAssetPaths));
            }

            // 7. Resolve final safe destination & write temporary output on destination volume
            string finalOutputPath = OutputPathHelper.ResolveUniqueOutputPath(
                request.SourcePath,
                request.TargetVersion,
                request.OutputDirectory,
                request.OutputFileNameTemplate,
                request.OutputFormatOverride);

            string outputDir = Path.GetDirectoryName(finalOutputPath)!;
            tempOutputOnTargetVol = Path.Combine(outputDir, $".vf_tmp_{jobId}{Path.GetExtension(finalOutputPath)}");

            Report(ConversionJobState.WritingOutput, "Writing temporary output safely", Path.GetFileName(finalOutputPath), 0.76);
            IProjectWriter writer = workingCopy.FormatKind == ProjectFormatKind.AepxXml
                ? new AepxWriter()
                : new RifxWriter();
            await writer.WriteAsync(workingCopy, tempOutputOnTargetVol, cancellationToken);

            // 8. Validate temporary output before committing
            Report(ConversionJobState.ValidatingStructure, "Validating output container & structure", null, 0.88);
            var validation = await _validator.ValidateAsync(
                tempOutputOnTargetVol,
                sourceDoc,
                request.TargetVersion,
                request.VerifyWithInstalledAfterEffects,
                cancellationToken);

            if (!validation.IsValid)
            {
                throw new VersionFlowException(
                    ErrorCodes.ValidationFailed,
                    $"Output structural validation failed: {string.Join("; ", validation.Errors)}",
                    ["Inspect diagnostic logs.", "Original source file remains untouched."]);
            }

            // 9. Atomic move to final destination (never overwrite existing)
            if (File.Exists(finalOutputPath))
                finalOutputPath = OutputPathHelper.ResolveUniqueOutputPath(
                    request.SourcePath, request.TargetVersion, request.OutputDirectory, request.OutputFileNameTemplate, request.OutputFormatOverride);

            File.Move(tempOutputOnTargetVol, finalOutputPath, overwrite: false);
            tempOutputOnTargetVol = null;

            string outputSha = await ComputeSha256Async(finalOutputPath, cancellationToken);
            string sourceShaAfter = await ComputeSha256Async(request.SourcePath, cancellationToken);
            bool sourceUnmodified = string.Equals(sourceShaBefore, sourceShaAfter, StringComparison.Ordinal);

            // 10. Package assets if requested
            if (request.PackageAssets && sourceDoc.FootageItems.Count > 0)
            {
                await PackageProjectAssetsAsync(sourceDoc, finalOutputPath, cancellationToken);
            }

            var warnings = new List<string>();
            warnings.AddRange(validation.Warnings);
            if (request.Mode == ConversionMode.Experimental)
                warnings.Add("EXPERIMENTAL OUTPUT: Opening in target After Effects has not been verified by VersionFlow.");
            foreach (var f in compat.Findings.Where(x => x.Classification is FeatureClassification.Warning or FeatureClassification.Unsupported))
                warnings.Add($"[{f.Category}] {f.FeatureName}: {f.Description}");

            var finalState = warnings.Count > 0
                ? ConversionJobState.CompletedWithWarnings
                : ConversionJobState.Completed;

            var interimResult = new ConversionResult
            {
                JobId = jobId,
                Succeeded = true,
                FinalState = finalState,
                SourcePath = request.SourcePath,
                OutputPath = finalOutputPath,
                SourceSha256 = sourceShaBefore,
                OutputSha256 = outputSha,
                SourceConfirmedUnmodified = sourceUnmodified,
                SourceVersion = sourceDoc.VersionDetection,
                TargetVersion = request.TargetVersion,
                UsedMethod = plan.Path.FirstOrDefault()?.Method ?? ConversionMethod.StandaloneNative,
                Duration = sw.Elapsed,
                Compatibility = compat,
                Validation = validation,
                AppliedTransformations = applied,
                Warnings = warnings
            };

            // 11. Generate reports if enabled
            string? htmlReportPath = null;
            string? jsonReportPath = null;
            if (request.GenerateReport)
            {
                var reportBase = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(finalOutputPath) + "_Report");
                htmlReportPath = reportBase + ".html";
                jsonReportPath = reportBase + ".json";
                await ReportGenerator.WriteHtmlReportAsync(interimResult, htmlReportPath, cancellationToken);
                await ReportGenerator.WriteJsonReportAsync(interimResult, jsonReportPath, cancellationToken);
            }

            Report(finalState, "Conversion complete", Path.GetFileName(finalOutputPath), 1.0);
            return interimResult with { ReportHtmlPath = htmlReportPath, ReportJsonPath = jsonReportPath };
        }
        catch (OperationCanceledException)
        {
            Report(ConversionJobState.Cancelled, "Conversion cancelled", null, null);
            return new ConversionResult
            {
                JobId = jobId,
                Succeeded = false,
                FinalState = ConversionJobState.Cancelled,
                SourcePath = request.SourcePath,
                SourceSha256 = sourceShaBefore,
                SourceConfirmedUnmodified = true,
                SourceVersion = sourceDoc?.VersionDetection,
                TargetVersion = request.TargetVersion,
                Duration = sw.Elapsed,
                Compatibility = compat,
                UserErrorMessage = "Conversion was cancelled by user. Original source file was not modified."
            };
        }
        catch (VersionFlowException vfEx)
        {
            Report(ConversionJobState.Failed, "Conversion failed", vfEx.Message, null);
            return new ConversionResult
            {
                JobId = jobId,
                Succeeded = false,
                FinalState = ConversionJobState.Failed,
                SourcePath = request.SourcePath,
                SourceSha256 = sourceShaBefore,
                SourceConfirmedUnmodified = true,
                SourceVersion = sourceDoc?.VersionDetection,
                TargetVersion = request.TargetVersion,
                Duration = sw.Elapsed,
                Compatibility = compat,
                ErrorCode = vfEx.ErrorCode,
                UserErrorMessage = vfEx.Message,
                TechnicalDetails = vfEx.ToString(),
                RecoveryActions = vfEx.RecoveryActions
            };
        }
        catch (Exception ex)
        {
            Report(ConversionJobState.Failed, "Unexpected error", ex.Message, null);
            return new ConversionResult
            {
                JobId = jobId,
                Succeeded = false,
                FinalState = ConversionJobState.Failed,
                SourcePath = request.SourcePath,
                SourceSha256 = sourceShaBefore,
                SourceConfirmedUnmodified = true,
                SourceVersion = sourceDoc?.VersionDetection,
                TargetVersion = request.TargetVersion,
                Duration = sw.Elapsed,
                Compatibility = compat,
                ErrorCode = ErrorCodes.OutputWriteFailed,
                UserErrorMessage = $"Conversion could not complete: {ex.Message}",
                TechnicalDetails = ex.ToString(),
                RecoveryActions = ["Verify write permissions on the destination folder.", "Ensure sufficient disk space is available."]
            };
        }
        finally
        {
            try
            {
                if (tempOutputOnTargetVol is not null && File.Exists(tempOutputOnTargetVol))
                    File.Delete(tempOutputOnTargetVol);
                if (!request.RetainIntermediateFiles && Directory.Exists(jobWorkspace))
                    Directory.Delete(jobWorkspace, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of temp workspace
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var hash = await SHA256.HashDataAsync(fs, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task PackageProjectAssetsAsync(ParsedProjectDocument doc, string outputProjectPath, CancellationToken cancellationToken)
    {
        string outDir = Path.GetDirectoryName(outputProjectPath)!;
        string assetsDir = Path.Combine(outDir, "Assets", "Footage");
        Directory.CreateDirectory(assetsDir);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.FootageItems.Where(f => !string.IsNullOrWhiteSpace(f.FilePath) && File.Exists(f.FilePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(item.FilePath!);
            string destName = fileName;
            int idx = 2;
            while (!usedNames.Add(destName))
            {
                destName = $"{Path.GetFileNameWithoutExtension(fileName)}_{idx++}{Path.GetExtension(fileName)}";
            }
            string destPath = Path.Combine(assetsDir, destName);
            await using var src = new FileStream(item.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            await using var dst = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            await src.CopyToAsync(dst, cancellationToken);
        }
    }
}

public static class ReportGenerator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string BuildPlainTextReport(ConversionResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("VERSIONFLOW CONVERSION REPORT");
        sb.AppendLine($"Job ID: {result.JobId}");
        sb.AppendLine($"Source: {result.SourcePath}");
        sb.AppendLine($"Source release: {result.SourceVersion?.DisplayLabel ?? "Unknown"}");
        sb.AppendLine($"Target release: {result.TargetVersion?.DisplayName ?? "Unknown"}");
        sb.AppendLine($"Result: {result.FinalState}");
        sb.AppendLine($"Method: {result.UsedMethod}");
        sb.AppendLine($"Structural validation: {(result.Validation?.IsValid == true ? "Passed" : "Not passed")}");
        sb.AppendLine($"Verification level: {result.Validation?.AchievedLevel}");
        sb.AppendLine($"Source SHA-256: {result.SourceSha256}");
        sb.AppendLine($"Output SHA-256: {result.OutputSha256}");
        sb.AppendLine($"Source file: {(result.SourceConfirmedUnmodified ? "Unmodified" : "Unknown")}");
        sb.AppendLine();
        sb.AppendLine("Changes:");
        if (result.AppliedTransformations.Count == 0)
            sb.AppendLine("- None");
        else
            foreach (var t in result.AppliedTransformations)
                sb.AppendLine($"- [{t.RuleId}] {t.Description} ({t.BeforeSummary} -> {t.AfterSummary})");
        sb.AppendLine();
        sb.AppendLine("Warnings:");
        if (result.Warnings.Count == 0)
            sb.AppendLine("- None");
        else
            foreach (var w in result.Warnings)
                sb.AppendLine($"- {w}");
        return sb.ToString();
    }

    public static async Task WriteJsonReportAsync(ConversionResult result, string path, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result, JsonOpts);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);
    }

    public static async Task WriteHtmlReportAsync(ConversionResult result, string path, CancellationToken ct)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var changesRows = result.AppliedTransformations.Count == 0
            ? "<tr><td colspan='4'>No structural mutations applied.</td></tr>"
            : string.Join("\n", result.AppliedTransformations.Select(t =>
                $"<tr><td><code>{H(t.RuleId)}</code></td><td>{H(t.Description)}</td><td>{H(t.BeforeSummary)}</td><td>{H(t.AfterSummary)}</td></tr>"));
        var warningsList = result.Warnings.Count == 0
            ? "<li>None</li>"
            : string.Join("\n", result.Warnings.Select(w => $"<li>{H(w)}</li>"));

        string html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>VersionFlow Conversion Report — {{H(Path.GetFileName(result.SourcePath))}}</title>
<style>
  body { background:#111116; color:#F4F4F8; font-family:'Segoe UI',sans-serif; margin:32px; line-height:1.5; }
  .card { background:#1A1A23; border:1px solid #292735; border-radius:12px; padding:20px; margin-bottom:20px; }
  h1,h2 { margin-top:0; } h1 { color:#C4B5FD; font-size:22px; } h2 { font-size:16px; color:#A1A1B2; }
  table { width:100%; border-collapse:collapse; } th,td { text-align:left; padding:8px; border-bottom:1px solid #292735; font-size:13px; }
  code { background:#22222D; padding:2px 6px; border-radius:4px; color:#C4B5FD; }
</style>
</head>
<body>
  <div class="card">
    <h1>VersionFlow Conversion Report</h1>
    <p><strong>Result:</strong> {{H(result.FinalState.ToString())}} | <strong>Method:</strong> {{H(result.UsedMethod?.ToString())}} | <strong>Verification:</strong> {{H(result.Validation?.AchievedLevel.ToString())}}</p>
    <p><strong>Source:</strong> {{H(result.SourcePath)}} ({{H(result.SourceVersion?.DisplayLabel)}})<br/>
       <strong>Output:</strong> {{H(result.OutputPath)}} ({{H(result.TargetVersion?.DisplayName)}})<br/>
       <strong>Source SHA-256:</strong> <code>{{H(result.SourceSha256)}}</code> | <strong>Source Unmodified:</strong> {{result.SourceConfirmedUnmodified}}</p>
  </div>
  <div class="card">
    <h2>Applied Transformations</h2>
    <table><thead><tr><th>Rule</th><th>Description</th><th>Before</th><th>After</th></tr></thead><tbody>{{changesRows}}</tbody></table>
  </div>
  <div class="card">
    <h2>Compatibility Warnings &amp; Notes</h2>
    <ul>{{warningsList}}</ul>
  </div>
</body>
</html>
""";
        await File.WriteAllTextAsync(path, html, Encoding.UTF8, ct);
    }
}
