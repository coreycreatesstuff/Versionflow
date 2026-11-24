using System.Buffers.Binary;
using System.Text;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;

namespace VersionFlow.Formats.Rifx;

public sealed class RifxWriter : IProjectWriter
{
    public async Task WriteAsync(ParsedProjectDocument document, string outputPath, CancellationToken cancellationToken = default)
    {
        if (document.FormatKind != ProjectFormatKind.AepBinary)
            throw new InvalidOperationException("RifxWriter only supports binary .aep documents.");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Compute total size recursively so every LIST and RIFX length header stays valid
        long bodySize = 4; // "Egg!"
        foreach (var chunk in document.RootChunks)
        {
            bodySize += ComputeSerializedSizeWithPad(chunk);
        }

        if (bodySize > uint.MaxValue)
            throw new InvalidOperationException("Serialized RIFX container exceeds 4 GiB limit.");

        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        using var ms = new MemoryStream(capacity: (int)Math.Min(bodySize + 8, 16 * 1024 * 1024));

        var header = new byte[12];
        Encoding.ASCII.GetBytes("RIFX", header.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)bodySize);
        Encoding.ASCII.GetBytes(document.FormType ?? "Egg!", header.AsSpan(8, 4));
        await fs.WriteAsync(header, cancellationToken);

        foreach (var chunk in document.RootChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteChunkAsync(fs, chunk, cancellationToken);
        }

        if (document.TrailingBytes.Length > 0)
        {
            await fs.WriteAsync(document.TrailingBytes, cancellationToken);
        }

        await fs.FlushAsync(cancellationToken);
    }

    public static uint ComputePayloadLength(ProjectChunkNode node)
    {
        if (!node.IsList)
            return (uint)node.RawPayload.Length;

        if (node.IsOpaqueList)
            return checked((uint)(4L + node.RawPayload.Length));

        long total = 4; // list_type
        foreach (var child in node.Children)
        {
            total += ComputeSerializedSizeWithPad(child);
        }
        return checked((uint)total);
    }

    private static long ComputeSerializedSizeWithPad(ProjectChunkNode node)
    {
        uint payloadLen = ComputePayloadLength(node);
        long pad = payloadLen % 2;
        return 8L + payloadLen + pad;
    }

    private static async Task WriteChunkAsync(Stream stream, ProjectChunkNode node, CancellationToken cancellationToken)
    {
        uint payloadLen = ComputePayloadLength(node);
        var chunkHeader = new byte[8];
        Encoding.ASCII.GetBytes(node.Id, chunkHeader.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32BigEndian(chunkHeader.AsSpan(4, 4), payloadLen);
        await stream.WriteAsync(chunkHeader, cancellationToken);

        if (node.IsList)
        {
            var listTypeBytes = Encoding.ASCII.GetBytes(node.ListType!);
            await stream.WriteAsync(listTypeBytes, cancellationToken);
            if (node.IsOpaqueList)
            {
                if (node.RawPayload.Length > 0)
                    await stream.WriteAsync(node.RawPayload, cancellationToken);
            }
            else
            {
                foreach (var child in node.Children)
                {
                    await WriteChunkAsync(stream, child, cancellationToken);
                }
            }
        }
        else if (node.RawPayload.Length > 0)
        {
            await stream.WriteAsync(node.RawPayload, cancellationToken);
        }

        if (payloadLen % 2 == 1)
        {
            byte pad = node.PaddingByte >= 0 ? (byte)node.PaddingByte : (byte)0;
            await stream.WriteAsync(new[] { pad }, cancellationToken);
        }
    }
}
