using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;

namespace VersionFlow.AdobeIntegration;

public sealed class AdobeInstallationService : IAdobeInstallationService
{
    private readonly ISettingsService _settingsService;

    public AdobeInstallationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task<IReadOnlyList<AdobeInstallationInfo>> DetectInstallationsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<AdobeInstallationInfo>();
        var seenExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            ScanWindowsRegistry(results, seenExes);
            ScanStandardProgramFiles(results, seenExes);
        }

        foreach (var manualPath in _settingsService.Current.ManualAfterEffectsPaths)
        {
            if (seenExes.Contains(manualPath)) continue;
            var info = InspectExecutable(manualPath, isManual: true);
            if (info is not null && seenExes.Add(info.ExecutablePath))
                results.Add(info);
        }

        return Task.FromResult<IReadOnlyList<AdobeInstallationInfo>>(
            results.OrderByDescending(r => r.AppMajorVersion).ToList());
    }

    public async Task<AdobeInstallationInfo?> ValidateAndRegisterManualPathAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var info = InspectExecutable(executablePath, isManual: true);
        if (info is null) return null;

        var settings = _settingsService.Current;
        if (!settings.ManualAfterEffectsPaths.Contains(info.ExecutablePath, StringComparer.OrdinalIgnoreCase))
        {
            settings.ManualAfterEffectsPaths.Add(info.ExecutablePath);
            await _settingsService.SaveAsync(settings, cancellationToken);
        }
        return info;
    }

    [SupportedOSPlatform("windows")]
    private static void ScanWindowsRegistry(List<AdobeInstallationInfo> results, HashSet<string> seenExes)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Adobe\After Effects");
            if (baseKey is null) return;

            foreach (var subName in baseKey.GetSubKeyNames())
            {
                using var sub = baseKey.OpenSubKey(subName);
                var installPath = sub?.GetValue("InstallPath") as string;
                if (string.IsNullOrWhiteSpace(installPath)) continue;

                var exePath = Path.Combine(installPath, "AfterFX.exe");
                var info = InspectExecutable(exePath, isManual: false);
                if (info is not null && seenExes.Add(info.ExecutablePath))
                    results.Add(info);
            }
        }
        catch
        {
            // Registry access may be restricted; ignore gracefully
        }
    }

    private static void ScanStandardProgramFiles(List<AdobeInstallationInfo> results, HashSet<string> seenExes)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string adobeDir = Path.Combine(programFiles, "Adobe");
        if (!Directory.Exists(adobeDir)) return;

        foreach (var dir in Directory.GetDirectories(adobeDir, "Adobe After Effects*"))
        {
            var exePath = Path.Combine(dir, "Support Files", "AfterFX.exe");
            if (seenExes.Contains(exePath)) continue;
            var info = InspectExecutable(exePath, isManual: false);
            if (info is not null && seenExes.Add(info.ExecutablePath))
                results.Add(info);
        }
    }

    public static AdobeInstallationInfo? InspectExecutable(string executablePath, bool isManual)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;

        if (!string.Equals(Path.GetFileName(executablePath), "AfterFX.exe", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var ver = FileVersionInfo.GetVersionInfo(executablePath);
            int major = ver.FileMajorPart > 0 ? ver.FileMajorPart : ver.ProductMajorPart;
            if (major < 13) return null;

            string displayYear = major switch
            {
                >= 22 => (2000 + major).ToString(),
                18 => "2021",
                17 => "2020",
                16 => "2019",
                15 => "CC 2018",
                14 => "CC 2017",
                13 => "CC 2015",
                _ => major.ToString()
            };

            return new AdobeInstallationInfo
            {
                Id = $"ae-{displayYear.Replace(" ", "-").ToLowerInvariant()}",
                DisplayName = $"Adobe After Effects {displayYear}",
                AppMajorVersion = major,
                FullVersion = ver.FileVersion ?? $"{major}.0",
                ExecutablePath = Path.GetFullPath(executablePath),
                SupportFilesDirectory = Path.GetDirectoryName(executablePath),
                IsManuallyConfigured = isManual,
                IsAvailable = true
            };
        }
        catch
        {
            return null;
        }
    }
}

public sealed class AdobeAssistedValidator
{
    public async Task<VerificationOutcome> VerifyProjectInAfterEffectsAsync(
        AdobeInstallationInfo aeInstall,
        string convertedProjectPath,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? verificationCompName = null)
    {
        if (!File.Exists(aeInstall.ExecutablePath) || !File.Exists(convertedProjectPath))
            return new VerificationOutcome(false, VerificationLevel.StructureValidated, "After Effects executable or converted file missing.");

        string tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VersionFlow", "Temp", $"ae_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string isolatedCopy = Path.Combine(tempDir, Path.GetFileName(convertedProjectPath));
        string resultJsonPath = Path.Combine(tempDir, "verify_result.json");
        string scriptPath = Path.Combine(tempDir, "verify.jsx");

        try
        {
            File.Copy(convertedProjectPath, isolatedCopy, overwrite: true);

            string escapedProj = isolatedCopy.Replace("\\", "/");
            string escapedOut = resultJsonPath.Replace("\\", "/");

            string jsx = $$"""
            (function() {
                var outFile = new File("{{escapedOut}}");
                var report = { opened: false, comps: 0, layers: 0, error: null };
                try {
                    var f = new File("{{escapedProj}}");
                    app.beginSuppressDialogs();
                    var proj = app.open(f);
                    if (proj) {
                        report.opened = true;
                        for (var i = 1; i <= proj.numItems; i++) {
                            var item = proj.item(i);
                            if (item instanceof CompItem) {
                                report.comps++;
                                report.layers += item.numLayers;
                            }
                        }
                        proj.close(CloseOptions.DO_NOT_SAVE_CHANGES);
                    }
                    app.endSuppressDialogs(false);
                } catch (e) {
                    report.error = e.toString();
                }
                outFile.open("w");
                outFile.write('{"opened":' + report.opened + ',"comps":' + report.comps + ',"layers":' + report.layers + ',"error":' + (report.error ? '"' + report.error.replace(/"/g, '\\"') + '"' : 'null') + '}');
                outFile.close();
            })();
            """;
            await File.WriteAllTextAsync(scriptPath, jsx, cancellationToken);

            var psi = new ProcessStartInfo
            {
                FileName = aeInstall.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-noui");
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(scriptPath);

            using var proc = Process.Start(psi);
            if (proc is null)
                return new VerificationOutcome(false, VerificationLevel.StructureValidated, "Failed to launch After Effects process.");

            var scriptTimeout = !string.IsNullOrWhiteSpace(verificationCompName)
                ? TimeSpan.FromSeconds(Math.Min(15, Math.Max(5, timeout.TotalSeconds / 2)))
                : timeout;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(scriptTimeout);

            var deadline = DateTime.UtcNow + scriptTimeout;
            while (!File.Exists(resultJsonPath) && DateTime.UtcNow < deadline)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                if (proc.HasExited) break;
                await Task.Delay(400, timeoutCts.Token);
            }

            if (File.Exists(resultJsonPath))
            {
                var json = await File.ReadAllTextAsync(resultJsonPath, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                bool opened = doc.RootElement.TryGetProperty("opened", out var op) && op.GetBoolean();
                int comps = doc.RootElement.TryGetProperty("comps", out var cp) ? cp.GetInt32() : 0;
                int layers = doc.RootElement.TryGetProperty("layers", out var lp) ? lp.GetInt32() : 0;
                string? err = doc.RootElement.TryGetProperty("error", out var ep) && ep.ValueKind == JsonValueKind.String ? ep.GetString() : null;

                return opened
                    ? new VerificationOutcome(true, VerificationLevel.TargetApplicationOpened, $"Verified in {aeInstall.DisplayName}: opened cleanly ({comps} comps, {layers} layers).")
                    : new VerificationOutcome(false, VerificationLevel.StructureValidated, $"Target After Effects failed to open project: {err ?? "unknown error"}");
            }

            // Fallback: aerender.exe genuinely opens & renders frame 0 without needing ExtendScript file-write permissions.
            string aerenderPath = Path.Combine(
                Path.GetDirectoryName(aeInstall.ExecutablePath) ?? string.Empty,
                "aerender.exe");
            if (File.Exists(aerenderPath) && !string.IsNullOrWhiteSpace(verificationCompName))
            {
                return await TryVerifyViaAerenderAsync(
                    aerenderPath,
                    isolatedCopy,
                    verificationCompName,
                    aeInstall.DisplayName,
                    tempDir,
                    timeout,
                    cancellationToken);
            }

            return new VerificationOutcome(false, VerificationLevel.StructureValidated,
                "After Effects verification timed out or did not emit verification output (Allow Scripts to Write Files may be disabled).");
        }
        catch (OperationCanceledException)
        {
            return new VerificationOutcome(false, VerificationLevel.StructureValidated, "After Effects verification timed out or was cancelled.");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task<VerificationOutcome> TryVerifyViaAerenderAsync(
        string aerenderPath,
        string isolatedCopyPath,
        string compName,
        string aeDisplayName,
        string tempDir,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string outputPattern = Path.Combine(tempDir, "verify_[#####].psd");
        string expectedFrame = Path.Combine(tempDir, "verify_00000.psd");

        var psi = new ProcessStartInfo
        {
            FileName = aerenderPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-project");
        psi.ArgumentList.Add(isolatedCopyPath);
        psi.ArgumentList.Add("-comp");
        psi.ArgumentList.Add(compName);
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add("0");
        psi.ArgumentList.Add("-OMtemplate");
        psi.ArgumentList.Add("Multi-Machine Sequence");
        psi.ArgumentList.Add("-output");
        psi.ArgumentList.Add(outputPattern);
        psi.ArgumentList.Add("-continueOnMissingFootage");

        using var proc = Process.Start(psi);
        if (proc is null)
            return new VerificationOutcome(false, VerificationLevel.StructureValidated, "Failed to launch aerender.exe for verification.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            return new VerificationOutcome(false, VerificationLevel.StructureValidated, "aerender.exe verification timed out.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        string combined = $"{stdout}\n{stderr}";
        bool hasError = combined.Contains("aerender ERROR", StringComparison.OrdinalIgnoreCase);

        if (proc.ExitCode == 0 && !hasError && File.Exists(expectedFrame))
        {
            return new VerificationOutcome(
                true,
                VerificationLevel.TargetApplicationOpened,
                $"Verified via {aeDisplayName} aerender: opened and rendered '{compName}' frame 0 cleanly.");
        }

        return new VerificationOutcome(
            false,
            VerificationLevel.StructureValidated,
            $"aerender.exe verification did not complete cleanly (exit {proc.ExitCode}).");
    }
}

public sealed record VerificationOutcome(bool VerifiedInApp, VerificationLevel AchievedLevel, string Summary);

public static class SemanticReconstructionExporter
{
    public static string GenerateExtendScript(ParsedProjectDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// VersionFlow Semantic Reconstruction Script (Method D)");
        sb.AppendLine($"// Source: {Path.GetFileName(doc.SourcePath)} ({doc.VersionDetection.DisplayLabel})");
        sb.AppendLine("// NOTE: Reconstructs detected folders, compositions, solids, and layer skeletons.");
        sb.AppendLine("app.beginUndoGroup('VersionFlow Semantic Reconstruction');");
        sb.AppendLine("var proj = app.project || app.newProject();");
        foreach (var comp in doc.Compositions)
        {
            int w = comp.Width > 0 ? comp.Width : 1920;
            int h = comp.Height > 0 ? comp.Height : 1080;
            double fps = comp.FrameRate > 0 ? comp.FrameRate : 24.0;
            double dur = comp.DurationSeconds > 0 ? comp.DurationSeconds : 10.0;
            string safeName = comp.Name.Replace("\\", "\\\\").Replace("'", "\\'");
            sb.AppendLine($"var c_{comp.ItemId} = proj.items.addComp('{safeName}', {w}, {h}, 1.0, {dur:F2}, {fps:F2});");
            foreach (var layer in comp.Layers)
            {
                string safeLayer = layer.Name.Replace("\\", "\\\\").Replace("'", "\\'");
                sb.AppendLine($"c_{comp.ItemId}.layers.addSolid([0.1, 0.1, 0.14], '{safeLayer}', {w}, {h}, 1.0, {dur:F2});");
            }
        }
        sb.AppendLine("app.endUndoGroup();");
        return sb.ToString();
    }
}
