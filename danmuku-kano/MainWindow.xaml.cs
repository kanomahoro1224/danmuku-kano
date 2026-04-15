using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using damuku_kano.Services;

namespace damuku_kano;

public sealed partial class MainWindow : Window
{
    private readonly App _app;

    public MainWindow(App app)
    {
        _app = app;

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        AppWindow.Closing += AppWindow_Closing;

        HistoryList.ItemsSource = _app.NotificationService.History;

        LoadSettings();
        SetupSettingsAutoSave();

        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        await _app.HandleMainWindowClosingAsync(this, args);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            StylePane.Visibility = Visibility.Collapsed;
            SystemSettingsPane.Visibility = Visibility.Visible;
            HistoryPane.Visibility = Visibility.Collapsed;
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        StylePane.Visibility = item.Tag?.ToString() == "Style" ? Visibility.Visible : Visibility.Collapsed;
        SystemSettingsPane.Visibility = Visibility.Collapsed;
        HistoryPane.Visibility = item.Tag?.ToString() == "History" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveSettings()
    {
        SettingsService.Set("FontSize", FontSizeSlider.Value);
        SettingsService.Set("Speed", SpeedSlider.Value);
        SettingsService.Set("Opacity", OpacitySlider.Value);
        SettingsService.Set("DisplayArea", DisplayAreaSlider.Value);
        SettingsService.Set("Density", DensityNormal.IsChecked == true ? 0 : (DensityMore.IsChecked == true ? 1 : 2));
        SettingsService.Set("FontFamily", FontFamilyCombo.SelectedIndex);

        var color = DanmakuColor.Color;
        SettingsService.Set("DanmakuColor", $"{color.A},{color.R},{color.G},{color.B}");

        SettingsService.Set("Bold", BoldToggle.IsOn);
        SettingsService.Set("Shadow", ShadowToggle.IsOn);

        int languageIndex = LanguageCombo.SelectedIndex;
        SettingsService.Set("Language", languageIndex);
        _app.ApplyLanguage(languageIndex);

        SettingsService.Set("CloseAction", CloseActionMinimize.IsChecked == true ? 0 : 1);
        SettingsService.Set("ClosePrompt", CloseActionPrompt.IsChecked == true);
        SettingsService.Set("AutoStart", AutoStartToggle.IsOn);
    }

    private void LoadSettings()
    {
        try
        {
            FontSizeSlider.Value = SettingsService.Get<double>("FontSize", 100);
            SpeedSlider.Value = SettingsService.Get<double>("Speed", 6);
            OpacitySlider.Value = SettingsService.Get<double>("Opacity", 100);
            DisplayAreaSlider.Value = SettingsService.Get<double>("DisplayArea", 100);

            int density = SettingsService.Get<int>("Density", 0);
            if (density == 0)
            {
                DensityNormal.IsChecked = true;
            }
            else if (density == 1)
            {
                DensityMore.IsChecked = true;
            }
            else
            {
                DensityOverlap.IsChecked = true;
            }

            FontFamilyCombo.SelectedIndex = SettingsService.Get<int>("FontFamily", 0);

            var colorText = SettingsService.Get<string>("DanmakuColor");
            if (!string.IsNullOrEmpty(colorText))
            {
                var parts = colorText.Split(',');
                if (parts.Length == 4
                    && byte.TryParse(parts[0], out byte a)
                    && byte.TryParse(parts[1], out byte r)
                    && byte.TryParse(parts[2], out byte g)
                    && byte.TryParse(parts[3], out byte b))
                {
                    DanmakuColor.Color = Windows.UI.Color.FromArgb(a, r, g, b);
                }
            }

            BoldToggle.IsOn = SettingsService.Get<bool>("Bold", true);
            ShadowToggle.IsOn = SettingsService.Get<bool>("Shadow", true);
            LanguageCombo.SelectedIndex = SettingsService.Get<int>("Language", 0);

            int closeAction = SettingsService.Get<int>("CloseAction", 0);
            if (closeAction == 0)
            {
                CloseActionMinimize.IsChecked = true;
            }
            else
            {
                CloseActionExit.IsChecked = true;
            }

            CloseActionPrompt.IsChecked = SettingsService.Get<bool>("ClosePrompt", false);
            AutoStartToggle.IsOn = SettingsService.Get<bool>("AutoStart", false);
        }
        catch
        {
        }
    }

    private void SetupSettingsAutoSave()
    {
        FontSizeSlider.ValueChanged += (_, _) => SaveSettings();
        SpeedSlider.ValueChanged += (_, _) => SaveSettings();
        OpacitySlider.ValueChanged += (_, _) => SaveSettings();
        DisplayAreaSlider.ValueChanged += (_, _) => SaveSettings();
        DensityNormal.Checked += (_, _) => SaveSettings();
        DensityMore.Checked += (_, _) => SaveSettings();
        DensityOverlap.Checked += (_, _) => SaveSettings();
        FontFamilyCombo.SelectionChanged += (_, _) => SaveSettings();
        DanmakuColor.ColorChanged += (_, _) => SaveSettings();
        BoldToggle.Toggled += (_, _) => SaveSettings();
        ShadowToggle.Toggled += (_, _) => SaveSettings();
        LanguageCombo.SelectionChanged += (_, _) => SaveSettings();
        CloseActionMinimize.Checked += (_, _) => SaveSettings();
        CloseActionExit.Checked += (_, _) => SaveSettings();
        CloseActionPrompt.Checked += (_, _) => SaveSettings();
        CloseActionPrompt.Unchecked += (_, _) => SaveSettings();
        AutoStartToggle.Toggled += (_, _) =>
        {
            SaveSettings();
            SetAutoStart(AutoStartToggle.IsOn);
        };
    }

    private void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable)
            {
                key?.SetValue("KanoDanmaku", "\"" + System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName + "\"");
            }
            else
            {
                key?.DeleteValue("KanoDanmaku", false);
            }
        }
        catch
        {
        }
    }

    private void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        _app.ShowTestDanmaku();
    }
}
