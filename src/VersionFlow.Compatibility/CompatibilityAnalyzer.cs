using VersionFlow.Core.Interfaces;
using VersionFlow.Core.Models;

namespace VersionFlow.Compatibility;

public sealed class CompatibilityAnalyzer : ICompatibilityAnalyzer
{
    private static readonly (string Token, int MinFormatMajor, string Description)[] NewerExpressionApis =
    [
        ("previousKey(", 96, "previousKey() expression method requires AE 2025+ (Format 96+)."),
        ("nextKey(", 96, "nextKey() expression method requires AE 2025+ (Format 96+)."),
        ("text.sourceText.style", 93, "Per-character text style expressions require AE 17.0+ (2020) and newer font axes require 2025+."),
        ("setFont(", 94, "Expression font mutation APIs may differ in older AE releases.")
    ];

    public CompatibilityReport Analyse(ParsedProjectDocument document, AfterEffectsVersionInfo targetVersion)
    {
        var findings = new List<CompatibilityFinding>();
        int sourceFormat = document.VersionDetection.FormatMajorVersion ?? 0;

        // 1. Version direction & format boundary check
        if (sourceFormat == 0)
        {
            findings.Add(new CompatibilityFinding
            {
                Id = "VER-UNKNOWN",
                Category = "Project Format",
                FeatureName = "Source Version Unknown",
                Classification = FeatureClassification.Unknown,
                Description = "Could not verify the source format version from 'head' metadata.",
                VisualOrFunctionalImpact = "Compatibility cannot be guaranteed without a known source format."
            });
        }
        else if (sourceFormat <= targetVersion.FormatMajorVersion)
        {
            findings.Add(new CompatibilityFinding
            {
                Id = "VER-FORWARD",
                Category = "Project Format",
                FeatureName = $"Format {sourceFormat} -> {targetVersion.ShortName} (Format {targetVersion.FormatMajorVersion})",
                Classification = FeatureClassification.Compatible,
                Description = "Source project format is same-generation or older than target release."
            });
        }
        else
        {
            findings.Add(new CompatibilityFinding
            {
                Id = "VER-DOWNGRADE",
                Category = "Project Format",
                FeatureName = $"Format Downgrade ({sourceFormat} -> {targetVersion.FormatMajorVersion})",
                Classification = FeatureClassification.Convertible,
                Description = $"Project uses internal format {sourceFormat}; target {targetVersion.DisplayName} expects format {targetVersion.FormatMajorVersion}.",
                ProposedTransformation = $"Update validated 'head' format version ({sourceFormat} -> {targetVersion.FormatMajorVersion}) and 'svap' metadata via migration rules.",
                IsReversible = false,
                VisualOrFunctionalImpact = "Requires structural validation; target opening is experimental unless verified in target AE.",
                RequiresUserApproval = targetVersion.CompatibilityStatus == CompatibilityStatus.Experimental
            });
        }

        // 2. Track matte boundary check (Format 94+ vs 93)
        if (sourceFormat >= 94 && targetVersion.FormatMajorVersion < 94)
        {
            int layerCount = document.Summary.LayerCount ?? 0;
            findings.Add(new CompatibilityFinding
            {
                Id = "FEAT-TRACK-MATTE-94",
                Category = "Track Mattes",
                FeatureName = "AE 2023+ Track Matte Schema Boundary",
                Classification = layerCount > 0 ? FeatureClassification.Warning : FeatureClassification.Compatible,
                Description = "AE 2023 (Format 94) replaced adjacent-layer track mattes with selectable layer-ID track mattes in 'ldta'.",
                ProposedTransformation = "Preserve compatible layer data; flag any non-adjacent or multi-consumer track mattes for manual review.",
                IsReversible = false,
                VisualOrFunctionalImpact = "Non-adjacent or shared track mattes may not render identically in AE 2020–2022.",
                RequiresUserApproval = false
            });
        }

        // 3. 3D Renderer inspection (LIST:PRin -> prin)
        foreach (var comp in document.Compositions)
        {
            if (string.IsNullOrWhiteSpace(comp.RendererMatchName)) continue;

            if (comp.RendererMatchName.Contains("Advanced", StringComparison.OrdinalIgnoreCase)
                || comp.RendererMatchName.Contains("Mercury", StringComparison.OrdinalIgnoreCase))
            {
                if (targetVersion.FormatMajorVersion < 95)
                {
                    findings.Add(new CompatibilityFinding
                    {
                        Id = $"3D-RENDERER-{comp.ItemId}",
                        Category = "3D Renderer",
                        FeatureName = comp.RendererDisplayName ?? comp.RendererMatchName,
                        MatchName = comp.RendererMatchName,
                        Location = $"Composition '{comp.Name}'",
                        Classification = FeatureClassification.Unsupported,
                        Description = "Advanced 3D renderer requires After Effects 2024+ (Format 95+).",
                        ProposedTransformation = "Fallback renderer tag to 'ADBE Escher' (Classic 3D) in Compatibility Mode (3D models/IBL will not render).",
                        IsReversible = false,
                        VisualOrFunctionalImpact = "3D meshes, environment lights, and Advanced 3D shadows will be lost or flattened.",
                        RequiresUserApproval = true
                    });
                }
            }
            else
            {
                findings.Add(new CompatibilityFinding
                {
                    Id = $"3D-COMPAT-{comp.ItemId}",
                    Category = "3D Renderer",
                    FeatureName = comp.RendererDisplayName ?? comp.RendererMatchName,
                    MatchName = comp.RendererMatchName,
                    Location = $"Composition '{comp.Name}'",
                    Classification = FeatureClassification.Compatible,
                    Description = "Classic 3D / Cinema 4D renderer is supported in target version."
                });
            }
        }

        // 4. Effects & third-party plugins
        var seenEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fx in document.EffectsUsed)
        {
            if (!seenEffects.Add(fx.MatchName)) continue;

            var builtIn = FeatureDatabase.FindBuiltInEffect(fx.MatchName);
            if (builtIn is not null)
            {
                if (builtIn.IntroducedFormatMajor > targetVersion.FormatMajorVersion)
                {
                    findings.Add(new CompatibilityFinding
                    {
                        Id = $"FX-UNSUPPORTED-{fx.MatchName}",
                        Category = "Effects",
                        FeatureName = builtIn.DisplayName,
                        MatchName = fx.MatchName,
                        Location = fx.Location,
                        Classification = FeatureClassification.Unsupported,
                        Description = $"{builtIn.DisplayName} was introduced in AE {2020 + (builtIn.IntroducedAppMajor - 17)} and is unavailable in {targetVersion.ShortName}.",
                        VisualOrFunctionalImpact = "Target After Effects will report a missing effect placeholder.",
                        RequiresUserApproval = true
                    });
                }
                else
                {
                    findings.Add(new CompatibilityFinding
                    {
                        Id = $"FX-OK-{fx.MatchName}",
                        Category = "Effects",
                        FeatureName = builtIn.DisplayName,
                        MatchName = fx.MatchName,
                        Location = fx.Location,
                        Classification = FeatureClassification.Compatible,
                        Description = $"Built-in effect '{builtIn.DisplayName}' exists in {targetVersion.ShortName}."
                    });
                }
            }
            else
            {
                var thirdParty = FeatureDatabase.DetectThirdPartyVendor(fx.MatchName);
                if (thirdParty is not null)
                {
                    findings.Add(new CompatibilityFinding
                    {
                        Id = $"PLUGIN-{fx.MatchName}",
                        Category = "Third-Party Plugins",
                        FeatureName = $"{thirdParty.VendorName} ({fx.MatchName})",
                        MatchName = fx.MatchName,
                        Location = fx.Location,
                        Classification = FeatureClassification.Warning,
                        Description = $"{thirdParty.Notes} Referenced by project; local installation & target AE compatibility cannot be verified standalone.",
                        VisualOrFunctionalImpact = "Preserves raw plugin chunk data; target workstation must have the matching plugin installed."
                    });
                }
                else
                {
                    findings.Add(new CompatibilityFinding
                    {
                        Id = $"FX-ADBE-{fx.MatchName}",
                        Category = "Effects",
                        FeatureName = fx.MatchName,
                        MatchName = fx.MatchName,
                        Location = fx.Location,
                        Classification = FeatureClassification.Compatible,
                        Description = "Adobe built-in match name preserved."
                    });
                }
            }
        }

        // 5. Expressions scan
        foreach (var expr in document.ExpressionsUsed)
        {
            foreach (var (token, minFmt, desc) in NewerExpressionApis)
            {
                if (minFmt > targetVersion.FormatMajorVersion && expr.ExpressionText.Contains(token, StringComparison.Ordinal))
                {
                    findings.Add(new CompatibilityFinding
                    {
                        Id = $"EXPR-{token.TrimEnd('(')}-{Math.Abs(expr.Location.GetHashCode())}",
                        Category = "Expressions",
                        FeatureName = $"Expression API '{token}'",
                        Location = expr.Location,
                        Classification = FeatureClassification.Warning,
                        Description = desc,
                        VisualOrFunctionalImpact = "Expression is preserved untouched, but may raise an expression evaluation error in target AE."
                    });
                }
            }
        }

        // 6. Color management check
        if (!string.IsNullOrWhiteSpace(document.Summary.WorkingColorSpaceName)
            && document.Summary.WorkingColorSpaceName.Contains("ACES", StringComparison.OrdinalIgnoreCase)
            && targetVersion.FormatMajorVersion < 94)
        {
            findings.Add(new CompatibilityFinding
            {
                Id = "COLOR-ACES-OCIO",
                Category = "Color Management",
                FeatureName = document.Summary.WorkingColorSpaceName,
                Classification = FeatureClassification.Warning,
                Description = "OCIO / ACES color management settings differ significantly in AE 2020–2022.",
                VisualOrFunctionalImpact = "Colors may shift unless manually configured in Project Settings."
            });
        }

        // 7. Unknown binary chunks honesty check
        bool hasOpaque = document.UnknownChunkCount > 0;
        if (hasOpaque)
        {
            findings.Add(new CompatibilityFinding
            {
                Id = "BIN-OPAQUE-CHUNKS",
                Category = "Binary Coverage",
                FeatureName = $"{document.UnknownChunkCount} Unmapped / Opaque Chunks ({document.UnknownChunkBytes:N0} B)",
                Classification = FeatureClassification.Unknown,
                Description = "Parts of this project contain proprietary or undocumented binary chunk types preserved byte-for-byte.",
                VisualOrFunctionalImpact = "VersionFlow preserves unknown chunks losslessly, but cannot certify compatibility of uninspected structures."
            });
        }

        int compat = findings.Count(f => f.Classification == FeatureClassification.Compatible);
        int conv = findings.Count(f => f.Classification == FeatureClassification.Convertible);
        int warn = findings.Count(f => f.Classification == FeatureClassification.Warning);
        int unsup = findings.Count(f => f.Classification == FeatureClassification.Unsupported);
        int crit = findings.Count(f => f.Classification == FeatureClassification.Critical);
        int unk = findings.Count(f => f.Classification == FeatureClassification.Unknown);

        var recMethod = sourceFormat <= targetVersion.FormatMajorVersion
            ? ConversionMethod.StandaloneNative
            : (targetVersion.CompatibilityStatus == CompatibilityStatus.Experimental
                ? ConversionMethod.ExperimentalMetadata
                : ConversionMethod.StandaloneNative);

        return new CompatibilityReport
        {
            SourcePath = document.SourcePath,
            SourceVersion = document.VersionDetection,
            TargetVersion = targetVersion,
            Findings = findings,
            CompatibleCount = compat,
            ConvertibleCount = conv,
            WarningCount = warn,
            UnsupportedCount = unsup,
            CriticalCount = crit,
            UnknownCount = unk,
            HasUninspectedOpaqueRegions = hasOpaque,
            UninspectedSummary = hasOpaque
                ? $"{document.UnknownChunkCount} undocumented chunk(s) totaling {document.UnknownChunkBytes:N0} bytes preserved verbatim."
                : null,
            RecommendedMethod = recMethod,
            SummaryText = $"{compat} compatible, {conv} convertible, {warn} warning(s), {unsup} unsupported, {crit} critical, {unk} unmapped."
        };
    }
}
