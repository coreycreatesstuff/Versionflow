using System.Collections.ObjectModel;
using VersionFlow.Core.Models;
using VersionFlow.Formats;

namespace VersionFlow.App.ViewModels;

public sealed class InspectorTreeNode
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? IdentifierToCopy { get; init; }
    public ObservableCollection<InspectorTreeNode> Children { get; } = [];
}

public sealed class ChunkTreeItem
{
    public required ProjectChunkNode Node { get; init; }
    public string Title => $"{Node.DisplayTag}  @0x{Node.SourceOffset:X}  ({Node.DeclaredLength:N0} B)" + (Node.IsKnownChunk ? "" : "  [UNKNOWN]");
    public string? Preview => Node.Summary;
    public string HexDump => Node.IsList
        ? $"LIST container '{Node.ListType}' with {Node.Children.Count} direct child chunk(s)."
        : BinaryInspector.FormatHexDump(Node.RawPayload, 512);
    public ObservableCollection<ChunkTreeItem> Children { get; } = [];

    public static ChunkTreeItem FromNode(ProjectChunkNode node, int maxChildren = 500)
    {
        var item = new ChunkTreeItem { Node = node };
        foreach (var child in node.Children.Take(maxChildren))
        {
            item.Children.Add(FromNode(child, maxChildren));
        }
        return item;
    }
}

public sealed class ApprovalFindingItem
{
    public required CompatibilityFinding Finding { get; init; }
    public bool IsApproved { get; set; } = true;
}

public sealed class BatchJobViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string JobId { get; init; }
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }

    private string _sourceVersionLabel = "Detecting...";
    public string SourceVersionLabel { get => _sourceVersionLabel; set => SetProperty(ref _sourceVersionLabel, value); }

    private AfterEffectsVersionInfo _targetVersion = null!;
    public AfterEffectsVersionInfo TargetVersion { get => _targetVersion; set => SetProperty(ref _targetVersion, value); }

    private ConversionMode _mode = ConversionMode.Compatibility;
    public ConversionMode Mode { get => _mode; set => SetProperty(ref _mode, value); }

    private string? _outputDirectory;
    public string? OutputDirectory { get => _outputDirectory; set => SetProperty(ref _outputDirectory, value); }

    private string _compatibilitySummary = "Pending";
    public string CompatibilitySummary { get => _compatibilitySummary; set => SetProperty(ref _compatibilitySummary, value); }

    private string _methodLabel = "Auto";
    public string MethodLabel { get => _methodLabel; set => SetProperty(ref _methodLabel, value); }

    private ConversionJobState _state = ConversionJobState.Queued;
    public ConversionJobState State { get => _state; set => SetProperty(ref _state, value); }

    private double? _progressRatio;
    public double? ProgressRatio { get => _progressRatio; set => SetProperty(ref _progressRatio, value); }

    private string? _outputPath;
    public string? OutputPath { get => _outputPath; set => SetProperty(ref _outputPath, value); }

    private string? _statusMessage;
    public string? StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public CancellationTokenSource? JobCts { get; set; }
}
