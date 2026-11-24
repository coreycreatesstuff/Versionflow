using System.Text.Json;
using VersionFlow.Compatibility;
using VersionFlow.Conversion;
using VersionFlow.Core.Models;
using VersionFlow.Core.Registry;
using VersionFlow.Formats;
using VersionFlow.Validation;

namespace VersionFlow.Worker;

public sealed record WorkerRequest(
    string Operation, // "parse" | "analyse" | "convert" | "convert-stream"
    string SourcePath,
    string? TargetVersionId = null,
    ConversionMode Mode = ConversionMode.Compatibility,
    string? OutputDirectory = null,
    string? OutputTemplate = null,
    bool GenerateReport = true,
    bool ExperimentalAcknowledged = false);

public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            string inputJson = args.Length > 0 && File.Exists(args[0])
                ? await File.ReadAllTextAsync(args[0])
                : await Console.In.ReadToEndAsync();

            var req = JsonSerializer.Deserialize<WorkerRequest>(inputJson, JsonOpts);
            if (req is null || string.IsNullOrWhiteSpace(req.SourcePath))
            {
                Console.Error.WriteLine("Invalid worker request payload.");
                return 2;
            }

            var detector = new VersionDetector();
            var parser = new CompositeProjectParser(detector);
            var analyzer = new CompatibilityAnalyzer();
            var planner = new ConversionPlanner();
            var validator = new StructuralValidator(parser);
            var engine = new ConversionEngine(parser, analyzer, planner, validator);

            switch (req.Operation.ToLowerInvariant())
            {
                case "parse":
                {
                    var doc = await parser.ParseAsync(req.SourcePath);
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        doc.FileSizeBytes,
                        doc.FormatKind,
                        doc.VersionDetection,
                        doc.Summary,
                        doc.UnknownChunkCount,
                        doc.UnknownChunkBytes
                    }, JsonOpts));
                    return 0;
                }
                case "analyse":
                {
                    var doc = await parser.ParseAsync(req.SourcePath);
                    var target = VersionRegistry.FindById(req.TargetVersionId)
                                 ?? VersionRegistry.FindById("ae-2020")!;
                    var report = analyzer.Analyse(doc, target);
                    Console.WriteLine(JsonSerializer.Serialize(report, JsonOpts));
                    return 0;
                }
                case "convert":
                case "convert-stream":
                {
                    bool stream = req.Operation.Equals("convert-stream", StringComparison.OrdinalIgnoreCase);
                    var target = VersionRegistry.FindById(req.TargetVersionId)
                                 ?? VersionRegistry.FindById("ae-2020")!;
                    var progress = stream
                        ? new Progress<ConversionStepProgress>(p =>
                            Console.WriteLine(JsonSerializer.Serialize(new { kind = "progress", data = p }, JsonOpts)))
                        : null;

                    var res = await engine.ConvertAsync(new ConversionRequest
                    {
                        SourcePath = req.SourcePath,
                        TargetVersion = target,
                        Mode = req.Mode,
                        OutputDirectory = req.OutputDirectory,
                        OutputFileNameTemplate = req.OutputTemplate,
                        GenerateReport = req.GenerateReport,
                        ExperimentalAcknowledged = req.ExperimentalAcknowledged
                    }, progress);

                    Console.WriteLine(stream
                        ? JsonSerializer.Serialize(new { kind = "result", data = res }, JsonOpts)
                        : JsonSerializer.Serialize(res, JsonOpts));
                    return res.Succeeded ? 0 : 1;
                }
                default:
                    Console.Error.WriteLine($"Unknown worker operation: {req.Operation}");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }
}
