namespace VersionFlow.Core.Errors;

public static class ErrorCodes
{
    public const string InvalidSignature = "VF-FMT-001";
    public const string TruncatedStructure = "VF-FMT-002";
    public const string MalformedXml = "VF-FMT-003";
    public const string ResourceLimitExceeded = "VF-FMT-004";
    public const string UnknownProjectVersion = "VF-VER-001";
    public const string UnsupportedFeature = "VF-CMP-001";
    public const string NoVerifiedStrategy = "VF-CON-001";
    public const string ApprovalRequired = "VF-CON-002";
    public const string ExperimentalAckRequired = "VF-CON-003";
    public const string OutputWriteFailed = "VF-IO-001";
    public const string SourceUnreadable = "VF-IO-002";
    public const string ValidationFailed = "VF-VAL-001";
    public const string TargetAeUnavailable = "VF-AE-001";
}

public sealed class VersionFlowException : Exception
{
    public string ErrorCode { get; }
    public IReadOnlyList<string> RecoveryActions { get; }

    public VersionFlowException(
        string errorCode,
        string message,
        IReadOnlyList<string>? recoveryActions = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        RecoveryActions = recoveryActions ?? [];
    }
}
