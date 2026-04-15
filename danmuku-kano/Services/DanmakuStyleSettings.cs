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
    public int FontFamilyIndex { get; init; }
    public bool Bold { get; init; }
    public bool Shadow { get; init; }
    public Color Color { get; init; }

    public double FontSize => 36 * (FontSizePercent / 100.0);

    public double PixelsPerSecond => Math.Max(1, Speed) * 60.0;

    public string FontFamilyName => FontFamilyIndex switch
    {
        1 => "Microsoft YaHei",
        2 => "SimHei",
        3 => "KaiTi",
        _ => "Microsoft YaHei"
    };

    public static DanmakuStyleSettings Load()
    {
        return new DanmakuStyleSettings
        {
            FontSizePercent = SettingsService.Get<double>("FontSize", 100),
            Speed = SettingsService.Get<double>("Speed", 6),
            OpacityPercent = SettingsService.Get<double>("Opacity", 100),
            DisplayAreaPercent = SettingsService.Get<double>("DisplayArea", 100),
            Density = SettingsService.Get<int>("Density", 0),
            FontFamilyIndex = SettingsService.Get<int>("FontFamily", 0),
            Bold = SettingsService.Get<bool>("Bold", true),
            Shadow = SettingsService.Get<bool>("Shadow", true),
            Color = ParseColor(SettingsService.Get<string>("DanmakuColor"))
        };
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
