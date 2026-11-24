using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VersionFlow.AdobeIntegration;
using VersionFlow.Conversion;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;
using VersionFlow.Core.Registry;
using VersionFlow.Formats;
using VersionFlow.Infrastructure;

namespace VersionFlow.App.ViewModels;

public enum NavPage
{
    Convert,
    BatchQueue,
    History,
    Compatibility,
    Settings,
    Help,
    About
}

public partial class MainViewModel : ObservableObject
{
    private readonly IProjectParser _parser;
    private readonly ICompatibilityAnalyzer _analyzer;
    private readonly IConversionPlanner _planner;
    private readonly IConversionEngine _engine;
    private readonly IAdobeInstallationService _adobeService;
    private readonly AdobeAssistedValidator _adobeValidator;
    private readonly IConversionHistoryService _historyService;
    private readonly IAssetResolver _assetResolver;
    private readonly ISettingsService _settingsService;
    private readonly LocalFileLogger _logger;

    private CancellationTokenSource? _convertCts;
    private CancellationTokenSource? _batchCts;

    [ObservableProperty] private NavPage _currentPage = NavPage.Convert;
    [ObservableProperty] private string _statusBarText = "Local processing • Offline ready";

    // --- Convert Page State ---
    [ObservableProperty] private ParsedProjectDocument? _loadedProject;
    [ObservableProperty] private string? _loadedFilePath;
    [ObservableProperty] private string _loadedFileName = string.Empty;
    [ObservableProperty] private string _loadedFileSizeText = string.Empty;
    [ObservableProperty] private string _loadedFileTypeText = string.Empty;
    [ObservableProperty] private string _detectedVersionText = "—";
    [ObservableProperty] private string _detectionConfidenceText = "—";
    [ObservableProperty] private string _loadedProjectStatsText = string.Empty;
    [ObservableProperty] private bool _hasLoadedProject;
    [ObservableProperty] private bool _isBusyParsing;

    public IReadOnlyList<AfterEffectsVersionInfo> AvailableTargets { get; } = VersionRegistry.SupportedTargets;
    public IReadOnlyList<AfterEffectsVersionInfo> AllVersions { get; } = VersionRegistry.All;
    public IReadOnlyList<ConversionMode> ConversionModes { get; } = [ConversionMode.Compatibility, ConversionMode.Safe, ConversionMode.Experimental];
    public IReadOnlyList<string> ThemeOptions { get; } = ["Dark", "Light", "System"];

    [ObservableProperty] private AfterEffectsVersionInfo _selectedTargetVersion;
    [ObservableProperty] private ConversionMode _selectedMode = ConversionMode.Compatibility;
    [ObservableProperty] private bool _experimentalAcknowledged;
    [ObservableProperty] private string _outputDirectoryText = string.Empty;
    [ObservableProperty] private string _outputPreviewFileName = string.Empty;
    [ObservableProperty] private bool _packageAssets;
    [ObservableProperty] private bool _showAdvancedOutputOptions;

    [ObservableProperty] private CompatibilityReport? _currentReport;
    [ObservableProperty] private bool _showCompatibilityDetails;
    [ObservableProperty] private string _compatibilityStatusBadge = "Awaiting project";
    [ObservableProperty] private string _compatibilitySummaryLine = "Import a .aep or .aepx project to analyse feature compatibility.";
    [ObservableProperty] private string _recommendedMethodText = "—";

    public ObservableCollection<ApprovalFindingItem> CompatibilityFindings { get; } = [];
    public ObservableCollection<AssetInspectionItem> InspectedAssets { get; } = [];

    [ObservableProperty] private bool _isConverting;
    [ObservableProperty] private string _conversionStageLabel = string.Empty;
    [ObservableProperty] private string _conversionDetailLabel = string.Empty;
    [ObservableProperty] private double _conversionProgressPercent;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private string _conversionElapsedText = "00:00";

    [ObservableProperty] private ConversionResult? _lastResult;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _showTechnicalErrorDetails;

    // --- Batch Queue State ---
    public ObservableCollection<BatchJobViewModel> BatchJobs { get; } = [];
    [ObservableProperty] private BatchJobViewModel? _selectedBatchJob;
    [ObservableProperty] private AfterEffectsVersionInfo _batchGlobalTarget;
    [ObservableProperty] private ConversionMode _batchGlobalMode = ConversionMode.Compatibility;
    [ObservableProperty] private string _batchOutputDirectory = string.Empty;
    [ObservableProperty] private bool _isBatchRunning;
    [ObservableProperty] private bool _isBatchPaused;

    // --- Inspector State ---
    [ObservableProperty] private string _inspectorSearchText = string.Empty;
    public ObservableCollection<InspectorTreeNode> InspectorTree { get; } = [];
    public ObservableCollection<ChunkTreeItem> ChunkTree { get; } = [];
    [ObservableProperty] private ChunkTreeItem? _selectedChunkItem;
    [ObservableProperty] private bool _showBinaryInspectorTab;

    // --- History State ---
    public ObservableCollection<ConversionHistoryEntry> HistoryEntries { get; } = [];
    [ObservableProperty] private string _historySearchText = string.Empty;
    [ObservableProperty] private string _historyFilterResult = "All";

    // --- Compatibility Explorer State ---
    [ObservableProperty] private AfterEffectsVersionInfo? _selectedMatrixVersion;
    [ObservableProperty] private string _matrixSearchText = string.Empty;
    public ObservableCollection<AfterEffectsVersionInfo> FilteredMatrixVersions { get; } = [];
    public ObservableCollection<ConversionEdge> CompatibilityGraphEdges { get; } = [];

    // --- Settings State ---
    [ObservableProperty] private string _selectedTheme = "Dark";
    [ObservableProperty] private string _settingDefaultTargetId = "ae-2020";
    [ObservableProperty] private string _settingOutputTemplate = "{name}_AE{year}";
    [ObservableProperty] private ConversionMode _settingDefaultMode = ConversionMode.Compatibility;
    [ObservableProperty] private int _settingBatchConcurrency = 2;
    [ObservableProperty] private bool _settingRetainIntermediates;
    [ObservableProperty] private bool _settingGenerateReports = true;
    [ObservableProperty] private bool _settingAllowAdobeAutomation;
    [ObservableProperty] private bool _settingVerifyWithAe;
    [ObservableProperty] private bool _settingEnableHistory = true;
    [ObservableProperty] private bool _settingShowBinaryInspector = true;
    [ObservableProperty] private bool _settingRedactPathsOnExport = true;
    public ObservableCollection<AdobeInstallationInfo> DetectedAdobeInstalls { get; } = [];

    public string ModeDescription => SelectedMode switch
    {
        ConversionMode.Safe => "Only verified transformations. Blocks unverified downgrades without modifying source.",
        ConversionMode.Compatibility => "Permits rule-backed compatibility changes with review and approval.",
        ConversionMode.Experimental => "Allows experimental metadata migration. Output must be tested in target After Effects.",
        _ => string.Empty
    };

    public string PrimaryConvertButtonText => IsConverting
        ? "Converting..."
        : $"Convert to {SelectedTargetVersion.ShortName}";

    public bool IsExperimentalMode => SelectedMode == ConversionMode.Experimental;
    public string ShowDetailsButtonText => ShowCompatibilityDetails ? "Hide details" : "View details";
    public bool IsBatchEmpty => BatchJobs.Count == 0;
    public bool IsHistoryEmpty => HistoryEntries.Count == 0;
    public string InstalledAeSummaryText => DetectedAdobeInstalls.Count > 0
        ? string.Join(", ", DetectedAdobeInstalls.Select(a => a.DisplayName.Replace("Adobe ", ""))) + " detected"
        : "No local After Effects detected (standalone mode)";
    public bool CanOpenOutputInTargetAe => LastResult?.Succeeded == true
        && DetectedAdobeInstalls.Any(a => a.AppMajorVersion == SelectedTargetVersion.AppMajorVersion);
    public bool CanRetryInCompatibilityMode => LastResult?.Succeeded == false
        && SelectedMode == ConversionMode.Safe
        && LastResult.FinalState == ConversionJobState.Blocked;

    public MainViewModel(
        IProjectParser parser,
        ICompatibilityAnalyzer analyzer,
        IConversionPlanner planner,
        IConversionEngine engine,
        IAdobeInstallationService adobeService,
        AdobeAssistedValidator adobeValidator,
        IConversionHistoryService historyService,
        IAssetResolver assetResolver,
        ISettingsService settingsService,
        LocalFileLogger logger)
    {
        _parser = parser;
        _analyzer = analyzer;
        _planner = planner;
        _engine = engine;
        _adobeService = adobeService;
        _adobeValidator = adobeValidator;
        _historyService = historyService;
        _assetResolver = assetResolver;
        _settingsService = settingsService;
        _logger = logger;

        var settings = _settingsService.Current;
        _selectedTargetVersion = VersionRegistry.FindById(settings.DefaultTargetVersionId)
                                 ?? VersionRegistry.FindById("ae-2020")!;
        _batchGlobalTarget = _selectedTargetVersion;
        _selectedMode = settings.DefaultConversionMode;
        _batchGlobalMode = settings.DefaultConversionMode;
        _selectedTheme = settings.Theme;
        _settingDefaultTargetId = settings.DefaultTargetVersionId;
        _settingOutputTemplate = settings.OutputNamingTemplate;
        _settingDefaultMode = settings.DefaultConversionMode;
        _settingBatchConcurrency = Math.Clamp(settings.BatchConcurrency, 1, 4);
        _settingRetainIntermediates = settings.RetainIntermediateFiles;
        _settingGenerateReports = settings.GenerateReports;
        _settingAllowAdobeAutomation = settings.AllowAdobeAutomation;
        _settingVerifyWithAe = settings.VerifyWithInstalledAfterEffects;
        _settingEnableHistory = settings.EnableLocalHistory;
        _settingShowBinaryInspector = settings.ShowAdvancedBinaryInspector;
        _selectedMatrixVersion = _selectedTargetVersion;

        foreach (var edge in _planner.GetCompatibilityGraph())
            CompatibilityGraphEdges.Add(edge);

        foreach (var ver in AllVersions)
            FilteredMatrixVersions.Add(ver);

        BatchJobs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsBatchEmpty));
        HistoryEntries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsHistoryEmpty));
    }

    public async Task WarmUpAsync()
    {
        await RefreshAdobeInstallationsAsync();
        await RefreshHistoryAsync();
    }

    partial void OnSelectedTargetVersionChanged(AfterEffectsVersionInfo value)
    {
        OnPropertyChanged(nameof(PrimaryConvertButtonText));
        OnPropertyChanged(nameof(CanOpenOutputInTargetAe));
        UpdateOutputPreview();
        RunCompatibilityScan();
    }

    partial void OnSelectedModeChanged(ConversionMode value)
    {
        OnPropertyChanged(nameof(ModeDescription));
        OnPropertyChanged(nameof(IsExperimentalMode));
        if (value != ConversionMode.Experimental)
            ExperimentalAcknowledged = false;
    }

    partial void OnOutputDirectoryTextChanged(string value) => UpdateOutputPreview();
    partial void OnInspectorSearchTextChanged(string value) => RebuildInspectorTree();
    partial void OnHistorySearchTextChanged(string value) => _ = RefreshHistoryAsync();
    partial void OnHistoryFilterResultChanged(string value) => _ = RefreshHistoryAsync();
    partial void OnShowCompatibilityDetailsChanged(bool value) => OnPropertyChanged(nameof(ShowDetailsButtonText));
    partial void OnMatrixSearchTextChanged(string value) => RebuildMatrixFilter();

    private void RebuildMatrixFilter()
    {
        string q = MatrixSearchText.Trim();
        FilteredMatrixVersions.Clear();
        foreach (var v in AllVersions)
        {
            if (string.IsNullOrEmpty(q)
                || v.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || v.MigrationSupport.Contains(q, StringComparison.OrdinalIgnoreCase)
                || v.FormatMajorVersion.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredMatrixVersions.Add(v);
            }
        }
    }

    [RelayCommand]
    private void SetMode(ConversionMode mode) => SelectedMode = mode;

    [RelayCommand]
    private void SetTheme(string theme) => SelectedTheme = theme;

    [RelayCommand]
    private void SetHistoryFilter(string filter) => HistoryFilterResult = filter;

    partial void OnSelectedThemeChanged(string value)
    {
        App.ApplyTheme(value);
        _settingsService.Current.Theme = value;
        _ = _settingsService.SaveAsync(_settingsService.Current);
    }

    [RelayCommand]
    private void Navigate(NavPage page) => CurrentPage = page;

    [RelayCommand]
    private void ToggleCompatibilityDetails() => ShowCompatibilityDetails = !ShowCompatibilityDetails;

    [RelayCommand]
    private void ToggleAdvancedOutput() => ShowAdvancedOutputOptions = !ShowAdvancedOutputOptions;

    [RelayCommand]
    private async Task BrowseSourceProjectAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select After Effects Project",
            Filter = "After Effects Projects (*.aep;*.aepx)|*.aep;*.aepx|Binary AEP (*.aep)|*.aep|XML AEPX (*.aepx)|*.aepx|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;

        if (dlg.FileNames.Length == 1)
            await LoadProjectAsync(dlg.FileNames[0]);
        else
            await EnqueueBatchFilesAsync(dlg.FileNames);
    }

    [RelayCommand]
    private async Task PastePathFromClipboardAsync()
    {
        if (!Clipboard.ContainsText()) return;
        string raw = Clipboard.GetText().Trim().Trim('"');
        if (File.Exists(raw))
            await LoadProjectAsync(raw);
    }

    public async Task HandleDroppedFilesAsync(string[] files)
    {
        var valid = files.Where(File.Exists).ToArray();
        if (valid.Length == 0) return;

        if (CurrentPage == NavPage.BatchQueue || valid.Length > 1)
            await EnqueueBatchFilesAsync(valid);
        else
            await LoadProjectAsync(valid[0]);
    }

    public async Task LoadProjectAsync(string path)
    {
        IsBusyParsing = true;
        HasResult = false;
        LastResult = null;
        StatusBarText = $"Parsing {Path.GetFileName(path)}...";

        try
        {
            var doc = await _parser.ParseAsync(path);
            LoadedProject = doc;
            LoadedFilePath = path;
            LoadedFileName = Path.GetFileName(path);
            LoadedFileSizeText = FormatBytes(doc.FileSizeBytes);
            LoadedFileTypeText = doc.FormatKind == ProjectFormatKind.AepBinary ? "Binary .aep (RIFX)" : "XML .aepx";
            DetectedVersionText = doc.VersionDetection.DisplayLabel;
            DetectionConfidenceText = doc.VersionDetection.Confidence.ToString();
            LoadedProjectStatsText = $"{doc.Summary.CompositionCount ?? 0} comps • {doc.Summary.LayerCount ?? 0} layers • {doc.EffectsUsed.Count} effects • {doc.ExpressionsUsed.Count} expressions";
            HasLoadedProject = true;

            InspectedAssets.Clear();
            foreach (var asset in _assetResolver.InspectAssets(doc))
                InspectedAssets.Add(asset);

            UpdateOutputPreview();
            RunCompatibilityScan();
            RebuildInspectorTree();
            RebuildChunkTree();
            StatusBarText = $"Loaded {LoadedFileName} ({DetectedVersionText})";
        }
        catch (Exception ex)
        {
            _logger.Log("ERROR", $"Failed to load project '{path}': {ex.Message}", "VF-FMT-ERR");
            HasLoadedProject = false;
            LoadedProject = null;
            CompatibilityStatusBadge = "Parse failed";
            CompatibilitySummaryLine = ex.Message;
            StatusBarText = "Import failed — see details";
            MessageBox.Show($"Could not parse project:\n\n{ex.Message}", "VersionFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusyParsing = false;
        }
    }

    [RelayCommand]
    private void RemoveLoadedProject()
    {
        LoadedProject = null;
        LoadedFilePath = null;
        HasLoadedProject = false;
        HasResult = false;
        LastResult = null;
        CurrentReport = null;
        CompatibilityFindings.Clear();
        InspectedAssets.Clear();
        InspectorTree.Clear();
        ChunkTree.Clear();
        DetectedVersionText = "—";
        DetectionConfidenceText = "—";
        CompatibilityStatusBadge = "Awaiting project";
        CompatibilitySummaryLine = "Import a .aep or .aepx project to analyse feature compatibility.";
        RecommendedMethodText = "—";
        OutputPreviewFileName = string.Empty;
        StatusBarText = "Local processing • Offline ready";
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var dlg = new OpenFolderDialog { Title = "Select Output Directory" };
        if (dlg.ShowDialog() == true)
            OutputDirectoryText = dlg.FolderName;
    }

    [RelayCommand]
    private void ClearOutputDirectory() => OutputDirectoryText = string.Empty;

    private void UpdateOutputPreview()
    {
        if (string.IsNullOrWhiteSpace(LoadedFilePath))
        {
            OutputPreviewFileName = string.Empty;
            return;
        }
        string baseName = Path.GetFileNameWithoutExtension(LoadedFilePath);
        string ext = Path.GetExtension(LoadedFilePath);
        string template = string.IsNullOrWhiteSpace(_settingsService.Current.OutputNamingTemplate)
            ? "{name}_AE{year}"
            : _settingsService.Current.OutputNamingTemplate;

        string name = template
            .Replace("{name}", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", SelectedTargetVersion.ReleaseYear.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{major}", SelectedTargetVersion.AppMajorVersion.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", SelectedTargetVersion.FormatMajorVersion.ToString(), StringComparison.OrdinalIgnoreCase);
        OutputPreviewFileName = $"{name}{ext}";
    }

    private void RunCompatibilityScan()
    {
        if (LoadedProject is null) return;
        var report = _analyzer.Analyse(LoadedProject, SelectedTargetVersion);
        CurrentReport = report;
        CompatibilityFindings.Clear();
        foreach (var f in report.Findings)
            CompatibilityFindings.Add(new ApprovalFindingItem { Finding = f, IsApproved = true });

        CompatibilitySummaryLine = report.SummaryText;
        RecommendedMethodText = report.RecommendedMethod switch
        {
            ConversionMethod.StandaloneNative => "Standalone Native",
            ConversionMethod.XmlIntermediate => "XML Intermediate",
            ConversionMethod.AdobeAssisted => "Adobe-Assisted",
            ConversionMethod.SemanticReconstruction => "Semantic Reconstruction",
            ConversionMethod.ExperimentalMetadata => "Experimental Metadata",
            _ => "Manual / Blocked"
        };
        CompatibilityStatusBadge = report.CriticalCount > 0 ? "Blocked"
            : report.UnsupportedCount > 0 ? $"{report.UnsupportedCount} unsupported"
            : report.WarningCount > 0 ? $"{report.WarningCount} warning(s)"
            : "Ready";
    }

    [RelayCommand]
    private async Task ConvertProjectAsync()
    {
        if (LoadedProject is null || string.IsNullOrWhiteSpace(LoadedFilePath) || IsConverting) return;

        IsConverting = true;
        HasResult = false;
        LastResult = null;
        ShowTechnicalErrorDetails = false;
        _convertCts = new CancellationTokenSource();

        var approvedIds = CompatibilityFindings
            .Where(x => x.IsApproved)
            .Select(x => x.Finding.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relinks = InspectedAssets
            .Where(a => !string.IsNullOrWhiteSpace(a.ProposedRelinkPath))
            .ToDictionary(a => a.OriginalPath, a => a.ProposedRelinkPath!, StringComparer.OrdinalIgnoreCase);

        var progress = new Progress<ConversionStepProgress>(p =>
        {
            ConversionStageLabel = p.StageLabel;
            ConversionDetailLabel = p.Detail ?? string.Empty;
            ConversionElapsedText = p.Elapsed.ToString(@"mm\:ss");
            if (p.ProgressRatio.HasValue)
            {
                IsProgressIndeterminate = false;
                ConversionProgressPercent = p.ProgressRatio.Value * 100.0;
            }
            else
            {
                IsProgressIndeterminate = true;
            }
            StatusBarText = $"{p.StageLabel} ({ConversionElapsedText})";
        });

        try
        {
            var result = await _engine.ConvertAsync(new ConversionRequest
            {
                SourcePath = LoadedFilePath,
                TargetVersion = SelectedTargetVersion,
                Mode = SelectedMode,
                OutputDirectory = string.IsNullOrWhiteSpace(OutputDirectoryText) ? null : OutputDirectoryText,
                OutputFileNameTemplate = _settingsService.Current.OutputNamingTemplate,
                GenerateReport = _settingsService.Current.GenerateReports,
                RetainIntermediateFiles = _settingsService.Current.RetainIntermediateFiles,
                VerifyWithInstalledAfterEffects = _settingsService.Current.VerifyWithInstalledAfterEffects && _settingsService.Current.AllowAdobeAutomation,
                ExperimentalAcknowledged = ExperimentalAcknowledged,
                ApprovedFindingIds = approvedIds,
                RelinkedAssetPaths = relinks.Count > 0 ? relinks : null,
                PackageAssets = PackageAssets
            }, progress, _convertCts.Token);

            // Optional live After Effects verification when explicitly enabled and target AE is installed
            if (result.Succeeded
                && _settingsService.Current.VerifyWithInstalledAfterEffects
                && _settingsService.Current.AllowAdobeAutomation
                && result.OutputPath is not null)
            {
                var targetAe = DetectedAdobeInstalls.FirstOrDefault(a => a.AppMajorVersion == SelectedTargetVersion.AppMajorVersion);
                if (targetAe is not null)
                {
                    ConversionStageLabel = $"Verifying in {targetAe.DisplayName}...";
                    var outcome = await _adobeValidator.VerifyProjectInAfterEffectsAsync(
                        targetAe,
                        result.OutputPath,
                        TimeSpan.FromSeconds(45),
                        _convertCts.Token,
                        LoadedProject?.Compositions.FirstOrDefault()?.Name);
                    if (result.Validation is not null)
                    {
                        result = result with
                        {
                            Validation = result.Validation with
                            {
                                AchievedLevel = outcome.AchievedLevel,
                                TargetAppVerificationSummary = outcome.Summary
                            }
                        };
                    }
                }
            }

            LastResult = result;
            HasResult = true;
            OnPropertyChanged(nameof(CanOpenOutputInTargetAe));
            OnPropertyChanged(nameof(CanRetryInCompatibilityMode));

            if (result.OutputPath is not null || !result.Succeeded)
            {
                await _historyService.AddEntryAsync(new ConversionHistoryEntry
                {
                    JobId = result.JobId,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    SourceFileName = Path.GetFileName(result.SourcePath),
                    SourcePath = result.SourcePath,
                    SourceVersionLabel = result.SourceVersion?.DisplayLabel ?? "Unknown",
                    TargetVersionLabel = result.TargetVersion?.ShortName ?? SelectedTargetVersion.ShortName,
                    OutputFileName = result.OutputPath is not null ? Path.GetFileName(result.OutputPath) : "—",
                    OutputPath = result.OutputPath ?? string.Empty,
                    Method = result.UsedMethod ?? ConversionMethod.StandaloneNative,
                    ResultState = result.FinalState,
                    Duration = result.Duration,
                    ValidationLevel = result.Validation?.AchievedLevel ?? VerificationLevel.None,
                    WarningCount = result.Warnings.Count,
                    ReportPath = result.ReportHtmlPath
                });
                await RefreshHistoryAsync();
            }

            StatusBarText = result.Succeeded ? $"Completed: {Path.GetFileName(result.OutputPath)}" : $"Conversion {result.FinalState}";
        }
        finally
        {
            IsConverting = false;
            _convertCts?.Dispose();
            _convertCts = null;
        }
    }

    [RelayCommand]
    private void CancelConversion() => _convertCts?.Cancel();

    [RelayCommand]
    private async Task SwitchToCompatibilityAndRetryAsync()
    {
        SelectedMode = ConversionMode.Compatibility;
        await ConvertProjectAsync();
    }

    [RelayCommand]
    private void ToggleTechnicalErrorDetails() => ShowTechnicalErrorDetails = !ShowTechnicalErrorDetails;

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var path = LastResult?.OutputPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(LastResult?.OutputPath))
            Clipboard.SetText(LastResult.OutputPath);
    }

    [RelayCommand]
    private void ViewConversionReport()
    {
        var path = LastResult?.ReportHtmlPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenOutputInTargetAe()
    {
        if (LastResult?.OutputPath is not { } outPath || !File.Exists(outPath)) return;
        var targetAe = DetectedAdobeInstalls.FirstOrDefault(a => a.AppMajorVersion == SelectedTargetVersion.AppMajorVersion);
        if (targetAe is null) return;
        var psi = new ProcessStartInfo { FileName = targetAe.ExecutablePath, UseShellExecute = false };
        psi.ArgumentList.Add(outPath);
        Process.Start(psi);
    }

    [RelayCommand]
    private void SearchMissingAssetsFolder()
    {
        if (InspectedAssets.Count == 0) return;
        var dlg = new OpenFolderDialog { Title = "Select Root Folder to Search Missing Footage" };
        if (dlg.ShowDialog() != true) return;
        var updated = _assetResolver.AutoSearchDirectory(InspectedAssets.ToList(), dlg.FolderName);
        InspectedAssets.Clear();
        foreach (var item in updated) InspectedAssets.Add(item);
    }

    // --- Batch Queue Commands ---
    [RelayCommand]
    private async Task AddFilesToBatchAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add Projects to Batch Queue",
            Filter = "After Effects Projects (*.aep;*.aepx)|*.aep;*.aepx",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true)
            await EnqueueBatchFilesAsync(dlg.FileNames);
    }

    public async Task EnqueueBatchFilesAsync(IEnumerable<string> paths)
    {
        CurrentPage = NavPage.BatchQueue;
        foreach (var path in paths)
        {
            var job = new BatchJobViewModel
            {
                JobId = Guid.NewGuid().ToString("N")[..8],
                SourcePath = path,
                FileName = Path.GetFileName(path),
                TargetVersion = BatchGlobalTarget,
                Mode = BatchGlobalMode,
                OutputDirectory = string.IsNullOrWhiteSpace(BatchOutputDirectory) ? null : BatchOutputDirectory
            };
            BatchJobs.Add(job);

            _ = Task.Run(async () =>
            {
                try
                {
                    var doc = await _parser.ParseAsync(path);
                    var report = _analyzer.Analyse(doc, job.TargetVersion);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        job.SourceVersionLabel = doc.VersionDetection.DisplayLabel;
                        job.CompatibilitySummary = report.SummaryText;
                        job.MethodLabel = report.RecommendedMethod?.ToString() ?? "Blocked";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        job.SourceVersionLabel = "Parse error";
                        job.CompatibilitySummary = ex.Message;
                        job.State = ConversionJobState.Failed;
                    });
                }
            });
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ApplyGlobalTargetToBatch()
    {
        foreach (var job in BatchJobs.Where(j => j.State == ConversionJobState.Queued))
        {
            job.TargetVersion = BatchGlobalTarget;
            job.Mode = BatchGlobalMode;
            job.OutputDirectory = string.IsNullOrWhiteSpace(BatchOutputDirectory) ? null : BatchOutputDirectory;
        }
    }

    [RelayCommand]
    private void BrowseBatchOutputDir()
    {
        var dlg = new OpenFolderDialog { Title = "Select Shared Batch Output Directory" };
        if (dlg.ShowDialog() == true) BatchOutputDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private void MoveSelectedBatchUp()
    {
        if (SelectedBatchJob is null) return;
        int idx = BatchJobs.IndexOf(SelectedBatchJob);
        if (idx > 0) BatchJobs.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveSelectedBatchDown()
    {
        if (SelectedBatchJob is null) return;
        int idx = BatchJobs.IndexOf(SelectedBatchJob);
        if (idx >= 0 && idx < BatchJobs.Count - 1) BatchJobs.Move(idx, idx + 1);
    }

    [RelayCommand]
    private void RemoveSelectedBatchJob()
    {
        if (SelectedBatchJob is not null && SelectedBatchJob.State != ConversionJobState.TransformingData)
            BatchJobs.Remove(SelectedBatchJob);
    }

    [RelayCommand]
    private void ClearCompletedBatchJobs()
    {
        var done = BatchJobs.Where(j => j.State is ConversionJobState.Completed or ConversionJobState.CompletedWithWarnings).ToList();
        foreach (var j in done) BatchJobs.Remove(j);
    }

    [RelayCommand]
    private void RetryFailedBatchJobs()
    {
        foreach (var j in BatchJobs.Where(j => j.State is ConversionJobState.Failed or ConversionJobState.Cancelled or ConversionJobState.Blocked))
        {
            j.State = ConversionJobState.Queued;
            j.ProgressRatio = null;
            j.StatusMessage = "Queued for retry";
        }
    }

    [RelayCommand]
    private async Task StartBatchAsync()
    {
        if (IsBatchRunning) return;
        IsBatchRunning = true;
        IsBatchPaused = false;
        _batchCts = new CancellationTokenSource();

        int concurrency = Math.Clamp(_settingsService.Current.BatchConcurrency, 1, 4);
        using var sem = new SemaphoreSlim(concurrency, concurrency);

        try
        {
            var pending = BatchJobs.Where(j => j.State == ConversionJobState.Queued).ToList();
            var tasks = pending.Select(async job =>
            {
                await sem.WaitAsync(_batchCts.Token);
                try
                {
                    while (IsBatchPaused && !_batchCts.Token.IsCancellationRequested)
                        await Task.Delay(250, _batchCts.Token);

                    job.JobCts = CancellationTokenSource.CreateLinkedTokenSource(_batchCts.Token);
                    var progress = new Progress<ConversionStepProgress>(p =>
                    {
                        job.State = p.State;
                        job.ProgressRatio = p.ProgressRatio;
                        job.StatusMessage = p.StageLabel;
                    });

                    var res = await _engine.ConvertAsync(new ConversionRequest
                    {
                        SourcePath = job.SourcePath,
                        TargetVersion = job.TargetVersion,
                        Mode = job.Mode,
                        OutputDirectory = job.OutputDirectory,
                        ExperimentalAcknowledged = job.Mode == ConversionMode.Experimental,
                        GenerateReport = _settingsService.Current.GenerateReports
                    }, progress, job.JobCts.Token);

                    job.State = res.FinalState;
                    job.OutputPath = res.OutputPath;
                    job.StatusMessage = res.Succeeded ? "Completed" : (res.UserErrorMessage ?? res.FinalState.ToString());
                }
                finally
                {
                    sem.Release();
                }
            });
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsBatchRunning = false;
            _batchCts?.Dispose();
            _batchCts = null;
        }
    }

    [RelayCommand]
    private void TogglePauseBatch() => IsBatchPaused = !IsBatchPaused;

    [RelayCommand]
    private void CancelAllBatch() => _batchCts?.Cancel();

    // --- Inspector Commands ---
    private void RebuildInspectorTree()
    {
        InspectorTree.Clear();
        if (LoadedProject is null) return;
        string filter = InspectorSearchText.Trim();

        var root = new InspectorTreeNode
        {
            Title = Path.GetFileName(LoadedProject.SourcePath),
            Subtitle = $"{LoadedProject.VersionDetection.DisplayLabel} • {LoadedProject.Compositions.Count} comps • {LoadedProject.FootageItems.Count} footage items"
        };

        var compsFolder = new InspectorTreeNode { Title = $"Compositions ({LoadedProject.Compositions.Count})" };
        foreach (var comp in LoadedProject.Compositions)
        {
            if (!string.IsNullOrEmpty(filter)
                && !comp.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !comp.Layers.Any(l => l.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;

            var compNode = new InspectorTreeNode
            {
                Title = comp.Name,
                Subtitle = $"{comp.Width}×{comp.Height} • {comp.RendererDisplayName ?? comp.RendererMatchName ?? "Default Renderer"} • {comp.Layers.Count} layers",
                IdentifierToCopy = comp.Name
            };
            foreach (var layer in comp.Layers)
            {
                var layerNode = new InspectorTreeNode
                {
                    Title = $"#{layer.Index} {layer.Name}",
                    Subtitle = $"{layer.LayerType} • {layer.Effects.Count} effects • {layer.Expressions.Count} expressions",
                    IdentifierToCopy = layer.Name
                };
                foreach (var fx in layer.Effects)
                    layerNode.Children.Add(new InspectorTreeNode { Title = fx.MatchName, Subtitle = "Effect", IdentifierToCopy = fx.MatchName });
                foreach (var ex in layer.Expressions)
                    layerNode.Children.Add(new InspectorTreeNode { Title = $"Expression ({ex.LineCount} lines)", Subtitle = ex.ExpressionText.Replace("\n", " "), IdentifierToCopy = ex.ExpressionText });

                compNode.Children.Add(layerNode);
            }
            compsFolder.Children.Add(compNode);
        }
        root.Children.Add(compsFolder);

        var footageFolder = new InspectorTreeNode { Title = $"Footage & Solids ({LoadedProject.FootageItems.Count})" };
        foreach (var f in LoadedProject.FootageItems)
        {
            if (!string.IsNullOrEmpty(filter) && !f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            footageFolder.Children.Add(new InspectorTreeNode
            {
                Title = f.Name,
                Subtitle = $"{f.Kind} {(f.FilePath is not null ? $"• {f.FilePath}" : "")}",
                IdentifierToCopy = f.FilePath ?? f.Name
            });
        }
        root.Children.Add(footageFolder);

        var fxFolder = new InspectorTreeNode { Title = $"Effects Referenced ({LoadedProject.EffectsUsed.Count})" };
        foreach (var fx in LoadedProject.EffectsUsed.DistinctBy(e => e.MatchName))
        {
            if (!string.IsNullOrEmpty(filter) && !fx.MatchName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            fxFolder.Children.Add(new InspectorTreeNode
            {
                Title = fx.MatchName,
                Subtitle = fx.IsBuiltInAdobe ? "Adobe Built-in" : "Third-Party Plugin",
                IdentifierToCopy = fx.MatchName
            });
        }
        root.Children.Add(fxFolder);

        InspectorTree.Add(root);
    }

    private void RebuildChunkTree()
    {
        ChunkTree.Clear();
        if (LoadedProject is null) return;
        foreach (var rootChunk in LoadedProject.RootChunks)
            ChunkTree.Add(ChunkTreeItem.FromNode(rootChunk, 400));
    }

    [RelayCommand]
    private void ExportInspectionJson()
    {
        if (LoadedProject is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export Project Inspection",
            FileName = Path.GetFileNameWithoutExtension(LoadedProject.SourcePath) + "_Inspection.json",
            Filter = "JSON File (*.json)|*.json"
        };
        if (dlg.ShowDialog() != true) return;
        var json = JsonSerializer.Serialize(new
        {
            LoadedProject.SourcePath,
            LoadedProject.FileSizeBytes,
            LoadedProject.VersionDetection,
            LoadedProject.Summary,
            LoadedProject.Compositions,
            LoadedProject.EffectsUsed,
            LoadedProject.UnknownChunkCount
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dlg.FileName, json);
    }

    [RelayCommand]
    private void ExportSemanticReconstructionScript()
    {
        if (LoadedProject is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export Semantic Reconstruction ExtendScript (Method D)",
            FileName = Path.GetFileNameWithoutExtension(LoadedProject.SourcePath) + "_Reconstruct.jsx",
            Filter = "ExtendScript (*.jsx)|*.jsx"
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, SemanticReconstructionExporter.GenerateExtendScript(LoadedProject));
    }

    // --- History Commands ---
    [RelayCommand]
    private async Task RefreshHistoryAsync()
    {
        var items = await _historyService.GetHistoryAsync();
        string q = HistorySearchText.Trim();
        HistoryEntries.Clear();
        foreach (var e in items)
        {
            if (!string.IsNullOrEmpty(q) && !e.SourceFileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !e.TargetVersionLabel.Contains(q, StringComparison.OrdinalIgnoreCase))
                continue;
            if (HistoryFilterResult == "Succeeded" && e.ResultState is not (ConversionJobState.Completed or ConversionJobState.CompletedWithWarnings))
                continue;
            if (HistoryFilterResult == "Failed/Blocked" && e.ResultState is ConversionJobState.Completed or ConversionJobState.CompletedWithWarnings)
                continue;
            HistoryEntries.Add(e);
        }
    }

    [RelayCommand]
    private void OpenHistoryOutputFolder(ConversionHistoryEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.OutputPath) || !File.Exists(entry.OutputPath))
        {
            MessageBox.Show("Output file no longer exists at that location.", "VersionFlow", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.OutputPath}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task RetryHistoryEntryAsync(ConversionHistoryEntry? entry)
    {
        if (entry is null || !File.Exists(entry.SourcePath)) return;
        CurrentPage = NavPage.Convert;
        await LoadProjectAsync(entry.SourcePath);
    }

    [RelayCommand]
    private async Task RemoveHistoryEntryAsync(ConversionHistoryEntry? entry)
    {
        if (entry is null) return;
        await _historyService.RemoveEntryAsync(entry.JobId);
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private async Task ClearAllHistoryAsync()
    {
        await _historyService.ClearHistoryAsync();
        await RefreshHistoryAsync();
    }

    // --- Settings & Adobe Integration Commands ---
    [RelayCommand]
    private async Task RefreshAdobeInstallationsAsync()
    {
        var installs = await _adobeService.DetectInstallationsAsync();
        DetectedAdobeInstalls.Clear();
        foreach (var i in installs) DetectedAdobeInstalls.Add(i);
        OnPropertyChanged(nameof(CanOpenOutputInTargetAe));
        OnPropertyChanged(nameof(InstalledAeSummaryText));
    }

    [RelayCommand]
    private void ViewHistoryReport(ConversionHistoryEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.ReportPath) || !File.Exists(entry.ReportPath))
        {
            MessageBox.Show("Conversion report file is unavailable.", "VersionFlow", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(entry.ReportPath) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task BrowseManualAfterEffectsAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Locate AfterFX.exe",
            Filter = "After Effects Executable (AfterFX.exe)|AfterFX.exe"
        };
        if (dlg.ShowDialog() != true) return;
        var added = await _adobeService.ValidateAndRegisterManualPathAsync(dlg.FileName);
        if (added is null)
        {
            MessageBox.Show("Selected file is not a valid AfterFX.exe installation.", "VersionFlow", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RefreshAdobeInstallationsAsync();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var s = _settingsService.Current;
        s.Theme = SelectedTheme;
        s.DefaultTargetVersionId = SettingDefaultTargetId;
        s.OutputNamingTemplate = SettingOutputTemplate;
        s.DefaultConversionMode = SettingDefaultMode;
        s.BatchConcurrency = Math.Clamp(SettingBatchConcurrency, 1, 4);
        s.RetainIntermediateFiles = SettingRetainIntermediates;
        s.GenerateReports = SettingGenerateReports;
        s.AllowAdobeAutomation = SettingAllowAdobeAutomation;
        s.VerifyWithInstalledAfterEffects = SettingVerifyWithAe;
        s.EnableLocalHistory = SettingEnableHistory;
        s.ShowAdvancedBinaryInspector = SettingShowBinaryInspector;
        await _settingsService.SaveAsync(s);
        UpdateOutputPreview();
        StatusBarText = "Settings saved locally";
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        var s = await _settingsService.ResetAsync();
        SelectedTheme = s.Theme;
        SettingDefaultTargetId = s.DefaultTargetVersionId;
        SettingOutputTemplate = s.OutputNamingTemplate;
        SettingDefaultMode = s.DefaultConversionMode;
        SettingBatchConcurrency = s.BatchConcurrency;
        SettingRetainIntermediates = s.RetainIntermediateFiles;
        SettingGenerateReports = s.GenerateReports;
        SettingAllowAdobeAutomation = s.AllowAdobeAutomation;
        SettingVerifyWithAe = s.VerifyWithInstalledAfterEffects;
        SettingEnableHistory = s.EnableLocalHistory;
        SettingShowBinaryInspector = s.ShowAdvancedBinaryInspector;
        StatusBarText = "Settings reset to defaults";
    }

    [RelayCommand]
    private void ExportDiagnostics()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Diagnostic Bundle",
            FileName = $"VersionFlow-Diagnostics-{DateTime.Now:yyyyMMdd}.zip",
            Filter = "ZIP Archive (*.zip)|*.zip"
        };
        if (dlg.ShowDialog() != true) return;
        _logger.ExportDiagnosticsZip(dlg.FileName, SettingRedactPathsOnExport);
        StatusBarText = $"Diagnostics exported to {Path.GetFileName(dlg.FileName)}";
    }

    [RelayCommand]
    private void ClearTempData()
    {
        try
        {
            if (Directory.Exists(AppPaths.TempDir))
            {
                Directory.Delete(AppPaths.TempDir, recursive: true);
                Directory.CreateDirectory(AppPaths.TempDir);
            }
            StatusBarText = "Temporary workspace cleared";
        }
        catch { }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F2} MB"
    };
}
