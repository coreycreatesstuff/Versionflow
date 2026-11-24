using System.IO.Compression;
using System.Text.Json;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;

namespace VersionFlow.Infrastructure;

public static class AppPaths
{
    public static string RootDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VersionFlow");

    public static string LogsDir => Path.Combine(RootDataDir, "Logs");
    public static string TempDir => Path.Combine(RootDataDir, "Temp");
    public static string SettingsFile => Path.Combine(RootDataDir, "settings.json");
    public static string HistoryFile => Path.Combine(RootDataDir, "history.json");
}

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public AppSettings Current { get; private set; } = new();

    public JsonSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? AppPaths.SettingsFile;
        LoadSync();
    }

    private void LoadSync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                Current = MigrateSettings(JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings());
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
                Current = MigrateSettings(JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings());
            }
        }
        catch
        {
            Current = new AppSettings();
        }
        return Current;
    }

    private static AppSettings MigrateSettings(AppSettings s)
    {
        if (s.SettingsSchemaVersion < 2)
        {
            s.DefaultConversionMode = ConversionMode.Compatibility;
            if (s.DefaultTargetVersionId is "ae-2019" or "ae-2018" or "ae-2017" or "ae-2015")
                s.DefaultTargetVersionId = "ae-2020";
            s.SettingsSchemaVersion = 2;
        }
        return s;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
    }

    public async Task<AppSettings> ResetAsync(CancellationToken cancellationToken = default)
    {
        var fresh = new AppSettings();
        await SaveAsync(fresh, cancellationToken);
        return fresh;
    }
}

public sealed class JsonConversionHistoryService : IConversionHistoryService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _historyPath;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConversionHistoryService(ISettingsService settings, string? historyPath = null)
    {
        _settings = settings;
        _historyPath = historyPath ?? AppPaths.HistoryFile;
    }

    public async Task<IReadOnlyList<ConversionHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_historyPath)) return [];
            var json = await File.ReadAllTextAsync(_historyPath, cancellationToken);
            var list = JsonSerializer.Deserialize<List<ConversionHistoryEntry>>(json) ?? [];
            return list.OrderByDescending(e => e.TimestampUtc).ToList();
        }
        catch
        {
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddEntryAsync(ConversionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.EnableLocalHistory) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = File.Exists(_historyPath)
                ? (JsonSerializer.Deserialize<List<ConversionHistoryEntry>>(await File.ReadAllTextAsync(_historyPath, cancellationToken)) ?? [])
                : [];
            list.Insert(0, entry);
            if (list.Count > 500) list = list.Take(500).ToList();

            var dir = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_historyPath, JsonSerializer.Serialize(list, JsonOpts), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveEntryAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_historyPath)) return;
            var list = JsonSerializer.Deserialize<List<ConversionHistoryEntry>>(await File.ReadAllTextAsync(_historyPath, cancellationToken)) ?? [];
            list.RemoveAll(e => e.JobId == jobId);
            await File.WriteAllTextAsync(_historyPath, JsonSerializer.Serialize(list, JsonOpts), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_historyPath)) File.Delete(_historyPath);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class LocalAssetResolver : IAssetResolver
{
    public IReadOnlyList<AssetInspectionItem> InspectAssets(ParsedProjectDocument document)
    {
        var list = new List<AssetInspectionItem>();
        foreach (var f in document.FootageItems)
        {
            if (f.Kind == "Solid")
            {
                list.Add(new AssetInspectionItem
                {
                    ItemId = f.ItemId,
                    Name = f.Name,
                    OriginalPath = "(Project Solid)",
                    Status = "Virtual"
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(f.FilePath))
            {
                list.Add(new AssetInspectionItem
                {
                    ItemId = f.ItemId,
                    Name = f.Name,
                    OriginalPath = "(Embedded / No external path)",
                    Status = "Virtual"
                });
                continue;
            }

            string status = f.IsNetworkPath ? "Network"
                : f.IsImageSequence ? "Sequence"
                : (f.FileExistsOnDisk == true ? "Found" : "Missing");

            list.Add(new AssetInspectionItem
            {
                ItemId = f.ItemId,
                Name = f.Name,
                OriginalPath = f.FilePath,
                Status = status
            });
        }
        return list;
    }

    public IReadOnlyList<AssetInspectionItem> AutoSearchDirectory(IReadOnlyList<AssetInspectionItem> items, string searchRoot)
    {
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
            return items;

        var filesByName = Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.Where(i => i.Status == "Missing"))
        {
            var fileName = Path.GetFileName(item.OriginalPath);
            if (!string.IsNullOrEmpty(fileName) && filesByName.TryGetValue(fileName, out var foundPath))
            {
                item.ProposedRelinkPath = foundPath;
            }
        }
        return items;
    }
}

public sealed class LocalFileLogger
{
    private readonly string _logsDir;
    private readonly object _sync = new();

    public LocalFileLogger(string? logsDir = null)
    {
        _logsDir = logsDir ?? AppPaths.LogsDir;
        Directory.CreateDirectory(_logsDir);
    }

    public void Log(string level, string message, string? errorCode = null)
    {
        try
        {
            var path = Path.Combine(_logsDir, $"versionflow-{DateTime.UtcNow:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.UtcNow:O} [{level}] {(errorCode is not null ? $"[{errorCode}] " : "")}{message}{Environment.NewLine}";
            lock (_sync)
            {
                // Rotate if > 5 MB
                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                {
                    var archive = Path.Combine(_logsDir, $"versionflow-{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                    File.Move(path, archive, overwrite: true);
                }
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Never crash application due to diagnostic logging failure
        }
    }

    public string ExportDiagnosticsZip(string outputZipPath, bool redactUserPaths)
    {
        var tempDir = Path.Combine(AppPaths.TempDir, $"diag_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var log in Directory.GetFiles(_logsDir, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
            {
                string content = File.ReadAllText(log);
                if (redactUserPaths && !string.IsNullOrEmpty(userProfile))
                    content = content.Replace(userProfile, "<USER_PROFILE>", StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(Path.Combine(tempDir, Path.GetFileName(log)), content);
            }
            if (File.Exists(outputZipPath)) File.Delete(outputZipPath);
            ZipFile.CreateFromDirectory(tempDir, outputZipPath);
            return outputZipPath;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
