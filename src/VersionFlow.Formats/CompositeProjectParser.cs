using System.Buffers.Binary;
using System.Text;
using VersionFlow.Core.Errors;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;
using VersionFlow.Formats.Aepx;
using VersionFlow.Formats.Rifx;

namespace VersionFlow.Formats;

public sealed class CompositeProjectParser : IProjectParser
{
    private readonly RifxParser _rifxParser = new();
    private readonly AepxParser _aepxParser = new();
    private readonly IVersionDetector _versionDetector;

    public CompositeProjectParser(IVersionDetector versionDetector)
    {
        _versionDetector = versionDetector;
    }

    public bool CanHandleExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".aep", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".aepx", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParsedProjectDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new VersionFlowException(ErrorCodes.SourceUnreadable, $"Cannot access project file '{filePath}'.");

        var fi = new FileInfo(filePath);
        if (fi.Length == 0)
            throw new VersionFlowException(ErrorCodes.InvalidSignature, "The selected file is empty (0 bytes).");

        // Sniff actual file contents rather than trusting extension alone
        var sniff = new byte[Math.Min(64, (int)fi.Length)];
        await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        {
            await fs.ReadExactlyAsync(sniff, cancellationToken);
        }

        string asciiPrefix = Encoding.ASCII.GetString(sniff, 0, Math.Min(12, sniff.Length));
        string textPrefix = Encoding.UTF8.GetString(sniff).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        ParsedProjectDocument doc;
        if (asciiPrefix.StartsWith("RIFX", StringComparison.Ordinal))
        {
            if (Path.GetExtension(filePath).Equals(".aepx", StringComparison.OrdinalIgnoreCase))
            {
                // Content is actually binary RIFX even though extension says .aepx
                doc = await _rifxParser.ParseAsync(filePath, cancellationToken);
                doc.StructuralWarnings.Add("File extension is .aepx, but file contents are binary RIFX (.aep).");
            }
            else
            {
                doc = await _rifxParser.ParseAsync(filePath, cancellationToken);
            }
            AepProjectExtractor.PopulateFromBinaryTree(doc);
        }
        else if (textPrefix.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
              || textPrefix.StartsWith("<AfterEffectsProject", StringComparison.OrdinalIgnoreCase))
        {
            doc = await _aepxParser.ParseAsync(filePath, cancellationToken);
            if (Path.GetExtension(filePath).Equals(".aep", StringComparison.OrdinalIgnoreCase))
                doc.StructuralWarnings.Add("File extension is .aep, but file contents are XML (.aepx).");
        }
        else
        {
            throw new VersionFlowException(
                ErrorCodes.InvalidSignature,
                "Unrecognized project file signature. File is neither a valid RIFX (.aep) nor an AfterEffectsProject XML (.aepx) file.",
                ["Ensure you selected an uncompressed After Effects .aep or .aepx project file."]);
        }

        var detection = _versionDetector.Detect(doc);
        return new ParsedProjectDocument
        {
            SourcePath = doc.SourcePath,
            FileSizeBytes = doc.FileSizeBytes,
            FormatKind = doc.FormatKind,
            DeclaredRifxSize = doc.DeclaredRifxSize,
            FormType = doc.FormType,
            TrailingBytes = doc.TrailingBytes,
            RawXmlContent = doc.RawXmlContent,
            XmlEncodingName = doc.XmlEncodingName,
            Summary = doc.Summary,
            UnknownChunkCount = doc.UnknownChunkCount,
            UnknownChunkBytes = doc.UnknownChunkBytes,
            VersionDetection = detection
        }.Also(copy =>
        {
            copy.RootChunks.AddRange(doc.RootChunks);
            copy.Folders.AddRange(doc.Folders);
            copy.Compositions.AddRange(doc.Compositions);
            copy.FootageItems.AddRange(doc.FootageItems);
            copy.EffectsUsed.AddRange(doc.EffectsUsed);
            copy.ExpressionsUsed.AddRange(doc.ExpressionsUsed);
            copy.StructuralWarnings.AddRange(doc.StructuralWarnings);
        });
    }
}

internal static class DocExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}

public static class BinaryInspector
{
    public static string FormatChunkHierarchy(ParsedProjectDocument doc, int maxNodes = 2500)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File: {doc.SourcePath}");
        sb.AppendLine($"Format: {doc.FormatKind} | Size: {doc.FileSizeBytes:N0} bytes | Version: {doc.VersionDetection.DisplayLabel}");
        if (doc.FormatKind == ProjectFormatKind.AepBinary)
        {
            sb.AppendLine($"RIFX Declared Size: {doc.DeclaredRifxSize:N0} | Form: {doc.FormType} | Trailing: {doc.TrailingBytes.Length} bytes");
            int count = 0;
            foreach (var root in doc.RootChunks)
            {
                AppendNode(sb, root, 0, ref count, maxNodes);
                if (count >= maxNodes) break;
            }
            if (count >= maxNodes)
                sb.AppendLine($"... (output truncated at {maxNodes} chunks)");
        }
        else
        {
            sb.AppendLine($"AEPX XML length: {doc.RawXmlContent?.Length ?? 0:N0} chars");
        }
        return sb.ToString();
    }

    private static void AppendNode(StringBuilder sb, ProjectChunkNode node, int depth, ref int count, int maxNodes)
    {
        if (count++ >= maxNodes) return;
        string indent = new(' ', depth * 2);
        string status = node.IsKnownChunk ? "" : " [UNKNOWN]";
        string preview = node.Summary is not null ? $" | {node.Summary}" : string.Empty;
        sb.AppendLine($"{indent}{node.DisplayTag} @0x{node.SourceOffset:X} ({node.DeclaredLength} B){status}{preview}");
        foreach (var child in node.Children)
        {
            AppendNode(sb, child, depth + 1, ref count, maxNodes);
            if (count >= maxNodes) return;
        }
    }

    public static string FormatHexDump(byte[] bytes, int maxBytes = 256)
    {
        if (bytes.Length == 0) return "(empty)";
        int len = Math.Min(bytes.Length, maxBytes);
        var sb = new StringBuilder();
        for (int i = 0; i < len; i += 16)
        {
            sb.Append($"{i:X4}: ");
            for (int j = 0; j < 16; j++)
            {
                if (i + j < len) sb.Append($"{bytes[i + j]:X2} ");
                else sb.Append("   ");
            }
            sb.Append(" | ");
            for (int j = 0; j < 16 && i + j < len; j++)
            {
                byte b = bytes[i + j];
                sb.Append(b is >= 32 and <= 126 ? (char)b : '.');
            }
            sb.AppendLine();
        }
        if (bytes.Length > maxBytes)
            sb.AppendLine($"... ({bytes.Length - maxBytes} more bytes)");
        return sb.ToString();
    }
}
