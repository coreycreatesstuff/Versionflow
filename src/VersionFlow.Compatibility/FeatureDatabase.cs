namespace VersionFlow.Compatibility;

public sealed record KnownEffectEntry(
    string MatchName,
    string DisplayName,
    int IntroducedFormatMajor,
    int IntroducedAppMajor,
    string Category,
    string? Notes = null);

public sealed record ThirdPartyPluginVendor(
    string Prefix,
    string VendorName,
    string Notes);

public static class FeatureDatabase
{
    public static IReadOnlyList<KnownEffectEntry> BuiltInEffects { get; } =
    [
        new("ADBE Unmult", "Unmult (Native)", 96, 25, "Keying / Transparency", "Introduced natively in modern AE 2025/2026; unavailable in AE 2020–2024."),
        new("ADBE 3D Model", "3D Model Layer", 95, 24, "3D Workspace", "Introduced in AE 2024 Advanced 3D."),
        new("ADBE Parametric Mesh", "Parametric 3D Mesh", 96, 25, "3D Workspace", "Introduced in AE 2025."),
        new("ADBE Object Matte", "AI Object Matte", 96, 25, "Matte / Roto", "Introduced in AE 2025/2026."),
        new("ADBE Gaussian Blur 2", "Gaussian Blur", 90, 13, "Blur & Sharpen"),
        new("ADBE Box Blur2", "Fast Box Blur", 91, 14, "Blur & Sharpen"),
        new("ADBE Lumetri", "Lumetri Color", 90, 13, "Color Correction"),
        new("ADBE 4ColorGradient", "4-Color Gradient", 90, 13, "Generate"),
        new("ADBE Drop Shadow", "Drop Shadow", 90, 13, "Perspective"),
        new("ADBE Fill", "Fill", 90, 13, "Generate"),
        new("ADBE Fractal Noise", "Fractal Noise", 90, 13, "Noise & Grain"),
        new("ADBE Glow2", "Glow", 90, 13, "Stylize"),
        new("ADBE Displacement Map", "Displacement Map", 90, 13, "Distort"),
        new("ADBE CurvesCustom", "Curves", 90, 13, "Color Correction"),
        new("ADBE HUE SATURATION", "Hue/Saturation", 90, 13, "Color Correction"),
        new("ADBE Slider Control", "Slider Control", 90, 13, "Expression Controls"),
        new("ADBE Checkbox Control", "Checkbox Control", 90, 13, "Expression Controls"),
        new("ADBE Dropdown Control", "Dropdown Menu Control", 93, 17, "Expression Controls", "Added in AE 17.0 (2020); text-value expression access expanded later."),
        new("ADBE Angle Control", "Angle Control", 90, 13, "Expression Controls"),
        new("ADBE Color Control", "Color Control", 90, 13, "Expression Controls"),
        new("ADBE Point Control", "Point Control", 90, 13, "Expression Controls")
    ];

    public static IReadOnlyList<ThirdPartyPluginVendor> KnownPluginVendors { get; } =
    [
        new("RG ", "Maxon / Red Giant", "Red Giant plugin reference detected."),
        new("Trapcode", "Maxon / Red Giant Trapcode", "Trapcode plugin reference detected."),
        new("Magic Bullet", "Maxon / Red Giant Magic Bullet", "Magic Bullet plugin reference detected."),
        new("Universe", "Maxon / Red Giant Universe", "Universe plugin reference detected."),
        new("BCC", "Boris FX Continuum", "Boris FX Continuum plugin reference detected."),
        new("Sapphire", "Boris FX Sapphire", "Boris FX Sapphire plugin reference detected."),
        new("Mocha", "Boris FX Mocha", "Mocha plugin reference detected."),
        new("VIDEOCOPILOT", "Video Copilot", "Video Copilot plugin (e.g., Element 3D, Optical Flares, Saber) detected."),
        new("Element", "Video Copilot Element 3D", "Element 3D plugin reference detected."),
        new("RE:", "RE:Vision Effects", "RE:Vision Effects plugin (e.g., RSMB, Twixtor) detected."),
        new("RSMB", "RE:Vision Effects RSMB", "ReelSmart Motion Blur reference detected."),
        new("Twixtor", "RE:Vision Effects Twixtor", "Twixtor reference detected.")
    ];

    public static KnownEffectEntry? FindBuiltInEffect(string matchName) =>
        BuiltInEffects.FirstOrDefault(e => string.Equals(e.MatchName, matchName, StringComparison.OrdinalIgnoreCase));

    public static ThirdPartyPluginVendor? DetectThirdPartyVendor(string matchName)
    {
        if (matchName.StartsWith("ADBE ", StringComparison.Ordinal))
            return null;

        return KnownPluginVendors.FirstOrDefault(v =>
            matchName.Contains(v.Prefix, StringComparison.OrdinalIgnoreCase))
            ?? new ThirdPartyPluginVendor(matchName, "Third-Party Plugin", "Non-Adobe effect match name detected.");
    }
}
