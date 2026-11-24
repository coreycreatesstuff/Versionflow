namespace VersionFlow.Core.Models;

public enum ProjectFormatKind
{
    Unknown = 0,
    AepBinary = 1,
    AepxXml = 2
}

public enum CompatibilityStatus
{
    Verified,
    PartiallyVerified,
    Experimental,
    RequiresAfterEffects,
    Unsupported,
    Unknown
}

public enum FeatureClassification
{
    Compatible,
    Convertible,
    Warning,
    Unsupported,
    Critical,
    Unknown
}

public enum ConversionMode
{
    Safe,
    Compatibility,
    Experimental
}

public enum ConversionMethod
{
    StandaloneNative,
    XmlIntermediate,
    AdobeAssisted,
    SemanticReconstruction,
    ExperimentalMetadata
}

public enum DetectionConfidence
{
    Exact,
    FormatRange,
    Heuristic,
    Unknown
}

public enum ConversionJobState
{
    Queued,
    ReadingSource,
    DetectingVersion,
    AnalysingProject,
    AwaitingApproval,
    PlanningConversion,
    TransformingData,
    WritingOutput,
    ValidatingStructure,
    VerifyingTargetCompatibility,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled,
    Blocked
}

public enum VerificationLevel
{
    None = 0,
    FileGenerated = 1,
    StructureValidated = 2,
    TargetApplicationOpened = 3,
    SemanticComparisonPassed = 4,
    RenderComparisonCompleted = 5
}

public enum RuleRiskLevel
{
    Low,
    Moderate,
    High,
    Experimental
}
