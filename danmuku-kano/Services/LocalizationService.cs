using System;
using System.ComponentModel;
using Microsoft.Windows.ApplicationModel.Resources;

namespace damuku_kano.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private ResourceLoader _resourceLoader = new();

    public static LocalizationService Instance { get; } = new LocalizationService();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppTitle => GetString(nameof(AppTitle));
    public string NavStyle => GetString(nameof(NavStyle));
    public string NavHistory => GetString(nameof(NavHistory));
    public string TextProjectBasedOn => GetString(nameof(TextProjectBasedOn));
    public string TextNoNativeTransparency => GetString(nameof(TextNoNativeTransparency));
    public string TextComplexHooks => GetString(nameof(TextComplexHooks));
    public string TextFontSize => GetString(nameof(TextFontSize));
    public string TextSpeed => GetString(nameof(TextSpeed));
    public string TextOpacity => GetString(nameof(TextOpacity));
    public string TextDisplayArea => GetString(nameof(TextDisplayArea));
    public string TextDensity => GetString(nameof(TextDensity));
    public string TextDensityNormal => GetString(nameof(TextDensityNormal));
    public string TextDensityMore => GetString(nameof(TextDensityMore));
    public string TextDensityOverlap => GetString(nameof(TextDensityOverlap));
    public string TextFontAndColor => GetString(nameof(TextFontAndColor));
    public string TextDefaultFont => GetString(nameof(TextDefaultFont));
    public string TextDanmakuColorConfig => GetString(nameof(TextDanmakuColorConfig));
    public string TextBold => GetString(nameof(TextBold));
    public string TextShadow => GetString(nameof(TextShadow));
    public string TextTestDanmaku => GetString(nameof(TextTestDanmaku));
    public string TextSystemSettings => GetString(nameof(TextSystemSettings));
    public string TextLanguage => GetString(nameof(TextLanguage));
    public string TextLanguageHans => GetString(nameof(TextLanguageHans));
    public string TextLanguageHant => GetString(nameof(TextLanguageHant));
    public string TextLanguageEn => GetString(nameof(TextLanguageEn));
    public string TextLanguageJa => GetString(nameof(TextLanguageJa));
    public string TextCloseAction => GetString(nameof(TextCloseAction));
    public string TextMinimizeToTray => GetString(nameof(TextMinimizeToTray));
    public string TextExitApp => GetString(nameof(TextExitApp));
    public string TextClosePrompt => GetString(nameof(TextClosePrompt));
    public string TextAutoStart => GetString(nameof(TextAutoStart));
    public string TrayAppShow => GetString(nameof(TrayAppShow));
    public string TrayAppExit => GetString(nameof(TrayAppExit));
    public string DialogTitle => GetString(nameof(DialogTitle));
    public string DialogConfirmMin => GetString(nameof(DialogConfirmMin));
    public string DialogConfirmExit => GetString(nameof(DialogConfirmExit));
    public string DialogConfirm => GetString(nameof(DialogConfirm));
    public string DialogCancel => GetString(nameof(DialogCancel));
    public string NotificationFallbackAppName => GetString(nameof(NotificationFallbackAppName));
    public string TestNotificationAppName => GetString(nameof(TestNotificationAppName));
    public string TestNotificationTitle => GetString(nameof(TestNotificationTitle));
    public string TestNotificationMessagePrefix => GetString(nameof(TestNotificationMessagePrefix));

    public void ApplySavedLanguage()
    {
        ApplyLanguage(SettingsService.Get<int>("Language", 0));
    }

    public void ApplyLanguage(int languageIndex)
    {
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageIndex switch
            {
                1 => "zh-Hant",
                2 => "en-US",
                3 => "ja-JP",
                _ => "zh-Hans"
            };
        }
        catch
        {
        }

        _resourceLoader = new ResourceLoader();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private string GetString(string key)
    {
        string value = _resourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
