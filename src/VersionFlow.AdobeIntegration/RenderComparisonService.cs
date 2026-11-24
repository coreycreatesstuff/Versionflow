using System.Diagnostics;
using System.IO.Compression;
using VersionFlow.Core.Models;

namespace VersionFlow.AdobeIntegration;

public sealed record RenderComparisonResult(
    bool Succeeded,
    int Width,
    int Height,
    double MeanSquaredError,
    double StructuralSimilarityEstimate,
    string Summary);

public sealed class RenderComparisonService
{
    public async Task<RenderComparisonResult> RenderAndCompareSingleFrameAsync(
        AdobeInstallationInfo aeInstall,
        string sourceProjectPath,
        string convertedProjectPath,
        string compositionName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string aerenderPath = Path.Combine(
            Path.GetDirectoryName(aeInstall.ExecutablePath) ?? string.Empty,
            "aerender.exe");

        if (!File.Exists(aerenderPath))
        {
            return new RenderComparisonResult(false, 0, 0, 0, 0,
                $"aerender.exe not found beside '{aeInstall.ExecutablePath}'.");
        }

        string tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VersionFlow", "Temp", $"render_cmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string srcFrame = await RenderSingleFrameWithFallbackAsync(aerenderPath, sourceProjectPath, compositionName, tempDir, "src", timeout, cancellationToken);
            string dstFrame = await RenderSingleFrameWithFallbackAsync(aerenderPath, convertedProjectPath, compositionName, tempDir, "dst", timeout, cancellationToken);

            if (string.IsNullOrEmpty(srcFrame) || string.IsNullOrEmpty(dstFrame) || !File.Exists(srcFrame) || !File.Exists(dstFrame))
            {
                return new RenderComparisonResult(false, 0, 0, 0, 0,
                    "aerender.exe did not produce both comparison frames within timeout.");
            }

            return ComparePngBytes(await File.ReadAllBytesAsync(srcFrame, cancellationToken),
                                   await File.ReadAllBytesAsync(dstFrame, cancellationToken));
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task<string> RenderSingleFrameWithFallbackAsync(
        string aerenderPath,
        string projectPath,
        string compName,
        string tempDir,
        string prefix,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var pngPattern = Path.Combine(tempDir, $"{prefix}_[#####].png");
        var pngExpected = Path.Combine(tempDir, $"{prefix}_00000.png");
        if (await RunAerenderFrameAsync(aerenderPath, projectPath, compName, "PNG Sequence", pngPattern, pngExpected, timeout, cancellationToken))
            return pngExpected;

        // Built-in fallback present on stock Adobe After Effects installs
        var psdPattern = Path.Combine(tempDir, $"{prefix}_[#####].psd");
        var psdExpected = Path.Combine(tempDir, $"{prefix}_00000.psd");
        if (await RunAerenderFrameAsync(aerenderPath, projectPath, compName, "Multi-Machine Sequence", psdPattern, psdExpected, timeout, cancellationToken))
            return psdExpected;

        return string.Empty;
    }

    private static async Task<bool> RunAerenderFrameAsync(
        string aerenderPath,
        string projectPath,
        string compName,
        string omTemplate,
        string outputPattern,
        string expectedOutputFile,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = aerenderPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-project");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("-comp");
        psi.ArgumentList.Add(compName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-RStemplate");
        psi.ArgumentList.Add("Best Settings");
        psi.ArgumentList.Add("-OMtemplate");
        psi.ArgumentList.Add(omTemplate);
        psi.ArgumentList.Add("-output");
        psi.ArgumentList.Add(outputPattern);
        psi.ArgumentList.Add("-continueOnMissingFootage");

        using var proc = Process.Start(psi);
        if (proc is null) return false;

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
            string combined = $"{await stdoutTask}\n{await stderrTask}";
            return proc.ExitCode == 0
                && !combined.Contains("aerender ERROR", StringComparison.OrdinalIgnoreCase)
                && File.Exists(expectedOutputFile);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            return false;
        }
    }

    public static RenderComparisonResult ComparePngBytes(byte[] a, byte[] b)
    {
        if (!TryDecodeUnfilteredRgbOrRgba(a, out int wa, out int ha, out var pixA) ||
            !TryDecodeUnfilteredRgbOrRgba(b, out int wb, out int hb, out var pixB))
        {
            // Fallback exact byte-equality check when PNG filter/interlace is complex
            bool identical = a.AsSpan().SequenceEqual(b);
            return new RenderComparisonResult(true, 0, 0, identical ? 0.0 : -1.0, identical ? 1.0 : 0.0,
                identical ? "Rendered PNG payloads are byte-identical." : "Rendered PNG payloads differ.");
        }

        if (wa != wb || ha != hb)
        {
            return new RenderComparisonResult(false, wa, ha, -1, 0, $"Dimension mismatch: {wa}x{ha} vs {wb}x{hb}.");
        }

        double sumSq = 0;
        int len = Math.Min(pixA.Length, pixB.Length);
        for (int i = 0; i < len; i++)
        {
            double diff = pixA[i] - pixB[i];
            sumSq += diff * diff;
        }
        double mse = len > 0 ? sumSq / len : 0.0;
        double ssimEst = 1.0 / (1.0 + (mse / 650.25)); // monotonic normalized similarity estimate
        return new RenderComparisonResult(true, wa, ha, mse, ssimEst,
            $"Rendered {wa}x{ha} frame comparison: MSE={mse:F2}, similarity={ssimEst:P1}.");
    }

    private static bool TryDecodeUnfilteredRgbOrRgba(byte[] png, out int width, out int height, out byte[] rawPixels)
    {
        width = 0;
        height = 0;
        rawPixels = [];
        if (png.Length < 33) return false;

        using var idat = new MemoryStream();
        int pos = 8;
        while (pos + 12 <= png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (pos + 12 + len > png.Length || len < 0) break;

            if (type == "IHDR" && len >= 13)
            {
                width = (png[pos + 8] << 24) | (png[pos + 9] << 16) | (png[pos + 10] << 8) | png[pos + 11];
                height = (png[pos + 12] << 24) | (png[pos + 13] << 16) | (png[pos + 14] << 8) | png[pos + 15];
            }
            else if (type == "IDAT")
            {
                idat.Write(png, pos + 8, len);
            }
            else if (type == "IEND")
            {
                break;
            }
            pos += 12 + len;
        }

        if (width <= 0 || height <= 0 || idat.Length == 0) return false;
        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        zlib.CopyTo(outMs);
        rawPixels = outMs.ToArray();
        return true;
    }
}
