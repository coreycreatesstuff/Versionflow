namespace VersionFlow.Core.Models;

public sealed class ProjectChunkNode
{
    public required string Id { get; init; }
    public string? ListType { get; init; }
    public bool IsList => ListType is not null;
    public bool IsOpaqueList => IsList && Children.Count == 0 && RawPayload.Length > 0;
    public long SourceOffset { get; init; }
    public uint DeclaredLength { get; init; }
    public int PaddingByte { get; init; }
    public byte[] RawPayload { get; set; } = [];
    public List<ProjectChunkNode> Children { get; } = [];
    public ProjectChunkNode? Parent { get; set; }
    public string? Summary { get; set; }
    public bool IsKnownChunk { get; set; }

    public string DisplayTag => IsList ? $"LIST:{ListType}" : Id;

    public IEnumerable<ProjectChunkNode> WalkDescendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var grand in child.WalkDescendants())
                yield return grand;
        }
    }
}

public sealed record AepxXmlNodeInfo
{
    public required string ElementName { get; init; }
    public string? BdataHex { get; init; }
    public string? TextPreview { get; init; }
    public int Depth { get; init; }
}

public sealed class ParsedProjectDocument
{
    public required string SourcePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required ProjectFormatKind FormatKind { get; init; }
    public required VersionDetectionResult VersionDetection { get; init; }

    // Binary RIFX fields
    public uint DeclaredRifxSize { get; init; }
    public string? FormType { get; init; }
    public byte[] TrailingBytes { get; init; } = [];
    public List<ProjectChunkNode> RootChunks { get; } = [];

    // AEPX XML raw document string (for lossless XML round-tripping)
    public string? RawXmlContent { get; set; }
    public string? XmlEncodingName { get; set; }

    // Semantic extracted hierarchy
    public ProjectSummary Summary { get; set; } = new();
    public List<ProjectFolderModel> Folders { get; } = [];
    public List<CompositionModel> Compositions { get; } = [];
    public List<FootageItemModel> FootageItems { get; } = [];
    public List<EffectUsageModel> EffectsUsed { get; } = [];
    public List<ExpressionUsageModel> ExpressionsUsed { get; } = [];
    public List<string> StructuralWarnings { get; } = [];
    public int UnknownChunkCount { get; set; }
    public long UnknownChunkBytes { get; set; }
}

public sealed class ProjectSummary
{
    public int? FormatMajorVersion { get; set; }
    public int? FormatMinorVersion { get; set; }
    public uint? SvapWord { get; set; }
    public int? BitsPerChannel { get; set; }
    public double? AudioSampleRateHz { get; set; }
    public int? CompositionCount { get; set; }
    public int? LayerCount { get; set; }
    public int? FootageCount { get; set; }
    public int? FolderCount { get; set; }
    public int? EffectReferenceCount { get; set; }
    public int? ExpressionCount { get; set; }
    public string? WorkingColorSpaceName { get; set; }
    public string? ExpressionEngine { get; set; }
    public bool HasRenderQueueItems { get; set; }
    public int RenderQueueItemCount { get; set; }
}

public sealed class ProjectFolderModel
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "Unnamed Folder";
    public int ParentFolderId { get; set; }
    public long SourceOffset { get; set; }
}

public sealed class CompositionModel
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "Composition";
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public double DurationSeconds { get; set; }
    public string? RendererMatchName { get; set; }
    public string? RendererDisplayName { get; set; }
    public List<LayerModel> Layers { get; } = [];
    public long SourceOffset { get; set; }
}

public sealed class LayerModel
{
    public int LayerId { get; set; }
    public int Index { get; set; }
    public string Name { get; set; } = "Layer";
    public string LayerType { get; set; } = "Unknown";
    public int SourceItemId { get; set; }
    public int ParentLayerId { get; set; }
    public int? TrackMatteLayerId { get; set; }
    public int TrackMatteMode { get; set; }
    public bool Is3D { get; set; }
    public bool HasTimeRemapping { get; set; }
    public int KeyframeStreamCount { get; set; }
    public List<EffectUsageModel> Effects { get; } = [];
    public List<ExpressionUsageModel> Expressions { get; } = [];
    public List<string> PropertyGroupNames { get; } = [];
    public long SourceOffset { get; set; }
}

public sealed class FootageItemModel
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "Footage";
    public string Kind { get; set; } = "Unknown";
    public string? FilePath { get; set; }
    public bool? FileExistsOnDisk { get; set; }
    public bool IsImageSequence { get; set; }
    public bool IsNetworkPath { get; set; }
    public long SourceOffset { get; set; }
}

public sealed class EffectUsageModel
{
    public required string MatchName { get; init; }
    public string? DisplayName { get; init; }
    public string? Location { get; init; }
    public bool IsBuiltInAdobe => MatchName.StartsWith("ADBE ", StringComparison.Ordinal);
    public bool IsThirdParty => !IsBuiltInAdobe && !string.IsNullOrWhiteSpace(MatchName);
}

public sealed class ExpressionUsageModel
{
    public required string Location { get; init; }
    public required string ExpressionText { get; init; }
    public int LineCount => ExpressionText.Split('\n').Length;
}
