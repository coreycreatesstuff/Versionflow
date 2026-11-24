using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using VersionFlow.Core.Errors;
using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;

namespace VersionFlow.Formats.Aepx;

public sealed class AepxParser
{
    public const long MaxFileSizeBytes = 512L * 1024 * 1024; // 512 MiB

    public async Task<ParsedProjectDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new VersionFlowException(ErrorCodes.SourceUnreadable, $"AEPX file does not exist: {filePath}");

        var fi = new FileInfo(filePath);
        if (fi.Length == 0)
            throw new VersionFlowException(ErrorCodes.MalformedXml, "The AEPX project file is empty (0 bytes).");
        if (fi.Length > MaxFileSizeBytes)
            throw new VersionFlowException(ErrorCodes.ResourceLimitExceeded, $"AEPX file exceeds maximum size ({fi.Length:N0} bytes).");

        string rawXml = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);

        // Harden against XXE, DTD entity expansion, and external network reads
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 200_000_000,
            MaxCharactersFromEntities = 1024,
            IgnoreWhitespace = false
        };

        XDocument xdoc;
        try
        {
            using var sr = new StringReader(rawXml);
            using var xr = XmlReader.Create(sr, settings);
            xdoc = XDocument.Load(xr, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new VersionFlowException(
                ErrorCodes.MalformedXml,
                $"Malformed AEPX XML structure at line {ex.LineNumber}, position {ex.LinePosition}.",
                ["Verify the .aepx file was exported completely from After Effects and is not truncated."],
                ex);
        }

        if (xdoc.Root is null || !string.Equals(xdoc.Root.Name.LocalName, "AfterEffectsProject", StringComparison.Ordinal))
        {
            throw new VersionFlowException(
                ErrorCodes.InvalidSignature,
                $"XML root element '{xdoc.Root?.Name.LocalName ?? "<none>"}' is not 'AfterEffectsProject'. Ordinary XML files are not valid AEPX projects.");
        }

        var doc = new ParsedProjectDocument
        {
            SourcePath = filePath,
            FileSizeBytes = fi.Length,
            FormatKind = ProjectFormatKind.AepxXml,
            RawXmlContent = rawXml,
            XmlEncodingName = xdoc.Declaration?.Encoding ?? "UTF-8",
            VersionDetection = new VersionDetectionResult
            {
                FormatKind = ProjectFormatKind.AepxXml,
                DisplayLabel = "Pending detection",
                Confidence = DetectionConfidence.Unknown,
                DetectionMethod = "Pending"
            }
        };

        PopulateSemanticFromXml(xdoc, doc);
        return doc;
    }

    private static void PopulateSemanticFromXml(XDocument xdoc, ParsedProjectDocument doc)
    {
        var summary = new ProjectSummary();
        int compCount = 0;
        int layerCount = 0;
        int footageCount = 0;
        int folderCount = 0;

        foreach (var el in xdoc.Descendants())
        {
            string local = el.Name.LocalName;
            if (local == "head")
            {
                var bdata = el.Attribute("bdata")?.Value;
                if (TryDecodeHex(bdata, out var bytes) && bytes.Length >= 8)
                {
                    summary.FormatMajorVersion = (bytes[0] << 8) | bytes[1];
                    summary.FormatMinorVersion = (bytes[2] << 8) | bytes[3];
                    summary.SvapWord = (uint)((bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7]);
                }
            }
            else if (local == "svap")
            {
                var bdata = el.Attribute("bdata")?.Value;
                if (TryDecodeHex(bdata, out var bytes) && bytes.Length >= 4)
                {
                    summary.SvapWord ??= (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
                }
            }
            else if (local == "pjef")
            {
                var matchName = el.Attribute("matchName")?.Value ?? el.Value.Trim();
                if (!string.IsNullOrWhiteSpace(matchName))
                {
                    doc.EffectsUsed.Add(new EffectUsageModel { MatchName = matchName, Location = "AEPX Pefl" });
                }
            }
            else if (local == "Item")
            {
                // Classify based on child cdta / pin / fold
                if (el.Elements().Any(c => c.Name.LocalName == "cdta"))
                {
                    compCount++;
                    var compName = el.Elements().FirstOrDefault(c => c.Name.LocalName == "string" || c.Name.LocalName == "Utf8")?.Value?.Trim()
                                   ?? $"Composition {compCount}";
                    var comp = new CompositionModel { ItemId = compCount, Name = compName };
                    foreach (var layr in el.Elements().Where(c => c.Name.LocalName == "Layr"))
                    {
                        layerCount++;
                        var lname = layr.Elements().FirstOrDefault(c => c.Name.LocalName == "string" || c.Name.LocalName == "Utf8")?.Value?.Trim()
                                    ?? $"Layer {comp.Layers.Count + 1}";
                        comp.Layers.Add(new LayerModel { LayerId = layerCount, Index = comp.Layers.Count + 1, Name = lname });
                    }
                    doc.Compositions.Add(comp);
                }
                else if (el.Elements().Any(c => c.Name.LocalName == "Fold"))
                {
                    folderCount++;
                    doc.Folders.Add(new ProjectFolderModel { ItemId = folderCount, Name = $"Folder {folderCount}" });
                }
                else if (el.Elements().Any(c => c.Name.LocalName == "Pin"))
                {
                    footageCount++;
                    doc.FootageItems.Add(new FootageItemModel { ItemId = footageCount, Name = $"Footage {footageCount}" });
                }
            }
        }

        summary.CompositionCount = compCount;
        summary.LayerCount = layerCount;
        summary.FootageCount = footageCount;
        summary.FolderCount = folderCount;
        summary.EffectReferenceCount = doc.EffectsUsed.Count;
        doc.Summary = summary;
    }

    public static bool TryDecodeHex(string? hex, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var clean = hex.Trim();
        if (clean.Length % 2 != 0) return false;
        bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(clean.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                bytes = [];
                return false;
            }
        }
        return true;
    }
}

public sealed class AepxWriter : IProjectWriter
{
    public async Task WriteAsync(ParsedProjectDocument document, string outputPath, CancellationToken cancellationToken = default)
    {
        if (document.FormatKind != ProjectFormatKind.AepxXml || string.IsNullOrEmpty(document.RawXmlContent))
            throw new InvalidOperationException("AepxWriter requires a valid parsed AEPX XML document.");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Validate XML structure before committing
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 200_000_000
        };
        using (var sr = new StringReader(document.RawXmlContent))
        using (var xr = XmlReader.Create(sr, settings))
        {
            var check = XDocument.Load(xr, LoadOptions.PreserveWhitespace);
            if (check.Root?.Name.LocalName != "AfterEffectsProject")
                throw new VersionFlowException(ErrorCodes.ValidationFailed, "Modified AEPX root is invalid.");
        }

        await File.WriteAllTextAsync(outputPath, document.RawXmlContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }
}
