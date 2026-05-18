using System;
using System.Globalization;
using System.Windows.Media;

namespace damuku_kano.Services;

public sealed class DanmakuStyleSettings
{
    public double FontSizePercent { get; init; }
    public double Speed { get; init; }
    public double OpacityPercent { get; init; }
    public double DisplayAreaPercent { get; init; }
    public int Density { get; init; }
    public string FontFamilyName { get; init; } = "Microsoft YaHei";
    public bool Bold { get; init; }
    public bool Shadow { get; init; }
    public Color Color { get; init; }
    public double BorderThickness { get; init; }
    public Color BorderColor { get; init; }
    public double ShadowBlur { get; init; }
    public double ShadowDepth { get; init; }
    public double ShadowOpacity { get; init; }
    public Color ShadowColor { get; init; }
    public int DisplayScreenMode { get; init; }

    public double FontSize => 36 * (FontSizePercent / 100.0);

    public double PixelsPerSecond => Math.Max(1, Speed) * 60.0;

    public static DanmakuStyleSettings Load()
    {
        return new DanmakuStyleSettings
        {
            FontSizePercent = SettingsService.Get<double>("FontSize", 100),
            Speed = SettingsService.Get<double>("Speed", 6),
            OpacityPercent = SettingsService.Get<double>("Opacity", 100),
            DisplayAreaPercent = SettingsService.Get<double>("DisplayArea", 100),
            Density = SettingsService.Get<int>("Density", 0),
            FontFamilyName = ResolveFontFamilyName(SettingsService.Get<string>("FontFamilyName")),
            Bold = SettingsService.Get<bool>("Bold", true),
            Shadow = SettingsService.Get<bool>("Shadow", true),
            Color = ParseColor(SettingsService.Get<string>("DanmakuColor")),
            BorderThickness = SettingsService.Get<bool>("Border", false)
                ? Math.Max(0, SettingsService.Get<double>("BorderThickness", 2))
                : 0,
            BorderColor = ParseColor(SettingsService.Get<string>("BorderColor")),
            ShadowBlur = Math.Max(0, SettingsService.Get<double>("ShadowBlur", 4)),
            ShadowDepth = Math.Max(0, SettingsService.Get<double>("ShadowDepth", 2)),
            ShadowOpacity = Math.Clamp(SettingsService.Get<double>("ShadowOpacity", 100) / 100.0, 0, 1),
            ShadowColor = ParseColor(SettingsService.Get<string>("ShadowColor")),
            DisplayScreenMode = SettingsService.Get<int>("DisplayScreenMode", 0)
        };
    }

    private static string ResolveFontFamilyName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? "Microsoft YaHei" : name;
    }

    private static Color ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Colors.White;
        }

        var parts = text.Split(',');
        if (parts.Length != 4)
        {
            return Colors.White;
        }

        if (!byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte a))
        {
            return Colors.White;
        }

        if (!byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r))
        {
            return Colors.White;
        }

        if (!byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g))
        {
            return Colors.White;
        }

        if (!byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
        {
            return Colors.White;
        }

        return Color.FromArgb(a, r, g, b);
    }
}
