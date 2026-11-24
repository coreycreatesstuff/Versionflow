using VersionFlow.Core.Models;

namespace VersionFlow.Core.Registry;

public static class VersionRegistry
{
    private static readonly IReadOnlyList<AfterEffectsVersionInfo> AllVersionsList =
    [
        new AfterEffectsVersionInfo
        {
            Id = "ae-2026",
            DisplayName = "Adobe After Effects 2026",
            ShortName = "AE 2026",
            ReleaseYear = 2026,
            AppMajorVersion = 26,
            SupportedMinorVersions = "26.0+",
            FormatMajorVersion = 97,
            FormatMinorVersion = 1,
            CanonicalSvapWord = 0x10000000,
            CompatibilityStatus = CompatibilityStatus.Experimental,
            ParserSupport = "Full RIFX & AEPX structural parser",
            WriterSupport = "Lossless RIFX & AEPX preservation",
            MigrationSupport = "Source & upgrade target (Format 97); experimental until live AE 2026 verified",
            KnownFeatureDifferences =
            [
                "Native Unmult effect",
                "AI Object Matte & disk cache",
                "Parametric 3D meshes & variable font axes",
                "Dropdown Menu text expressions & previousKey()/nextKey()"
            ],
            AvailableMethods = [ConversionMethod.StandaloneNative, ConversionMethod.ExperimentalMetadata, ConversionMethod.AdobeAssisted],
            ValidationRequirements = ["RIFX header & container checks", "Feature scan against target"],
            ResearchReferences = ["docs/VERSION_MATRIX.md", "forticheprod/py-aep format 97"],
            IsSupportedTarget = true,
            IsSupportedSource = true
        },
        new AfterEffectsVersionInfo
        {
            Id = "ae-2025",
            DisplayName = "Adobe After Effects 2025",
            ShortName = "AE 2025",
            ReleaseYear = 2025,
            AppMajorVersion = 25,
            SupportedMinorVersions = "25.0–25.x",
            FormatMajorVersion = 96,
            FormatMinorVersion = 1,
            CanonicalSvapWord = 0x0f008500,
            CompatibilityStatus = CompatibilityStatus.Experimental,
            ParserSupport = "Full RIFX & AEPX structural parser",
            WriterSupport = "Lossless RIFX & AEPX preservation",
            MigrationSupport = "Rule-backed Compatibility Mode target (Format 96); no local AE 2025 binary",
            KnownFeatureDifferences =
            [
                "Parametric 3D primitives",
                "Variable font animation",
                "previousKey()/nextKey() expressions"
            ],
            AvailableMethods = [ConversionMethod.StandaloneNative, ConversionMethod.ExperimentalMetadata, ConversionMethod.AdobeAssisted],
            ValidationRequirements = ["RIFX structural validation"],
            ResearchReferences = ["docs/VERSION_MATRIX.md", "forticheprod/py-aep format 96"],
            IsSupportedTarget = true,
            IsSupportedSource = true
        },
        new AfterEffectsVersionInfo
        {
            Id = "ae-2024",
            DisplayName = "Adobe After Effects 2024",
            ShortName = "AE 2024",
            ReleaseYear = 2024,
            AppMajorVersion = 24,
            SupportedMinorVersions = "24.0–24.6",
            FormatMajorVersion = 95,
            FormatMinorVersion = 1,
            CanonicalSvapWord = 0x0f00843b,
            CompatibilityStatus = CompatibilityStatus.PartiallyVerified,
            ParserSupport = "Verified against genuine AE 2024 RIFX fixture (secret.aep)",
            WriterSupport = "Verified lossless round-trip on AE 2024 RIFX tree",
            MigrationSupport = "Partially verified (live AE 2024 aerender 24.4x47 open+render verified; 25/26->24 unverified on full 25/26 feature set)",
            KnownFeatureDifferences =
            [
                "Advanced 3D renderer & imported GLB/OBJ/USD models",
                "HDR / OCIO / ACES color management extensions"
            ],
            AvailableMethods = [ConversionMethod.StandaloneNative, ConversionMethod.AdobeAssisted, ConversionMethod.ExperimentalMetadata],
            ValidationRequirements = ["RIFX structural validation", "Optional live AE 2024 verification"],
            ResearchReferences = ["docs/FORMAT_RESEARCH.md", "AE 2024 secret.aep empirical dump"],
            IsSupportedTarget = true,
            IsSupportedSource = true
        },
        new AfterEffectsVersionInfo
        {
            Id = "ae-2023",
            DisplayName = "Adobe After Effects 2023",
            ShortName = "AE 2023",
            ReleaseYear = 2023,
            AppMajorVersion = 23,
            SupportedMinorVersions = "23.0–23.6",
            FormatMajorVersion = 94,
            FormatMinorVersion = 1,
            CanonicalSvapWord = 0x0e008300,
            CompatibilityStatus = CompatibilityStatus.Experimental,
            ParserSupport = "Full RIFX & AEPX structural parser",
            WriterSupport = "Lossless RIFX & AEPX preservation",
            MigrationSupport = "Rule-backed Compatibility Mode target (Format 94)",
            KnownFeatureDifferences =
            [
                "Selectable Track Matte layers (23.0 major format boundary)"
            ],
            AvailableMethods = [ConversionMethod.AdobeAssisted, ConversionMethod.ExperimentalMetadata, ConversionMethod.StandaloneNative],
            ValidationRequirements = ["RIFX structural validation", "3D model / 2024+ feature scan"],
            ResearchReferences = ["docs/VERSION_MATRIX.md", "Adobe AE 23.0 Selectable Track Mattes"],
            IsSupportedTarget = true,
            IsSupportedSource = true
        },
        new AfterEffectsVersionInfo
        {
            Id = "ae-2022",
            DisplayName = "Adobe After Effects 2022",
            ShortName = "AE 2022",
            ReleaseYear = 2022,
            AppMajorVersion = 22,
            SupportedMinorVersions = "22.0–22.6",
            FormatMajorVersion = 93,
            FormatMinorVersion = 1,
            CanonicalSvapWord = 0x0d008200,
            CompatibilityStatus = CompatibilityStatus.Experimental,
            ParserSupport = "Full RIFX & AEPX structural parser (Format 93)",
            WriterSupport = "Lossless RIFX & AEPX preservation",
            MigrationSupport = "Rule-backed Compatibility Mode target (Format 93, shared with 2020–2021)",
            KnownFeatureDifferences =
            [
                "Adjacent-only legacy track mattes (pre-23.0)",
                "Multi-Frame Rendering & Scene Edit Detection"
            ],
            AvailableMethods = [ConversionMethod.AdobeAssisted, ConversionMethod.StandaloneNative, ConversionMethod.ExperimentalMetadata],
            ValidationRequirements = ["RIFX structural validation", "No 94+ selectable track mattes"],
            ResearchReferences = ["docs/VERSION_MATRIX.md"],
            IsSupportedTarget = true,
            IsSupportedSource = true
        },
        new AfterEffectsVersionInfo
        {
            Id = "ae-2021",
            DisplayName = "Adobe After Effects 2021",
            ShortName = "AE 2021",
            ReleaseYear = 2021,
            AppMajorVersion = 18,
            SupportedMinorVersions = "18.0–18.4",
            FormatMajorVersion = 93,
            FormatMinorVersion = 1,
            CanonicalSvapWord = 0x0c008100,
            CompatibilityStatus = CompatibilityStatus.Experimental,
            ParserSupport = "Full RIFX & AEPX structural parser (Format 93)",
            WriterSupport = "Lossless RIFX & AEPX preservation",
            MigrationSupport = "Rule-backed Compatibility Mode target (Format 93, shared with 2020 & 2022)",
            KnownFeatureDifferences =
            [
                "Adjacent-only legacy track mattes",
                "Real-Time 3D Draft Preview"
            ],
            AvailableMethods = [ConversionMethod.StandaloneNative, ConversionMethod.ExperimentalMetadata, ConversionMethod.SemanticReconstruction],
            ValidationRequirements = ["RIFX structural validation", "Format 93 feature compatibility"],
            ResearchReferences = ["docs/VERSION_MATRIX.md"],
            IsSupportedTarget = true,
            IsSupportedSource = true
        },
        new AfterEffectsVersionInfo
        {
            Id = "ae-2020",
            DisplayName = "Adobe After Effects 2020",
            ShortName = "AE 2020",
            ReleaseYear = 2020,
            AppMajorVersion = 17,
            SupportedMinorVersions = "17.0–17.7",
            FormatMajorVersion = 93,
            FormatMinorVersion = 1,
            CanonicalSvapWord = 0x0b008000,
            CompatibilityStatus = CompatibilityStatus.Experimental,
            ParserSupport = "Full RIFX & AEPX structural parser (Format 93)",
            WriterSupport = "Lossless RIFX & AEPX preservation",
            MigrationSupport = "Flagship Compatibility Mode target (Format 93); experimental until verified with live AE 2020",
            KnownFeatureDifferences =
            [
                "Adjacent-only legacy track mattes (incompatible with 23.0+ selectable mattes)",
                "Classic 3D / Cinema 4D renderers only (no Advanced 3D / GLB / USD)",
                "JavaScript Expression Engine (no previousKey/nextKey or Dropdown text)"
            ],
            AvailableMethods = [ConversionMethod.StandaloneNative, ConversionMethod.ExperimentalMetadata, ConversionMethod.SemanticReconstruction],
            ValidationRequirements = ["RIFX structural validation", "Format 93 compatibility scan", "Live AE 2020 verification (when installed)"],
            ResearchReferences = ["docs/FORMAT_RESEARCH.md", "docs/VERSION_MATRIX.md", "forticheprod/py-aep format 93"],
            IsSupportedTarget = true,
            IsSupportedSource = true
        }
    ];

    public static IReadOnlyList<AfterEffectsVersionInfo> All => AllVersionsList;

    public static IReadOnlyList<AfterEffectsVersionInfo> SupportedTargets =>
        AllVersionsList.Where(v => v.IsSupportedTarget).ToList();

    public static AfterEffectsVersionInfo? FindById(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : AllVersionsList.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    public static AfterEffectsVersionInfo? FindByYearOrAppMajor(int number) =>
        AllVersionsList.FirstOrDefault(v => v.ReleaseYear == number || v.AppMajorVersion == number);

    public static IReadOnlyList<AfterEffectsVersionInfo> FindByFormatMajor(int formatMajor) =>
        AllVersionsList.Where(v => v.FormatMajorVersion == formatMajor).ToList();
}
