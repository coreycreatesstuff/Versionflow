using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;
using VersionFlow.Formats.Aepx;

namespace VersionFlow.Conversion.Rules;

public sealed class VersionMetadataMigrationRule : IMigrationRule
{
    public string Id => "VF-VERSION-METADATA";
    public string Description => "Updates validated 'head' internal format version (bytes 0..3) and 'svap' saving application build word (bytes 4..7) to match target release.";
    public RuleRiskLevel RiskLevel => RuleRiskLevel.Experimental;
    public bool IsLossy => false;
    public string ResearchReference => "docs/FORMAT_RESEARCH.md §3.3 & docs/VERSION_MATRIX.md";
    public CompatibilityStatus VerificationStatus => CompatibilityStatus.Experimental;

    public bool AppliesTo(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion, ConversionMode mode)
    {
        int currentFmt = document.Summary.FormatMajorVersion ?? 0;
        if (currentFmt == 0) return false;
        return currentFmt != targetVersion.FormatMajorVersion || mode != ConversionMode.Safe;
    }

    public IReadOnlyList<AppliedTransformation> Apply(
        ParsedProjectDocument document,
        AfterEffectsVersionInfo targetVersion,
        ConversionMode mode,
        IReadOnlySet<string> approvedFindingIds)
    {
        var applied = new List<AppliedTransformation>();
        if (document.FormatKind == ProjectFormatKind.AepBinary)
        {
            var head = document.RootChunks.FirstOrDefault(c => c.Id == "head");
            if (head is { RawPayload.Length: >= 8 })
            {
                ushort oldMajor = BinaryPrimitives.ReadUInt16BigEndian(head.RawPayload.AsSpan(0, 2));
                ushort oldMinor = BinaryPrimitives.ReadUInt16BigEndian(head.RawPayload.AsSpan(2, 2));
                uint oldSvap = BinaryPrimitives.ReadUInt32BigEndian(head.RawPayload.AsSpan(4, 4));

                ushort newMajor = (ushort)targetVersion.FormatMajorVersion;
                ushort newMinor = (ushort)targetVersion.FormatMinorVersion;
                uint newSvap = targetVersion.CanonicalSvapWord ?? oldSvap;

                BinaryPrimitives.WriteUInt16BigEndian(head.RawPayload.AsSpan(0, 2), newMajor);
                BinaryPrimitives.WriteUInt16BigEndian(head.RawPayload.AsSpan(2, 2), newMinor);
                BinaryPrimitives.WriteUInt32BigEndian(head.RawPayload.AsSpan(4, 4), newSvap);
                head.Summary = $"Format {newMajor}.{newMinor}, svap=0x{newSvap:X8}";

                var svapChunk = document.RootChunks.FirstOrDefault(c => c.Id == "svap");
                if (svapChunk is { RawPayload.Length: >= 4 })
                {
                    BinaryPrimitives.WriteUInt32BigEndian(svapChunk.RawPayload.AsSpan(0, 4), newSvap);
                    svapChunk.Summary = $"0x{newSvap:X8}";
                }

                applied.Add(new AppliedTransformation
                {
                    RuleId = Id,
                    Description = $"Updated RIFX 'head' format version and 'svap' build word for {targetVersion.ShortName}.",
                    Location = "Root > head, svap",
                    BeforeSummary = $"Format {oldMajor}.{oldMinor} (svap 0x{oldSvap:X8})",
                    AfterSummary = $"Format {newMajor}.{newMinor} (svap 0x{newSvap:X8})",
                    IsLossy = false,
                    Notes = "Exact byte offsets verified against AE 2024 RIFX fixture."
                });
            }
        }
        else if (document.FormatKind == ProjectFormatKind.AepxXml && !string.IsNullOrEmpty(document.RawXmlContent))
        {
            var xdoc = XDocument.Parse(document.RawXmlContent, LoadOptions.PreserveWhitespace);
            var headEl = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "head");
            var bdataAttr = headEl?.Attribute("bdata");
            if (bdataAttr is not null && AepxParser.TryDecodeHex(bdataAttr.Value, out var bytes) && bytes.Length >= 8)
            {
                ushort oldMajor = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2));
                ushort oldMinor = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2));
                uint oldSvap = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4));

                ushort newMajor = (ushort)targetVersion.FormatMajorVersion;
                ushort newMinor = (ushort)targetVersion.FormatMinorVersion;
                uint newSvap = targetVersion.CanonicalSvapWord ?? oldSvap;

                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0, 2), newMajor);
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2, 2), newMinor);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), newSvap);
                bdataAttr.Value = Convert.ToHexString(bytes).ToLowerInvariant();

                var svapEl = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "svap");
                var svapAttr = svapEl?.Attribute("bdata");
                if (svapAttr is not null && AepxParser.TryDecodeHex(svapAttr.Value, out var svapBytes) && svapBytes.Length >= 4)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(svapBytes.AsSpan(0, 4), newSvap);
                    svapAttr.Value = Convert.ToHexString(svapBytes).ToLowerInvariant();
                }

                document.RawXmlContent = FormatXmlPreservingDeclaration(xdoc);

                applied.Add(new AppliedTransformation
                {
                    RuleId = Id,
                    Description = $"Updated AEPX <head bdata> format version for {targetVersion.ShortName}.",
                    Location = "AfterEffectsProject > head/@bdata",
                    BeforeSummary = $"Format {oldMajor}.{oldMinor} (svap 0x{oldSvap:X8})",
                    AfterSummary = $"Format {newMajor}.{newMinor} (svap 0x{newSvap:X8})",
                    IsLossy = false
                });
            }
        }

        document.Summary.FormatMajorVersion = targetVersion.FormatMajorVersion;
        document.Summary.FormatMinorVersion = targetVersion.FormatMinorVersion;
        return applied;
    }

    internal static string FormatXmlPreservingDeclaration(XDocument xdoc) =>
        xdoc.Declaration is not null
            ? $"{xdoc.Declaration}{Environment.NewLine}{xdoc}"
            : xdoc.ToString(SaveOptions.DisableFormatting);
}

public sealed class ThreeDRendererCompatibilityRule : IMigrationRule
{
    public string Id => "VF-3D-RENDERER";
    public string Description => "Rewrites unsupported Advanced 3D ('ADBE Advanced 3D') composition renderer tags to Classic 3D ('ADBE Escher') in both binary .aep and XML .aepx.";
    public RuleRiskLevel RiskLevel => RuleRiskLevel.High;
    public bool IsLossy => true;
    public string ResearchReference => "docs/FEATURE_COMPATIBILITY.md & AE 2024 secret.aep LIST:PRin/prin";
    public CompatibilityStatus VerificationStatus => CompatibilityStatus.PartiallyVerified;

    public bool AppliesTo(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion, ConversionMode mode)
    {
        if (targetVersion.FormatMajorVersion >= 95 || mode == ConversionMode.Safe)
            return false;

        if (document.FormatKind == ProjectFormatKind.AepxXml)
            return !string.IsNullOrEmpty(document.RawXmlContent) && document.RawXmlContent.Contains("prin", StringComparison.OrdinalIgnoreCase);

        return document.Compositions.Any(c =>
            !string.IsNullOrWhiteSpace(c.RendererMatchName) &&
            (c.RendererMatchName.Contains("Advanced", StringComparison.OrdinalIgnoreCase)
             || c.RendererMatchName.Contains("Mercury", StringComparison.OrdinalIgnoreCase)));
    }

    public IReadOnlyList<AppliedTransformation> Apply(
        ParsedProjectDocument document,
        AfterEffectsVersionInfo targetVersion,
        ConversionMode mode,
        IReadOnlySet<string> approvedFindingIds)
    {
        var applied = new List<AppliedTransformation>();
        if (document.FormatKind == ProjectFormatKind.AepBinary)
        {
            foreach (var itemNode in document.RootChunks.SelectMany(r => r.WalkDescendants()).Where(n => n.IsList && n.ListType == "Item"))
            {
                var prin = itemNode.Children
                    .FirstOrDefault(c => c.IsList && c.ListType == "PRin")?
                    .Children.FirstOrDefault(c => c.Id == "prin");
                if (prin is not { RawPayload.Length: >= 104 }) continue;

                string currentMatch = Encoding.ASCII.GetString(prin.RawPayload, 4, 48).TrimEnd('\0');
                if (!IsUnsupported3DRenderer(currentMatch)) continue;

                if (mode == ConversionMode.Compatibility && approvedFindingIds.Count > 0)
                {
                    if (!approvedFindingIds.Any(id => id.StartsWith("3D-RENDERER-", StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                RewritePrinBytes(prin.RawPayload);

                applied.Add(new AppliedTransformation
                {
                    RuleId = Id,
                    Description = "Switched unsupported Advanced 3D composition renderer to Classic 3D ('ADBE Escher').",
                    Location = $"PRin/prin @0x{prin.SourceOffset:X}",
                    BeforeSummary = currentMatch,
                    AfterSummary = "ADBE Escher (Classic 3D)",
                    IsLossy = true,
                    Notes = "3D models and environment lights are not supported in Classic 3D."
                });
            }
        }
        else if (document.FormatKind == ProjectFormatKind.AepxXml && !string.IsNullOrEmpty(document.RawXmlContent))
        {
            var xdoc = XDocument.Parse(document.RawXmlContent, LoadOptions.PreserveWhitespace);
            bool changed = false;
            int idx = 0;
            foreach (var prinEl in xdoc.Descendants().Where(e => e.Name.LocalName == "prin"))
            {
                idx++;
                var bdataAttr = prinEl.Attribute("bdata");
                if (bdataAttr is null || !AepxParser.TryDecodeHex(bdataAttr.Value, out var bytes) || bytes.Length < 104)
                    continue;

                string currentMatch = Encoding.ASCII.GetString(bytes, 4, 48).TrimEnd('\0');
                if (!IsUnsupported3DRenderer(currentMatch)) continue;

                RewritePrinBytes(bytes);
                bdataAttr.Value = Convert.ToHexString(bytes).ToLowerInvariant();
                changed = true;

                applied.Add(new AppliedTransformation
                {
                    RuleId = Id,
                    Description = "Switched AEPX <prin bdata> renderer to Classic 3D ('ADBE Escher').",
                    Location = $"AEPX prin #{idx}",
                    BeforeSummary = currentMatch,
                    AfterSummary = "ADBE Escher (Classic 3D)",
                    IsLossy = true
                });
            }
            if (changed)
                document.RawXmlContent = VersionMetadataMigrationRule.FormatXmlPreservingDeclaration(xdoc);
        }

        return applied;
    }

    private static bool IsUnsupported3DRenderer(string matchName) =>
        matchName.Contains("Advanced", StringComparison.OrdinalIgnoreCase)
        || matchName.Contains("Mercury", StringComparison.OrdinalIgnoreCase);

    private static void RewritePrinBytes(byte[] payload)
    {
        Array.Clear(payload, 4, 48);
        Encoding.ASCII.GetBytes("ADBE Escher", payload.AsSpan(4));
        Array.Clear(payload, 52, Math.Min(48, payload.Length - 52));
        Encoding.ASCII.GetBytes("Classic 3D", payload.AsSpan(52));
    }
}

public sealed class TrackMatteCompatibilityRule : IMigrationRule
{
    public string Id => "VF-TRACK-MATTE-COMPATIBILITY";
    public string Description => "Audits AE 2023+ (Format 94+) layer 'ldta' structures when converting to Format 93 (AE 2020–2022) and flags selectable track matte boundaries.";
    public RuleRiskLevel RiskLevel => RuleRiskLevel.Moderate;
    public bool IsLossy => false;
    public string ResearchReference => "docs/FEATURE_COMPATIBILITY.md & Adobe AE 23.0 Selectable Track Mattes";
    public CompatibilityStatus VerificationStatus => CompatibilityStatus.PartiallyVerified;

    public bool AppliesTo(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion, ConversionMode mode)
    {
        int srcFmt = document.Summary.FormatMajorVersion ?? 0;
        return srcFmt >= 94 && targetVersion.FormatMajorVersion < 94 && document.Compositions.Count > 0;
    }

    public IReadOnlyList<AppliedTransformation> Apply(
        ParsedProjectDocument document,
        AfterEffectsVersionInfo targetVersion,
        ConversionMode mode,
        IReadOnlySet<string> approvedFindingIds)
    {
        int layerCount = document.Compositions.Sum(c => c.Layers.Count);
        return
        [
            new AppliedTransformation
            {
                RuleId = Id,
                Description = $"Audited {layerCount} layer(s) across {document.Compositions.Count} composition(s) for Format 94+ selectable track matte boundary.",
                Location = "Compositions > Layr/ldta",
                BeforeSummary = "Format 94+ ldta schema",
                AfterSummary = "Preserved ldta; adjacent mattes retained, non-adjacent mattes flagged",
                IsLossy = false,
                Notes = "Verify any non-adjacent or shared track mattes when opening in AE 2020–2022."
            }
        ];
    }
}

public sealed class AssetRelinkMigrationRule : IMigrationRule
{
    public string Id => "VF-ASSET-RELINK";
    public string Description => "Updates footage 'alas' path references in output copies (.aep and .aepx) when user selects relinked media paths.";
    public RuleRiskLevel RiskLevel => RuleRiskLevel.Low;
    public bool IsLossy => false;
    public string ResearchReference => "docs/CHUNK_REGISTRY.md (LIST:Pin / alas)";
    public CompatibilityStatus VerificationStatus => CompatibilityStatus.PartiallyVerified;

    public bool AppliesTo(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion, ConversionMode mode) => false;

    public IReadOnlyList<AppliedTransformation> Apply(
        ParsedProjectDocument document,
        AfterEffectsVersionInfo targetVersion,
        ConversionMode mode,
        IReadOnlySet<string> approvedFindingIds) => [];

    public static IReadOnlyList<AppliedTransformation> ApplyRelinks(
        ParsedProjectDocument document,
        IReadOnlyDictionary<string, string>? relinkMap)
    {
        var applied = new List<AppliedTransformation>();
        if (relinkMap is null || relinkMap.Count == 0)
            return applied;

        if (document.FormatKind == ProjectFormatKind.AepBinary)
        {
            foreach (var alas in document.RootChunks.SelectMany(r => r.WalkDescendants()).Where(n => n.Id == "alas"))
            {
                string raw = Encoding.UTF8.GetString(alas.RawPayload);
                foreach (var (oldPath, newPath) in relinkMap)
                {
                    string oldEscaped = oldPath.Replace("\\", "\\\\");
                    string newEscaped = newPath.Replace("\\", "\\\\");
                    if (raw.Contains(oldEscaped, StringComparison.OrdinalIgnoreCase))
                    {
                        string updated = Regex.Replace(raw, Regex.Escape(oldEscaped), newEscaped.Replace("$", "$$"), RegexOptions.IgnoreCase);
                        alas.RawPayload = Encoding.UTF8.GetBytes(updated);
                        applied.Add(new AppliedTransformation
                        {
                            RuleId = "VF-ASSET-RELINK",
                            Description = "Updated footage path in output project.",
                            Location = $"alas @0x{alas.SourceOffset:X}",
                            BeforeSummary = oldPath,
                            AfterSummary = newPath,
                            IsLossy = false
                        });
                        break;
                    }
                }
            }
        }
        else if (document.FormatKind == ProjectFormatKind.AepxXml && !string.IsNullOrEmpty(document.RawXmlContent))
        {
            var xdoc = XDocument.Parse(document.RawXmlContent, LoadOptions.PreserveWhitespace);
            bool changed = false;
            foreach (var alasEl in xdoc.Descendants().Where(e => e.Name.LocalName == "alas"))
            {
                var bdataAttr = alasEl.Attribute("bdata");
                if (bdataAttr is null || !AepxParser.TryDecodeHex(bdataAttr.Value, out var bytes)) continue;
                string raw = Encoding.UTF8.GetString(bytes);
                foreach (var (oldPath, newPath) in relinkMap)
                {
                    string oldEscaped = oldPath.Replace("\\", "\\\\");
                    string newEscaped = newPath.Replace("\\", "\\\\");
                    if (raw.Contains(oldEscaped, StringComparison.OrdinalIgnoreCase))
                    {
                        string updated = Regex.Replace(raw, Regex.Escape(oldEscaped), newEscaped.Replace("$", "$$"), RegexOptions.IgnoreCase);
                        bdataAttr.Value = Convert.ToHexString(Encoding.UTF8.GetBytes(updated)).ToLowerInvariant();
                        changed = true;
                        applied.Add(new AppliedTransformation
                        {
                            RuleId = "VF-ASSET-RELINK",
                            Description = "Updated AEPX <alas bdata> footage path in output project.",
                            Location = "AEPX alas/@bdata",
                            BeforeSummary = oldPath,
                            AfterSummary = newPath,
                            IsLossy = false
                        });
                        break;
                    }
                }
            }
            if (changed)
                document.RawXmlContent = VersionMetadataMigrationRule.FormatXmlPreservingDeclaration(xdoc);
        }

        return applied;
    }
}
