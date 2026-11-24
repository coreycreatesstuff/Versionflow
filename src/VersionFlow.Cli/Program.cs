using System.Text.Json;
using VersionFlow.AdobeIntegration;
using VersionFlow.Compatibility;
using VersionFlow.Conversion;
using VersionFlow.Core.Errors;
using VersionFlow.Core.Models;
using VersionFlow.Core.Registry;
using VersionFlow.Formats;
using VersionFlow.Infrastructure;
using VersionFlow.Validation;

namespace VersionFlow.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        bool jsonOutput = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        string cmd = args[0].ToLowerInvariant();

        var detector = new VersionDetector();
        var parser = new CompositeProjectParser(detector);
        var analyzer = new CompatibilityAnalyzer();
        var planner = new ConversionPlanner();
        var validator = new StructuralValidator(parser);
        var engine = new ConversionEngine(parser, analyzer, planner, validator);

        try
        {
            return cmd switch
            {
                "versions" => HandleVersions(jsonOutput),
                "inspect" => await HandleInspectAsync(args, parser, jsonOutput),
                "analyse" or "analyze" => await HandleAnalyseAsync(args, parser, analyzer, jsonOutput),
                "convert" => await HandleConvertAsync(args, engine, jsonOutput),
                "batch" => await HandleBatchAsync(args, engine, jsonOutput),
                "validate" => await HandleValidateAsync(args, validator, jsonOutput),
                "verify-target" => await HandleVerifyTargetAsync(args, parser, engine, jsonOutput),
                _ => UnknownCommand(cmd)
            };
        }
        catch (VersionFlowException ex)
        {
            if (jsonOutput)
                Console.WriteLine(JsonSerializer.Serialize(new { error = ex.ErrorCode, message = ex.Message, recovery = ex.RecoveryActions }, JsonOpts));
            else
                Console.Error.WriteLine($"[{ex.ErrorCode}] {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            if (jsonOutput)
                Console.WriteLine(JsonSerializer.Serialize(new { error = "VF-CLI-ERR", message = ex.Message }, JsonOpts));
            else
                Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int HandleVersions(bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(VersionRegistry.All, JsonOpts));
            return 0;
        }
        Console.WriteLine("Supported After Effects Versions:");
        foreach (var v in VersionRegistry.All)
        {
            string targetTag = v.IsSupportedTarget ? $"Target ({v.CompatibilityStatus})" : "Source/Inspect Only";
            Console.WriteLine($"  {v.ReleaseYear} | {v.DisplayName,-30} | App {v.AppMajorVersion}.x | Format {v.FormatMajorVersion} | {targetTag}");
        }
        return 0;
    }

    private static async Task<int> HandleInspectAsync(string[] args, CompositeProjectParser parser, bool json)
    {
        string path = RequirePositional(args, 1, "project path");
        bool showChunks = args.Contains("--chunks", StringComparer.OrdinalIgnoreCase);
        var doc = await parser.ParseAsync(path);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                doc.SourcePath,
                doc.FileSizeBytes,
                Format = doc.FormatKind.ToString(),
                doc.VersionDetection,
                doc.Summary,
                Compositions = doc.Compositions.Select(c => new { c.Name, c.Width, c.Height, c.FrameRate, Layers = c.Layers.Count, c.RendererMatchName }),
                Effects = doc.EffectsUsed.Select(e => e.MatchName).Distinct(),
                doc.UnknownChunkCount,
                doc.UnknownChunkBytes
            }, JsonOpts));
            return 0;
        }

        Console.WriteLine($"File: {doc.SourcePath} ({doc.FileSizeBytes:N0} bytes)");
        Console.WriteLine($"Detected Version: {doc.VersionDetection.DisplayLabel} [{doc.VersionDetection.Confidence}]");
        Console.WriteLine($"Internal Format: {doc.Summary.FormatMajorVersion}.{doc.Summary.FormatMinorVersion} | svap: {(doc.Summary.SvapWord.HasValue ? $"0x{doc.Summary.SvapWord:X8}" : "N/A")}");
        Console.WriteLine($"Compositions: {doc.Summary.CompositionCount} | Layers: {doc.Summary.LayerCount} | Footage: {doc.Summary.FootageCount} | Effects: {doc.EffectsUsed.Count} | Expressions: {doc.ExpressionsUsed.Count}");
        Console.WriteLine($"Unknown chunks: {doc.UnknownChunkCount} ({doc.UnknownChunkBytes:N0} bytes preserved)");
        if (showChunks)
        {
            Console.WriteLine();
            Console.WriteLine(BinaryInspector.FormatChunkHierarchy(doc, 400));
        }
        return 0;
    }

    private static async Task<int> HandleAnalyseAsync(string[] args, CompositeProjectParser parser, CompatibilityAnalyzer analyzer, bool json)
    {
        string path = RequirePositional(args, 1, "project path");
        var target = ResolveTarget(GetOption(args, "--target") ?? "2020");
        var doc = await parser.ParseAsync(path);
        var report = analyzer.Analyse(doc, target);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOpts));
            return 0;
        }

        Console.WriteLine($"Source: {report.SourceVersion.DisplayLabel} -> Target: {report.TargetVersion.DisplayName}");
        Console.WriteLine($"Summary: {report.SummaryText}");
        foreach (var f in report.Findings)
        {
            Console.WriteLine($"  [{f.Classification}] {f.Category}: {f.FeatureName} — {f.Description}");
        }
        return report.CriticalCount > 0 ? 3 : 0;
    }

    private static async Task<int> HandleConvertAsync(string[] args, ConversionEngine engine, bool json)
    {
        string path = RequirePositional(args, 1, "project path");
        var target = ResolveTarget(GetOption(args, "--target") ?? "2020");
        var mode = ParseMode(GetOption(args, "--mode"));
        string? outputDir = GetOption(args, "--output");
        bool ackExp = args.Contains("--ack-experimental", StringComparer.OrdinalIgnoreCase);

        var result = await engine.ConvertAsync(new ConversionRequest
        {
            SourcePath = path,
            TargetVersion = target,
            Mode = mode,
            OutputDirectory = outputDir,
            ExperimentalAcknowledged = ackExp,
            GenerateReport = !args.Contains("--no-report", StringComparer.OrdinalIgnoreCase)
        });

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
            return result.Succeeded ? 0 : 1;
        }

        Console.WriteLine(ReportGenerator.BuildPlainTextReport(result));
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"Blocked/Failed [{result.ErrorCode}]: {result.UserErrorMessage}");
            return 1;
        }
        return 0;
    }

    private static async Task<int> HandleBatchAsync(string[] args, ConversionEngine engine, bool json)
    {
        string folder = RequirePositional(args, 1, "folder path");
        if (!Directory.Exists(folder))
            throw new VersionFlowException(ErrorCodes.SourceUnreadable, $"Folder does not exist: {folder}");

        var target = ResolveTarget(GetOption(args, "--target") ?? "2020");
        var mode = ParseMode(GetOption(args, "--mode"));
        string? outputDir = GetOption(args, "--output");
        bool ackExp = args.Contains("--ack-experimental", StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(folder, "*.aep")
            .Concat(Directory.EnumerateFiles(folder, "*.aepx"))
            .ToList();

        var results = new List<ConversionResult>();
        foreach (var file in files)
        {
            var res = await engine.ConvertAsync(new ConversionRequest
            {
                SourcePath = file,
                TargetVersion = target,
                Mode = mode,
                OutputDirectory = outputDir,
                ExperimentalAcknowledged = ackExp
            });
            results.Add(res);
            if (!json)
                Console.WriteLine($"[{res.FinalState}] {Path.GetFileName(file)} -> {res.OutputPath ?? res.UserErrorMessage}");
        }

        if (json)
            Console.WriteLine(JsonSerializer.Serialize(results, JsonOpts));

        return results.All(r => r.Succeeded) ? 0 : 1;
    }

    private static async Task<int> HandleValidateAsync(string[] args, StructuralValidator validator, bool json)
    {
        string path = RequirePositional(args, 1, "project path");
        var target = ResolveTarget(GetOption(args, "--target") ?? "2020");
        var res = await validator.ValidateAsync(path, null, target, verifyWithInstalledAe: false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(res, JsonOpts));
            return res.IsValid ? 0 : 1;
        }

        Console.WriteLine($"Valid: {res.IsValid} | Level: {res.AchievedLevel}");
        foreach (var c in res.ChecksPassed) Console.WriteLine($"  [PASS] {c}");
        foreach (var w in res.Warnings) Console.WriteLine($"  [WARN] {w}");
        foreach (var e in res.Errors) Console.WriteLine($"  [FAIL] {e}");
        return res.IsValid ? 0 : 1;
    }

    private static async Task<int> HandleVerifyTargetAsync(
        string[] args,
        CompositeProjectParser parser,
        ConversionEngine engine,
        bool json)
    {
        string? explicitProject = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : null;
        string? targetArg = GetOption(args, "--target");
        string? reportPath = GetOption(args, "--report");
        bool includePaths = args.Contains("--include-paths", StringComparer.OrdinalIgnoreCase);

        var settingsService = new JsonSettingsService();
        var adobeService = new AdobeInstallationService(settingsService);
        var installs = await adobeService.DetectInstallationsAsync();
        if (installs.Count == 0)
            throw new VersionFlowException(ErrorCodes.TargetAeUnavailable, "No local After Effects 2020–2026 installation was detected.");

        var targetsToTest = new List<(AfterEffectsVersionInfo Version, AdobeInstallationInfo Install)>();
        if (!string.IsNullOrWhiteSpace(targetArg))
        {
            var requestedTarget = ResolveTarget(targetArg);
            var matchingInstall = installs.FirstOrDefault(i => i.AppMajorVersion == requestedTarget.AppMajorVersion)
                ?? throw new VersionFlowException(ErrorCodes.TargetAeUnavailable,
                    $"Requested target {requestedTarget.DisplayName} (App {requestedTarget.AppMajorVersion}.x) is not installed locally.");
            targetsToTest.Add((requestedTarget, matchingInstall));
        }
        else
        {
            foreach (var v in VersionRegistry.SupportedTargets)
            {
                var install = installs.FirstOrDefault(i => i.AppMajorVersion == v.AppMajorVersion);
                if (install is not null)
                    targetsToTest.Add((v, install));
            }
        }

        if (targetsToTest.Count == 0)
            throw new VersionFlowException(ErrorCodes.TargetAeUnavailable, "Installed After Effects versions did not match supported targets (AE 2020–2026).");

        string? projectPath = explicitProject;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            foreach (var (_, inst) in targetsToTest)
            {
                if (string.IsNullOrWhiteSpace(inst.SupportFilesDirectory)) continue;
                string candidate = Path.Combine(inst.SupportFilesDirectory, "Required", "secret.aep");
                if (File.Exists(candidate))
                {
                    projectPath = candidate;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            throw new VersionFlowException(ErrorCodes.SourceUnreadable,
                "Provide a .aep/.aepx project path: versionflow verify-target <project.aep> [--target <year>]");

        var parsedSource = await parser.ParseAsync(projectPath);
        string? compName = parsedSource.Compositions.FirstOrDefault()?.Name;
        var validator = new AdobeAssistedValidator();
        var entries = new List<TargetVerificationEntry>();
        bool allPassed = true;

        string workDir = Path.Combine(Path.GetTempPath(), "vf_tester_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            foreach (var (ver, inst) in targetsToTest)
            {
                string outSubDir = Path.Combine(workDir, ver.Id);
                Directory.CreateDirectory(outSubDir);

                var conv = await engine.ConvertAsync(new ConversionRequest
                {
                    SourcePath = projectPath,
                    TargetVersion = ver,
                    Mode = ConversionMode.Compatibility,
                    OutputDirectory = outSubDir,
                    GenerateReport = false
                });

                VerificationOutcome outcome;
                if (conv.Succeeded && conv.OutputPath is not null)
                {
                    outcome = await validator.VerifyProjectInAfterEffectsAsync(
                        inst,
                        conv.OutputPath,
                        TimeSpan.FromSeconds(60),
                        CancellationToken.None,
                        compName);
                }
                else
                {
                    outcome = new VerificationOutcome(false, VerificationLevel.None, conv.UserErrorMessage ?? "Conversion failed.");
                }

                if (!outcome.VerifiedInApp)
                    allPassed = false;

                entries.Add(new TargetVerificationEntry(
                    ver.Id,
                    ver.DisplayName,
                    ver.FormatMajorVersion,
                    inst.FullVersion,
                    parsedSource.VersionDetection.DisplayLabel,
                    parsedSource.Summary.FormatMajorVersion,
                    conv.Succeeded,
                    conv.UsedMethod?.ToString(),
                    conv.Validation?.IsValid ?? false,
                    outcome.VerifiedInApp,
                    outcome.AchievedLevel.ToString(),
                    outcome.Summary,
                    conv.Warnings.Count));
            }
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { }
        }

        var submission = new
        {
            Schema = "versionflow-verification-submission/1.0",
            VersionFlowCli = "1.0.0",
            TimestampUtc = DateTimeOffset.UtcNow,
            OsVersion = Environment.OSVersion.VersionString,
            SourceFile = includePaths ? projectPath : Path.GetFileName(projectPath),
            SourceFormatMajor = parsedSource.Summary.FormatMajorVersion,
            CompositionCount = parsedSource.Compositions.Count,
            LayerCount = parsedSource.Summary.LayerCount,
            Results = entries
        };

        string payloadJson = JsonSerializer.Serialize(submission, JsonOpts);
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(reportPath, payloadJson);
        }

        if (json)
        {
            Console.WriteLine(payloadJson);
        }
        else
        {
            Console.WriteLine($"VersionFlow Target Verification Summary ({entries.Count} installed target(s)):");
            foreach (var item in entries)
            {
                string badge = item.VerifiedInApp ? "PASS" : "FAIL";
                Console.WriteLine($"  [{badge}] {item.TargetName} (Build {item.InstalledBuild}) | Level: {item.AchievedLevel} | {item.Summary}");
            }
            if (!string.IsNullOrWhiteSpace(reportPath))
                Console.WriteLine($"Submission JSON saved to: {reportPath}");
        }

        return allPassed ? 0 : 1;
    }

    private sealed record TargetVerificationEntry(
        string TargetId,
        string TargetName,
        int TargetFormatMajor,
        string InstalledBuild,
        string SourceDetected,
        int? SourceFormatMajor,
        bool ConversionSucceeded,
        string? ConversionMethod,
        bool StructuralValidationPassed,
        bool VerifiedInApp,
        string AchievedLevel,
        string Summary,
        int WarningCount);

    private static AfterEffectsVersionInfo ResolveTarget(string val)
    {
        if (int.TryParse(val, out int num))
        {
            var byNum = VersionRegistry.FindByYearOrAppMajor(num);
            if (byNum is not null) return byNum;
        }
        return VersionRegistry.FindById(val)
               ?? throw new VersionFlowException(ErrorCodes.UnknownProjectVersion, $"Unknown target version '{val}'. Run 'versionflow versions' to list supported releases.");
    }

    private static ConversionMode ParseMode(string? raw) => (raw?.ToLowerInvariant()) switch
    {
        null or "compatibility" or "compat" => ConversionMode.Compatibility,
        "safe" => ConversionMode.Safe,
        "experimental" or "exp" => ConversionMode.Experimental,
        _ => throw new ArgumentException($"Invalid mode '{raw}'. Expected safe, compatibility, or experimental.")
    };

    private static string RequirePositional(string[] args, int index, string name)
    {
        if (args.Length <= index || args[index].StartsWith("--"))
            throw new ArgumentException($"Missing required {name}.");
        return args[index];
    }

    private static string? GetOption(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command '{cmd}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
VersionFlow CLI 1.0.0 — Create forward. Convert backward.
Usage:
  versionflow versions [--json]
  versionflow inspect <project.aep|aepx> [--chunks] [--json]
  versionflow analyse <project.aep|aepx> --target <year|major> [--json]
  versionflow convert <project.aep|aepx> --target <year|major> [--mode compatibility|safe|experimental] [--output <dir>] [--ack-experimental] [--json]
  versionflow batch <folder> --target <year|major> [--mode compatibility|safe|experimental] [--output <dir>] [--ack-experimental] [--json]
  versionflow validate <project.aep|aepx> --target <year|major> [--json]
  versionflow verify-target [<project.aep|aepx>] [--target <year|major>] [--report <path.json>] [--include-paths] [--json]
""");
    }
}
