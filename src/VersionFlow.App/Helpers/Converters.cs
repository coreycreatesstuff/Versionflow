using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VersionFlow.Core.Models;

namespace VersionFlow.App.Helpers;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value switch
        {
            string s => !string.IsNullOrWhiteSpace(s),
            int count => count > 0,
            _ => value is not null
        };
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null ? parameter : Binding.DoNothing;
}

public sealed partial class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        return value switch
        {
            ConversionMode.Safe => "Safe Mode",
            ConversionMode.Compatibility => "Compatibility Mode — Recommended",
            ConversionMode.Experimental => "Experimental Mode",
            CompatibilityStatus.PartiallyVerified => "Partially Verified",
            CompatibilityStatus.RequiresAfterEffects => "Requires After Effects",
            ConversionMethod.StandaloneNative => "Standalone Native",
            ConversionMethod.XmlIntermediate => "XML Intermediate",
            ConversionMethod.AdobeAssisted => "Adobe-Assisted",
            ConversionMethod.SemanticReconstruction => "Semantic Reconstruction",
            ConversionMethod.ExperimentalMetadata => "Experimental Metadata",
            ConversionJobState.Completed => "Conversion complete",
            ConversionJobState.CompletedWithWarnings => "Converted with warnings",
            ConversionJobState.AwaitingApproval => "Awaiting approval",
            ConversionJobState.Blocked => "Conversion blocked",
            ConversionJobState.Failed => "Conversion failed",
            ConversionJobState.Cancelled => "Conversion cancelled",
            VerificationLevel.StructureValidated => "Structure Validated",
            VerificationLevel.TargetApplicationOpened => "Verified in After Effects",
            VerificationLevel.FileGenerated => "File Generated",
            _ => SplitPascalCase(value.ToString() ?? string.Empty)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex PascalBoundaryRegex();

    private static string SplitPascalCase(string input) =>
        PascalBoundaryRegex().Replace(input, " $1");
}

/// <summary>
/// Maps status strings or enums to semantic Foreground, Background (rgba tint), or Border brushes.
/// Parameter: "fg" (default), "bg", or "border".
/// </summary>
public sealed class StatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessFg = CreateFrozen(0xFF, 0x34, 0xD3, 0x99);
    private static readonly SolidColorBrush SuccessBg = CreateFrozen(0x1E, 0x34, 0xD3, 0x99);
    private static readonly SolidColorBrush SuccessBd = CreateFrozen(0x4A, 0x34, 0xD3, 0x99);

    private static readonly SolidColorBrush WarningFg = CreateFrozen(0xFF, 0xFB, 0xBF, 0x24);
    private static readonly SolidColorBrush WarningBg = CreateFrozen(0x1E, 0xFB, 0xBF, 0x24);
    private static readonly SolidColorBrush WarningBd = CreateFrozen(0x4A, 0xFB, 0xBF, 0x24);

    private static readonly SolidColorBrush ErrorFg = CreateFrozen(0xFF, 0xF8, 0x71, 0x71);
    private static readonly SolidColorBrush ErrorBg = CreateFrozen(0x1E, 0xF8, 0x71, 0x71);
    private static readonly SolidColorBrush ErrorBd = CreateFrozen(0x4A, 0xF8, 0x71, 0x71);

    private static readonly SolidColorBrush AccentFg = CreateFrozen(0xFF, 0xC4, 0xB5, 0xFD);
    private static readonly SolidColorBrush AccentBg = CreateFrozen(0x24, 0x8B, 0x5C, 0xF6);
    private static readonly SolidColorBrush AccentBd = CreateFrozen(0x52, 0x8B, 0x5C, 0xF6);

    private static readonly SolidColorBrush NeutralFg = CreateFrozen(0xFF, 0xA7, 0xA7, 0xBC);
    private static readonly SolidColorBrush NeutralBg = CreateFrozen(0x1A, 0xA7, 0xA7, 0xBC);
    private static readonly SolidColorBrush NeutralBd = CreateFrozen(0x36, 0xA7, 0xA7, 0xBC);

    private static SolidColorBrush CreateFrozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string kind = (parameter as string)?.ToLowerInvariant() ?? "fg";
        string text = value?.ToString() ?? string.Empty;

        int tone = text switch
        {
            "Verified" or "PartiallyVerified" or "Compatible" or "Ready" or "Completed" or "StructureValidated" or "TargetApplicationOpened" or "Found" => 1, // green
            "Experimental" or "Warning" or "CompletedWithWarnings" or "AwaitingApproval" or "RequiresAfterEffects" or "Sequence" => 2, // amber
            "Unsupported" or "Blocked" or "Failed" or "Critical" or "Parse failed" or "Missing" => 3, // red
            "Convertible" or "StandaloneNative" or "Exact" => 4, // purple
            _ when text.Contains("warning", StringComparison.OrdinalIgnoreCase) => 2,
            _ when text.Contains("unsupported", StringComparison.OrdinalIgnoreCase) => 3,
            _ => 0 // neutral
        };

        return (tone, kind) switch
        {
            (1, "bg") => SuccessBg,
            (1, "border") => SuccessBd,
            (1, _) => SuccessFg,
            (2, "bg") => WarningBg,
            (2, "border") => WarningBd,
            (2, _) => WarningFg,
            (3, "bg") => ErrorBg,
            (3, "border") => ErrorBd,
            (3, _) => ErrorFg,
            (4, "bg") => AccentBg,
            (4, "border") => AccentBd,
            (4, _) => AccentFg,
            (_, "bg") => NeutralBg,
            (_, "border") => NeutralBd,
            _ => NeutralFg
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
