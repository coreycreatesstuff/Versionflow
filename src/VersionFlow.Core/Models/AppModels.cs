namespace VersionFlow.Core.Models;

public sealed record AdobeInstallationInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required int AppMajorVersion { get; init; }
    public required string FullVersion { get; init; }
    public required string ExecutablePath { get; init; }
    public string? SupportFilesDirectory { get; init; }
    public bool IsManuallyConfigured { get; init; }
    public bool IsAvailable { get; init; }
}

public sealed record ConversionHistoryEntry
{
    public required string JobId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string SourceFileName { get; init; }
    public required string SourcePath { get; init; }
    public required string SourceVersionLabel { get; init; }
    public required string TargetVersionLabel { get; init; }
    public required string OutputFileName { get; init; }
    public required string OutputPath { get; init; }
    public required ConversionMethod Method { get; init; }
    public required ConversionJobState ResultState { get; init; }
    public required TimeSpan Duration { get; init; }
    public required VerificationLevel ValidationLevel { get; init; }
    public int WarningCount { get; init; }
    public string? ReportPath { get; init; }
}

public sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; } = 2;
    public string Theme { get; set; } = "Dark"; // Dark, Light, System
    public string DefaultTargetVersionId { get; set; } = "ae-2020";
    public string? DefaultOutputDirectory { get; set; }
    public string OutputNamingTemplate { get; set; } = "{name}_AE{year}";
    public bool RememberPreviousSelections { get; set; } = true;
    public bool ShowCompletionNotifications { get; set; } = true;
    public ConversionMode DefaultConversionMode { get; set; } = ConversionMode.Compatibility;
    public ConversionMethod? PreferredMethod { get; set; }
    public int BatchConcurrency { get; set; } = 2;
    public bool RetainIntermediateFiles { get; set; }
    public bool AutomaticCompatibilityScanning { get; set; } = true;
    public bool GenerateReports { get; set; } = true;
    public bool InspectAssetsOnImport { get; set; } = true;
    public bool VerifyWithInstalledAfterEffects { get; set; }
    public bool AllowAdobeAutomation { get; set; }
    public bool EnableDiagnosticLogging { get; set; } = true;
    public bool EnableExperimentalFeatures { get; set; }
    public bool ShowAdvancedBinaryInspector { get; set; } = true;
    public bool EnableLocalHistory { get; set; } = true;
    public string? CustomTempDirectory { get; set; }
    public List<string> ManualAfterEffectsPaths { get; set; } = [];
    public double WindowWidth { get; set; } = 1140;
    public double WindowHeight { get; set; } = 780;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }
}

public sealed record AssetInspectionItem
{
    public required int ItemId { get; init; }
    public required string Name { get; init; }
    public required string OriginalPath { get; init; }
    public required string Status { get; init; } // Found, Missing, Network, Sequence, Virtual
    public string? ProposedRelinkPath { get; set; }
}

public sealed record BatchJobItem
{
    public required string JobId { get; init; }
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public string SourceVersionLabel { get; set; } = "Pending...";
    public required AfterEffectsVersionInfo TargetVersion { get; set; }
    public ConversionMode Mode { get; set; } = ConversionMode.Compatibility;
    public string? OutputDirectory { get; set; }
    public string CompatibilitySummary { get; set; } = "Pending";
    public string MethodLabel { get; set; } = "Auto";
    public ConversionJobState State { get; set; } = ConversionJobState.Queued;
    public double? ProgressRatio { get; set; }
    public string? OutputPath { get; set; }
    public string? StatusMessage { get; set; }
}
