using System.Buffers.Binary;
using System.Text;
using VersionFlow.Core.Errors;
using VersionFlow.Core.Models;

namespace VersionFlow.Formats.Rifx;

public sealed class RifxParser
{
    public const long MaxFileSizeBytes = 1024L * 1024 * 1024; // 1 GiB
    public const int MaxNestingDepth = 64;
    public const int MaxChunkCount = 2_000_000;

    private static readonly HashSet<string> OpaqueListTypes = new(StringComparer.Ordinal)
    {
        // CoolType / COS text-keyframe container whose body after the 4-byte listType is raw COS text data, not RIFX chunks
        "btdk"
    };

    private static readonly HashSet<string> KnownChunkIds = new(StringComparer.Ordinal)
    {
        "RIFX", "LIST", "svap", "head", "nhed", "nnhd", "adfr",
        "Pefl", "pjef", "EfdG", "EfDC", "EfDf", "tdmn", "sspc", "fnam",
        "parT", "parn", "pard", "pdnm", "tdgp", "tdsb", "tdsn", "tdbs",
        "tdb4", "cdat", "tdpi", "tdps", "tdum", "tduM", "pgui", "list",
        "lhd3", "ldat", "Utf8", "engv", "CPPl", "pprf", "cpid", "dwga",
        "pcms", "PwCs", "pdvc", "ExEn", "Fold", "fdta", "Item", "iide",
        "idpc", "idpi", "idta", "cdta", "PRin", "prin", "prda", "Layr",
        "ldta", "Pin ", "opti", "alas", "cmta", "LRdr", "Rhed", "Rout",
        "LItm", "otln", "mkif", "CLRS", "mnfo", "strt", "drop", "ftgi",
        "btds", "btdk"
    };

    public async Task<ParsedProjectDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new VersionFlowException(ErrorCodes.SourceUnreadable, $"Project file does not exist: {filePath}");

        var fi = new FileInfo(filePath);
        if (fi.Length == 0)
            throw new VersionFlowException(ErrorCodes.InvalidSignature, "The project file is empty (0 bytes).");

        if (fi.Length < 12)
            throw new VersionFlowException(ErrorCodes.TruncatedStructure, $"The project file is too small ({fi.Length} bytes) to be a valid RIFX AEP project.");

        if (fi.Length > MaxFileSizeBytes)
            throw new VersionFlowException(ErrorCodes.ResourceLimitExceeded, $"Project file exceeds maximum supported size ({fi.Length} bytes).");

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var header = new byte[12];
        await fs.ReadExactlyAsync(header, cancellationToken);

        var magic = Encoding.ASCII.GetString(header, 0, 4);
        if (!string.Equals(magic, "RIFX", StringComparison.Ordinal))
        {
            throw new VersionFlowException(
                ErrorCodes.InvalidSignature,
                $"Invalid file signature '{SanitizeFourCc(magic)}'. Expected 'RIFX' for binary After Effects (.aep) files.",
                ["Verify the file is a genuine Adobe After Effects .aep project and not renamed from another format."]);
        }

        uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
        var formType = Encoding.ASCII.GetString(header, 8, 4);
        if (!string.Equals(formType, "Egg!", StringComparison.Ordinal))
        {
            throw new VersionFlowException(
                ErrorCodes.InvalidSignature,
                $"Unexpected RIFX form type '{SanitizeFourCc(formType)}'. Expected 'Egg!' for After Effects projects.");
        }

        long expectedRifxEnd = 8L + declaredSize;
        if (expectedRifxEnd > fi.Length)
        {
            throw new VersionFlowException(
                ErrorCodes.TruncatedStructure,
                $"Truncated RIFX container: header declares {expectedRifxEnd:N0} bytes, but file is {fi.Length:N0} bytes.",
                ["Re-export or recover the project from an After Effects auto-save backup."]);
        }

        // Read bounded RIFX payload + any trailing bytes
        var allBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var warnings = new List<string>();
        int chunkCounter = 0;

        var rootChunks = ParseChunkSequence(
            allBytes,
            offset: 12,
            endOffset: (int)expectedRifxEnd,
            parent: null,
            depth: 0,
            warnings: warnings,
            chunkCounter: ref chunkCounter,
            cancellationToken: cancellationToken);

        byte[] trailing = expectedRifxEnd < allBytes.Length
            ? allBytes[(int)expectedRifxEnd..]
            : [];

        var doc = new ParsedProjectDocument
        {
            SourcePath = filePath,
            FileSizeBytes = fi.Length,
            FormatKind = ProjectFormatKind.AepBinary,
            DeclaredRifxSize = declaredSize,
            FormType = formType,
            TrailingBytes = trailing,
            VersionDetection = new VersionDetectionResult
            {
                FormatKind = ProjectFormatKind.AepBinary,
                DisplayLabel = "Pending detection",
                Confidence = DetectionConfidence.Unknown,
                DetectionMethod = "Pending"
            }
        };

        doc.RootChunks.AddRange(rootChunks);
        doc.StructuralWarnings.AddRange(warnings);
        return doc;
    }

    private static List<ProjectChunkNode> ParseChunkSequence(
        byte[] buffer,
        int offset,
        int endOffset,
        ProjectChunkNode? parent,
        int depth,
        List<string> warnings,
        ref int chunkCounter,
        CancellationToken cancellationToken)
    {
        if (depth > MaxNestingDepth)
            throw new VersionFlowException(ErrorCodes.ResourceLimitExceeded, $"RIFX container nesting exceeded maximum depth ({MaxNestingDepth}).");

        var list = new List<ProjectChunkNode>();
        int pos = offset;

        while (pos + 8 <= endOffset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++chunkCounter > MaxChunkCount)
                throw new VersionFlowException(ErrorCodes.ResourceLimitExceeded, $"Chunk count exceeded maximum limit ({MaxChunkCount:N0}).");

            string id = Encoding.ASCII.GetString(buffer, pos, 4);
            uint len = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(pos + 4, 4));
            long payloadStart = pos + 8L;
            long payloadEnd = payloadStart + len;

            if (payloadEnd > endOffset)
            {
                throw new VersionFlowException(
                    ErrorCodes.TruncatedStructure,
                    $"Chunk '{SanitizeFourCc(id)}' at offset 0x{pos:X} declares length {len} bytes, exceeding parent container boundary (0x{endOffset:X}).");
            }

            int padding = (int)(len % 2);
            int padByte = -1;
            if (padding == 1 && payloadEnd < buffer.Length)
            {
                padByte = buffer[(int)payloadEnd];
            }

            if (string.Equals(id, "LIST", StringComparison.Ordinal))
            {
                if (len < 4)
                    throw new VersionFlowException(ErrorCodes.TruncatedStructure, $"LIST chunk at offset 0x{pos:X} has length {len} < 4.");

                string listType = Encoding.ASCII.GetString(buffer, (int)payloadStart, 4);
                if (OpaqueListTypes.Contains(listType))
                {
                    int bodyLen = (int)(len - 4);
                    var opaqueBytes = new byte[bodyLen];
                    if (bodyLen > 0)
                        Buffer.BlockCopy(buffer, (int)payloadStart + 4, opaqueBytes, 0, bodyLen);

                    list.Add(new ProjectChunkNode
                    {
                        Id = id,
                        ListType = listType,
                        SourceOffset = pos,
                        DeclaredLength = len,
                        PaddingByte = padByte,
                        RawPayload = opaqueBytes,
                        Parent = parent,
                        IsKnownChunk = true,
                        Summary = $"Opaque {listType} binary stream ({bodyLen:N0} B)"
                    });
                }
                else
                {
                    var node = new ProjectChunkNode
                    {
                        Id = id,
                        ListType = listType,
                        SourceOffset = pos,
                        DeclaredLength = len,
                        PaddingByte = padByte,
                        Parent = parent,
                        IsKnownChunk = KnownChunkIds.Contains(listType)
                    };

                    var children = ParseChunkSequence(
                        buffer,
                        offset: (int)payloadStart + 4,
                        endOffset: (int)payloadEnd,
                        parent: node,
                        depth: depth + 1,
                        warnings: warnings,
                        chunkCounter: ref chunkCounter,
                        cancellationToken: cancellationToken);

                    node.Children.AddRange(children);
                    list.Add(node);
                }
            }
            else
            {
                var payload = new byte[len];
                if (len > 0)
                    Buffer.BlockCopy(buffer, (int)payloadStart, payload, 0, (int)len);

                var node = new ProjectChunkNode
                {
                    Id = id,
                    SourceOffset = pos,
                    DeclaredLength = len,
                    PaddingByte = padByte,
                    RawPayload = payload,
                    Parent = parent,
                    IsKnownChunk = KnownChunkIds.Contains(id),
                    Summary = BuildChunkPreview(id, payload)
                };
                list.Add(node);
            }

            long nextPos = payloadEnd + padding;
            if (nextPos > endOffset && padding == 1 && payloadEnd == endOffset)
            {
                // Some exporters omit trailing pad on final container byte
                nextPos = endOffset;
            }
            pos = (int)nextPos;
        }

        if (pos != endOffset)
        {
            warnings.Add($"Trailing {endOffset - pos} unparsed byte(s) inside {(parent?.DisplayTag ?? "Egg!")} at 0x{pos:X}.");
        }

        return list;
    }

    private static string? BuildChunkPreview(string id, byte[] payload)
    {
        if (payload.Length == 0) return "0 bytes";
        return id switch
        {
            "head" when payload.Length >= 8 =>
                $"Format {BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(0, 2))}.{BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2, 2))}, svap=0x{BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4, 4)):X8}",
            "svap" when payload.Length >= 4 =>
                $"0x{BinaryPrimitives.ReadUInt32BigEndian(payload):X8}",
            "adfr" when payload.Length >= 8 =>
                $"{BinaryPrimitives.ReadDoubleBigEndian(payload):F0} Hz",
            "pjef" or "tdmn" =>
                Encoding.ASCII.GetString(payload).TrimEnd('\0'),
            "Utf8" =>
                Truncate(Encoding.UTF8.GetString(payload).Replace("\r", " ").Replace("\n", " "), 80),
            "fnam" or "tdsn" or "pdnm" when payload.Length > 8 && Encoding.ASCII.GetString(payload, 0, 4) == "Utf8" =>
                Truncate(Encoding.UTF8.GetString(payload, 8, Math.Min(payload.Length - 8, (int)BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4, 4)))), 80),
            _ => null
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "…");

    private static string SanitizeFourCc(string s) =>
        new(s.Select(c => char.IsControl(c) ? '?' : c).ToArray());
}
