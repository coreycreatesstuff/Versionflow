using System.Buffers.Binary;
using System.Text;
using VersionFlow.Core.Models;

namespace VersionFlow.Formats.Rifx;

public static class AepProjectExtractor
{
    public static void PopulateFromBinaryTree(ParsedProjectDocument doc)
    {
        var summary = new ProjectSummary();
        int layerCount = 0;
        int unknownCount = 0;
        long unknownBytes = 0;

        foreach (var node in doc.RootChunks)
        {
            switch (node.Id)
            {
                case "head" when node.RawPayload.Length >= 8:
                    summary.FormatMajorVersion = BinaryPrimitives.ReadUInt16BigEndian(node.RawPayload.AsSpan(0, 2));
                    summary.FormatMinorVersion = BinaryPrimitives.ReadUInt16BigEndian(node.RawPayload.AsSpan(2, 2));
                    summary.SvapWord = BinaryPrimitives.ReadUInt32BigEndian(node.RawPayload.AsSpan(4, 4));
                    break;
                case "svap" when node.RawPayload.Length >= 4:
                    summary.SvapWord ??= BinaryPrimitives.ReadUInt32BigEndian(node.RawPayload.AsSpan(0, 4));
                    break;
                case "nhed" when node.RawPayload.Length >= 16:
                    summary.BitsPerChannel = node.RawPayload[15] switch
                    {
                        0 => 8,
                        1 => 16,
                        2 => 32,
                        _ => null
                    };
                    break;
                case "adfr" when node.RawPayload.Length >= 8:
                    summary.AudioSampleRateHz = BinaryPrimitives.ReadDoubleBigEndian(node.RawPayload.AsSpan(0, 8));
                    break;
                case "LIST" when node.ListType == "Pefl":
                    foreach (var child in node.Children.Where(c => c.Id == "pjef"))
                    {
                        var matchName = Encoding.ASCII.GetString(child.RawPayload).TrimEnd('\0');
                        if (!string.IsNullOrWhiteSpace(matchName))
                        {
                            doc.EffectsUsed.Add(new EffectUsageModel
                            {
                                MatchName = matchName,
                                Location = "Project Effect List (Pefl)"
                            });
                        }
                    }
                    break;
                case "LIST" when node.ListType == "Fold":
                    WalkFolder(node, parentFolderId: 0, doc, ref layerCount);
                    break;
                case "LIST" when node.ListType == "LRdr":
                    var rqItems = node.Children.Count(c => c.IsList && c.ListType == "LItm");
                    summary.HasRenderQueueItems = rqItems > 0;
                    summary.RenderQueueItemCount = rqItems;
                    break;
                case "LIST" when node.ListType == "ExEn":
                    var exUtf = node.Children.FirstOrDefault(c => c.Id == "Utf8");
                    if (exUtf is not null)
                        summary.ExpressionEngine = Encoding.UTF8.GetString(exUtf.RawPayload).TrimEnd('\0');
                    break;
            }
        }

        // Track unknown chunks & color profile
        foreach (var node in doc.RootChunks.SelectMany(r => new[] { r }.Concat(r.WalkDescendants())))
        {
            if (!node.IsKnownChunk)
            {
                unknownCount++;
                unknownBytes += node.RawPayload.Length;
            }
            if (node.Id == "pprf" && node.RawPayload.Length >= 64)
            {
                summary.WorkingColorSpaceName = ExtractIccDescriptionHint(node.RawPayload);
            }
        }

        summary.CompositionCount = doc.Compositions.Count;
        summary.LayerCount = layerCount;
        summary.FootageCount = doc.FootageItems.Count;
        summary.FolderCount = doc.Folders.Count;
        summary.EffectReferenceCount = doc.EffectsUsed.Count;
        summary.ExpressionCount = doc.ExpressionsUsed.Count;

        doc.Summary = summary;
        doc.UnknownChunkCount = unknownCount;
        doc.UnknownChunkBytes = unknownBytes;
    }

    private static void WalkFolder(ProjectChunkNode foldNode, int parentFolderId, ParsedProjectDocument doc, ref int totalLayers)
    {
        foreach (var child in foldNode.Children)
        {
            if (!child.IsList || child.ListType != "Item")
                continue;

            var idta = child.Children.FirstOrDefault(c => c.Id == "idta");
            ushort itemType = idta is { RawPayload.Length: >= 2 }
                ? BinaryPrimitives.ReadUInt16BigEndian(idta.RawPayload.AsSpan(0, 2))
                : (ushort)0;
            int itemId = idta is { RawPayload.Length: >= 20 }
                ? (int)BinaryPrimitives.ReadUInt32BigEndian(idta.RawPayload.AsSpan(16, 4))
                : 0;

            var nameChunk = child.Children.FirstOrDefault(c => c.Id == "Utf8");
            string itemName = nameChunk is not null
                ? Encoding.UTF8.GetString(nameChunk.RawPayload).TrimEnd('\0')
                : $"Item #{itemId}";
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = $"Item #{itemId}";

            switch (itemType)
            {
                case 1: // Folder
                    doc.Folders.Add(new ProjectFolderModel
                    {
                        ItemId = itemId,
                        Name = itemName,
                        ParentFolderId = parentFolderId,
                        SourceOffset = child.SourceOffset
                    });
                    var subFold = child.Children.FirstOrDefault(c => c.IsList && c.ListType == "Fold");
                    if (subFold is not null)
                        WalkFolder(subFold, itemId, doc, ref totalLayers);
                    break;

                case 4: // Composition
                    var comp = ExtractComposition(child, itemId, itemName, doc, ref totalLayers);
                    doc.Compositions.Add(comp);
                    break;

                case 7: // Footage / Solid
                    var footage = ExtractFootage(child, itemId, itemName);
                    doc.FootageItems.Add(footage);
                    break;
            }
        }
    }

    private static CompositionModel ExtractComposition(ProjectChunkNode itemNode, int itemId, string name, ParsedProjectDocument doc, ref int totalLayers)
    {
        int width = 0;
        int height = 0;
        double fps = 0;
        double duration = 0;

        var cdta = itemNode.Children.FirstOrDefault(c => c.Id == "cdta");
        if (cdta is { RawPayload.Length: >= 144 })
        {
            // Verified in AE 2024 secret.aep: width@140 (u16 BE), height@142 (u16 BE)
            width = BinaryPrimitives.ReadUInt16BigEndian(cdta.RawPayload.AsSpan(140, 2));
            height = BinaryPrimitives.ReadUInt16BigEndian(cdta.RawPayload.AsSpan(142, 2));
            uint timeScale = BinaryPrimitives.ReadUInt32BigEndian(cdta.RawPayload.AsSpan(16, 4));
            if (timeScale > 0)
            {
                uint durTicks = BinaryPrimitives.ReadUInt32BigEndian(cdta.RawPayload.AsSpan(24, 4));
                if (durTicks > 0 && durTicks < uint.MaxValue)
                    duration = (double)durTicks / timeScale;
            }
            if (cdta.RawPayload.Length >= 152)
            {
                uint fpsFixed = BinaryPrimitives.ReadUInt16BigEndian(cdta.RawPayload.AsSpan(148, 2));
                if (fpsFixed is > 0 and <= 240)
                    fps = fpsFixed;
            }
        }

        string? rendererMatch = null;
        string? rendererDisplay = null;
        var prinList = itemNode.Children.FirstOrDefault(c => c.IsList && c.ListType == "PRin");
        var prinChunk = prinList?.Children.FirstOrDefault(c => c.Id == "prin");
        if (prinChunk is { RawPayload.Length: >= 60 })
        {
            rendererMatch = ReadAsciiZeroTerminated(prinChunk.RawPayload, 4, 48);
            rendererDisplay = ReadAsciiZeroTerminated(prinChunk.RawPayload, 52, Math.Min(48, prinChunk.RawPayload.Length - 52));
        }

        var comp = new CompositionModel
        {
            ItemId = itemId,
            Name = name,
            Width = width,
            Height = height,
            FrameRate = fps,
            DurationSeconds = duration,
            RendererMatchName = rendererMatch,
            RendererDisplayName = rendererDisplay,
            SourceOffset = itemNode.SourceOffset
        };

        int layerIdx = 1;
        foreach (var child in itemNode.Children.Where(c => c.IsList && c.ListType == "Layr"))
        {
            var layer = ExtractLayer(child, layerIdx++, name, doc);
            comp.Layers.Add(layer);
            totalLayers++;
        }

        return comp;
    }

    private static LayerModel ExtractLayer(ProjectChunkNode layrNode, int index, string compName, ParsedProjectDocument doc)
    {
        var ldta = layrNode.Children.FirstOrDefault(c => c.Id == "ldta");
        int layerId = ldta is { RawPayload.Length: >= 4 }
            ? (int)BinaryPrimitives.ReadUInt32BigEndian(ldta.RawPayload.AsSpan(0, 4))
            : 0;
        int sourceItemId = ldta is { RawPayload.Length: >= 44 }
            ? (int)BinaryPrimitives.ReadUInt32BigEndian(ldta.RawPayload.AsSpan(40, 4))
            : 0;

        // Track matte detection in ldta (format 94+ stores track matte target layer ID vs legacy adjacent mode)
        int? trackMatteLayerId = null;
        int trackMatteMode = 0;
        bool is3D = false;
        if (ldta is { RawPayload.Length: >= 120 })
        {
            // Bit flags in ldta[48..51] / ldta[104..119] vary by version; record conservative flags
            uint flags = BinaryPrimitives.ReadUInt32BigEndian(ldta.RawPayload.AsSpan(48, 4));
            is3D = (flags & 0x00000004) != 0;
        }

        var utfName = layrNode.Children.FirstOrDefault(c => c.Id == "Utf8");
        string layerName = utfName is not null
            ? Encoding.UTF8.GetString(utfName.RawPayload).TrimEnd('\0')
            : string.Empty;
        if (string.IsNullOrWhiteSpace(layerName))
            layerName = $"Layer {index}";

        var layer = new LayerModel
        {
            LayerId = layerId,
            Index = index,
            Name = layerName,
            LayerType = InferLayerType(layrNode, is3D),
            SourceItemId = sourceItemId,
            TrackMatteLayerId = trackMatteLayerId,
            TrackMatteMode = trackMatteMode,
            Is3D = is3D,
            SourceOffset = layrNode.SourceOffset
        };

        // Scan descendants for expressions (Utf8 inside LIST:tdbs) and applied effects (tdmn inside LIST:EfDf)
        foreach (var desc in layrNode.WalkDescendants())
        {
            if (desc.IsList && desc.ListType == "tdbs")
            {
                layer.KeyframeStreamCount++;
                var exprChunk = desc.Children.FirstOrDefault(c => c.Id == "Utf8");
                if (exprChunk is { RawPayload.Length: > 0 })
                {
                    var exprText = Encoding.UTF8.GetString(exprChunk.RawPayload).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(exprText))
                    {
                        var expr = new ExpressionUsageModel
                        {
                            Location = $"{compName} > {layerName}",
                            ExpressionText = exprText
                        };
                        layer.Expressions.Add(expr);
                        doc.ExpressionsUsed.Add(expr);
                    }
                }
            }
            else if (desc.IsList && desc.ListType == "EfDf")
            {
                var tdmn = desc.Children.FirstOrDefault(c => c.Id == "tdmn");
                if (tdmn is not null)
                {
                    var matchName = Encoding.ASCII.GetString(tdmn.RawPayload).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(matchName))
                    {
                        var fx = new EffectUsageModel
                        {
                            MatchName = matchName,
                            Location = $"{compName} > {layerName}"
                        };
                        layer.Effects.Add(fx);
                        if (!doc.EffectsUsed.Any(e => e.MatchName == matchName && e.Location == fx.Location))
                            doc.EffectsUsed.Add(fx);
                    }
                }
            }
            else if (desc.Id == "tdmn" && desc.Parent?.ListType == "tdgp")
            {
                var groupName = Encoding.ASCII.GetString(desc.RawPayload).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(groupName) && layer.PropertyGroupNames.Count < 32)
                    layer.PropertyGroupNames.Add(groupName);
            }
        }

        return layer;
    }

    private static FootageItemModel ExtractFootage(ProjectChunkNode itemNode, int itemId, string name)
    {
        string kind = "Footage";
        string? filePath = null;

        var pinNode = itemNode.Children.FirstOrDefault(c => c.IsList && c.ListType == "Pin ");
        if (pinNode is not null)
        {
            var sspc = pinNode.Children.FirstOrDefault(c => c.Id == "sspc");
            if (sspc is { RawPayload.Length: >= 26 } && Encoding.ASCII.GetString(sspc.RawPayload, 22, 4) == "Soli")
            {
                kind = "Solid";
            }

            var alas = pinNode.Children.FirstOrDefault(c => c.Id == "alas");
            if (alas is { RawPayload.Length: > 0 })
            {
                var rawAlias = Encoding.UTF8.GetString(alas.RawPayload).TrimEnd('\0');
                if (rawAlias.Contains("fullpath", StringComparison.OrdinalIgnoreCase) || rawAlias.Contains(":\\") || rawAlias.StartsWith('/'))
                {
                    filePath = ExtractPathFromAlas(rawAlias);
                }
            }
        }

        bool? exists = null;
        bool isNet = false;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            isNet = filePath.StartsWith(@"\\", StringComparison.Ordinal);
            exists = !isNet && File.Exists(filePath);
        }

        return new FootageItemModel
        {
            ItemId = itemId,
            Name = name,
            Kind = kind,
            FilePath = filePath,
            FileExistsOnDisk = exists,
            IsNetworkPath = isNet,
            IsImageSequence = filePath is not null && (filePath.Contains('[') || filePath.Contains('%')),
            SourceOffset = itemNode.SourceOffset
        };
    }

    private static string InferLayerType(ProjectChunkNode layrNode, bool is3D)
    {
        var tags = layrNode.WalkDescendants().Select(d => d.DisplayTag).ToHashSet(StringComparer.Ordinal);
        if (tags.Contains("LIST:btdk") || tags.Contains("LIST:btds")) return "Text";
        if (tags.Contains("otln") && tags.Contains("LIST:shap")) return "Shape";
        return is3D ? "3D / AV" : "AV / Solid";
    }

    private static string ReadAsciiZeroTerminated(byte[] data, int offset, int maxLen)
    {
        if (offset >= data.Length || maxLen <= 0) return string.Empty;
        int end = offset;
        int limit = Math.Min(data.Length, offset + maxLen);
        while (end < limit && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private static string? ExtractPathFromAlas(string alasText)
    {
        // Modern AE stores JSON in alas: {"fullpath":"C:\\..."}
        const string key = "\"fullpath\":\"";
        int idx = alasText.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int start = idx + key.Length;
            int end = alasText.IndexOf('"', start);
            if (end > start)
            {
                return alasText[start..end].Replace("\\\\", "\\");
            }
        }
        return alasText.Length <= 512 ? alasText : null;
    }

    private static string? ExtractIccDescriptionHint(byte[] pprf)
    {
        var text = Encoding.ASCII.GetString(pprf);
        if (text.Contains("sRGB", StringComparison.OrdinalIgnoreCase)) return "sRGB IEC61966-2.1";
        if (text.Contains("ACES", StringComparison.OrdinalIgnoreCase)) return "ACES / OCIO";
        if (text.Contains("Rec.709", StringComparison.OrdinalIgnoreCase)) return "Rec.709";
        return "Embedded ICC Profile";
    }
}
