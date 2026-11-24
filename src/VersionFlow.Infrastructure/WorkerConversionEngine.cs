using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;
using VersionFlow.Core.Registry;

namespace VersionFlow.Infrastructure;

public sealed class WorkerConversionEngine : IConversionEngine
{
    private readonly IConversionEngine _fallbackEngine;
    private readonly LocalFileLogger _logger;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WorkerConversionEngine(IConversionEngine fallbackEngine, LocalFileLogger logger)
    {
        _fallbackEngine = fallbackEngine;
        _logger = logger;
    }

    public async Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionStepProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string workerExe = Path.Combine(AppContext.BaseDirectory, "VersionFlow.Worker.exe");
        if (!File.Exists(workerExe))
        {
            return await _fallbackEngine.ConvertAsync(request, progress, cancellationToken);
        }

        // Fall back to in-process engine when relink map or packaging flags need direct local access not serialized in simple worker payload
        if (request.RelinkedAssetPaths is { Count: > 0 } || request.PackageAssets)
        {
            return await _fallbackEngine.ConvertAsync(request, progress, cancellationToken);
        }

        try
        {
            var payload = new
            {
                operation = "convert-stream",
                sourcePath = request.SourcePath,
                targetVersionId = request.TargetVersion.Id,
                mode = request.Mode,
                outputDirectory = request.OutputDirectory,
                outputTemplate = request.OutputFileNameTemplate,
                generateReport = request.GenerateReport,
                experimentalAcknowledged = request.ExperimentalAcknowledged
            };

            var psi = new ProcessStartInfo
            {
                FileName = workerExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return await _fallbackEngine.ConvertAsync(request, progress, cancellationToken);

            await proc.StandardInput.WriteAsync(JsonSerializer.Serialize(payload, JsonOpts));
            proc.StandardInput.Close();

            ConversionResult? finalResult = null;
            using var reg = cancellationToken.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            });

            while (await proc.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("kind", out var kindEl)) continue;

                string? kind = kindEl.GetString();
                if (kind == "progress" && doc.RootElement.TryGetProperty("data", out var pEl))
                {
                    var step = pEl.Deserialize<ConversionStepProgress>(JsonOpts);
                    if (step is not null) progress?.Report(step);
                }
                else if (kind == "result" && doc.RootElement.TryGetProperty("data", out var rEl))
                {
                    finalResult = rEl.Deserialize<ConversionResult>(JsonOpts);
                }
            }

            await proc.WaitForExitAsync(cancellationToken);
            if (finalResult is not null)
                return finalResult;

            string err = await proc.StandardError.ReadToEndAsync(cancellationToken);
            _logger.Log("WARN", $"Worker exited ({proc.ExitCode}) without result; falling back in-process. StdErr: {err}");
            return await _fallbackEngine.ConvertAsync(request, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return await _fallbackEngine.ConvertAsync(request, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Log("WARN", $"Worker IPC failed ({ex.Message}); falling back in-process.");
            return await _fallbackEngine.ConvertAsync(request, progress, cancellationToken);
        }
    }
}
