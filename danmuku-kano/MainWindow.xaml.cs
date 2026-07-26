using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using damuku_kano.Services;
using WinRT.Interop;
using System.Windows.Media;
using Windows.ApplicationModel;

namespace damuku_kano;

public sealed partial class MainWindow : Window
{
    private const string StartupTaskId = "KanoDanmakuStartupId";
    private NotificationService _notificationService;
    private Direct2DDanmakuRenderer _renderer;
    private System.Windows.Forms.NotifyIcon _notifyIcon = null!;
    private bool _isUpdatingAutoStartToggle;
    private bool _isApplyingSettings;

    public string AboutVersionText => string.Format(
        LocalizationService.Instance.AboutVersionTemplate,
        GetAppVersion(),
        Environment.Is64BitProcess ? LocalizationService.Instance.AboutArchitecture64 : LocalizationService.Instance.AboutArchitecture32);

    public MainWindow()
    {
        this.InitializeComponent();
        this.ExtendsContentIntoTitleBar = true; // Make it look modern like Windows 11
        
        // 强制 WPF 进程层面使用 GPU 默认硬件加速渲染（防止透明窗口回退到 CPU 级别的软件渲染）
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

        // 设置窗口左上角和任务栏运行时的图标 (需确保有 Assets\AppIcon.ico 文件)
        try
        {
            var pngPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "B_1720288456383.png");
            if (System.IO.File.Exists(pngPath))
            {
                using var bmp = new System.Drawing.Bitmap(pngPath);
                var hIcon = bmp.GetHicon();
                var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
                this.AppWindow.SetIcon(iconId);
            }
        }
        catch { }

        SetupTrayIcon();

        this.AppWindow.Closing += AppWindow_Closing;

        LoadSettings();
        SetupSettingsAutoSave();
        _ = SyncAutoStartToggleAsync();

        _renderer = new Direct2DDanmakuRenderer();
        ApplyPerformanceMode();
        _renderer.Start();

        _notificationService = new NotificationService();
        _notificationService.OnNewDanmaku += OnNewDanmaku;

        HistoryList.ItemsSource = _notificationService.History;

        NavView.SelectedItem = NavView.MenuItems[0];

        _ = _notificationService.InitializeAsync();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ShowPane("Settings");
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            ShowPane(item.Tag?.ToString());
        }
    }

    private void ShowPane(string? tag)
    {
        StylePane.Visibility = tag == "Style" ? Visibility.Visible : Visibility.Collapsed;
        SystemSettingsPane.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        AboutPane.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPane.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
        }
    }

    private void SetupTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon();
        
        try
        {
            var pngPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "B_1720288456383.png");
            if (System.IO.File.Exists(pngPath))
            {
                using var bmp = new System.Drawing.Bitmap(pngPath);
                var hIcon = bmp.GetHicon();
                _notifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
            }
            else
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
        }
        catch 
        {
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }
        
        _notifyIcon.Text = LocalizationService.Instance.AppTitle;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (s, e) =>
        {
            this.AppWindow.Show();
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = new System.Windows.Forms.ToolStripMenuItem(LocalizationService.Instance.TrayAppShow);
        showItem.Click += (s, e) => this.AppWindow.Show();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem(LocalizationService.Instance.TrayAppExit);
        exitItem.Click += (s, e) =>
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _renderer.Dispose();
            Microsoft.UI.Xaml.Application.Current.Exit();
        };
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (CloseActionPrompt != null && CloseActionPrompt.IsChecked == true)
        {
            args.Cancel = true;
            // Prevent reentry if dialog is already showing by temporary uncheck
            CloseActionPrompt.IsChecked = false;
            
            var dialog = new ContentDialog
            {
                Title = LocalizationService.Instance.DialogTitle,
                Content = CloseActionMinimize.IsChecked == true ? LocalizationService.Instance.DialogConfirmMin : LocalizationService.Instance.DialogConfirmExit,
                PrimaryButtonText = LocalizationService.Instance.DialogConfirm,
                CloseButtonText = LocalizationService.Instance.DialogCancel,
                XamlRoot = this.Content.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                CloseActionPrompt.IsChecked = true;
                return;
            }
        }

        if (CloseActionMinimize != null && CloseActionMinimize.IsChecked == true)
        {
            args.Cancel = true;
            this.AppWindow.Hide();
        }
        else
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _renderer.Dispose();
        }
    }

    private void SaveSettings()
    {
        if (_isApplyingSettings)
        {
            return;
        }

        Services.SettingsService.Set("FontSize", FontSizeSlider.Value);
        Services.SettingsService.Set("Speed", SpeedSlider.Value);
        Services.SettingsService.Set("Opacity", OpacitySlider.Value);
        Services.SettingsService.Set("DisplayArea", DisplayAreaSlider.Value);
        Services.SettingsService.Set("Density", DensityNormal.IsChecked == true ? 0 : (DensityMore.IsChecked == true ? 1 : 2));
        Services.SettingsService.Set("DisplayScreenMode", ScreenPrimary.IsChecked == true ? 0 : (ScreenAll.IsChecked == true ? 1 : (ScreenSpan.IsChecked == true ? 3 : 2)));
        Services.SettingsService.Set("FontFamilyName", GetSelectedFontFamilyName());
        
        var color = DanmakuColor.Color;
        Services.SettingsService.Set("DanmakuColor", SerializeColor(color));
        Services.SettingsService.Set("BorderColor", SerializeColor(BorderColor.Color));
        Services.SettingsService.Set("ShadowColor", SerializeColor(ShadowColor.Color));
        
        Services.SettingsService.Set("Bold", BoldToggle.IsOn);
        Services.SettingsService.Set("Border", BorderToggle.IsOn);
        Services.SettingsService.Set("BorderThickness", BorderThicknessSlider.Value);
        Services.SettingsService.Set("Shadow", ShadowToggle.IsOn);
        Services.SettingsService.Set("ShadowBlur", ShadowBlurSlider.Value);
        Services.SettingsService.Set("ShadowDepth", ShadowDepthSlider.Value);
        Services.SettingsService.Set("ShadowOpacity", ShadowOpacitySlider.Value);
        
        int langIndex = LocalizationService.NormalizeLanguageIndex(LanguageCombo.SelectedIndex);
        int savedLangIndex = Services.SettingsService.Get<int>("Language", 0);
        Services.SettingsService.Set("Language", langIndex);
        if (langIndex != savedLangIndex)
        {
            string selectedFontName = GetSelectedFontFamilyName();
            LocalizationService.Instance.ApplyLanguage(langIndex);
            Bindings.Update();
            PopulateFontFamilyCombo(selectedFontName);
        }
        
        Services.SettingsService.Set("CloseAction", CloseActionMinimize.IsChecked == true ? 0 : 1);
        Services.SettingsService.Set("ClosePrompt", CloseActionPrompt.IsChecked == true);

        Services.SettingsService.Set("PerformanceMode", GetSelectedPerformanceMode());
        ApplyPerformanceMode();

        UpdateTrayMenuStrings();
    }

    private int GetSelectedPerformanceMode()
    {
        if (PerfBalanced.IsChecked == true) return 1;
        if (PerfGame.IsChecked == true) return 2;
        if (PerfEco.IsChecked == true) return 3;
        return 0;
    }

    private static int MapPerformanceModeToFps(int mode)
    {
        return mode switch
        {
            1 => 60,
            2 => 30,
            3 => 15,
            _ => 0 // 跟随屏幕刷新率
        };
    }

    private void ApplyPerformanceMode()
    {
        _renderer?.SetMaxFps(MapPerformanceModeToFps(GetSelectedPerformanceMode()));
    }
    
    private void UpdateTrayMenuStrings()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Text = LocalizationService.Instance.AppTitle;
            if (_notifyIcon.ContextMenuStrip != null && _notifyIcon.ContextMenuStrip.Items.Count >= 2)
            {
                _notifyIcon.ContextMenuStrip.Items[0].Text = LocalizationService.Instance.TrayAppShow;
                _notifyIcon.ContextMenuStrip.Items[1].Text = LocalizationService.Instance.TrayAppExit;
            }
        }
    }

    private void PopulateFontFamilyCombo(string? selectedFontName)
    {
        bool wasApplyingSettings = _isApplyingSettings;
        _isApplyingSettings = true;

        try
        {
            FontFamilyCombo.Items.Clear();
            FontFamilyCombo.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Instance.TextDefaultFont,
                Tag = string.Empty
            });

            foreach (string fontName in System.Windows.Media.Fonts.SystemFontFamilies
                         .Select(font => font.Source)
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Distinct(StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
            {
                FontFamilyCombo.Items.Add(new ComboBoxItem
                {
                    Content = fontName,
                    Tag = fontName
                });
            }

            SelectFontFamily(selectedFontName);
        }
        finally
        {
            _isApplyingSettings = wasApplyingSettings;
        }
    }

    private void SelectFontFamily(string? fontName)
    {
        string normalizedFontName = fontName ?? string.Empty;

        for (int i = 0; i < FontFamilyCombo.Items.Count; i++)
        {
            if (FontFamilyCombo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString() ?? string.Empty, normalizedFontName, StringComparison.CurrentCultureIgnoreCase))
            {
                FontFamilyCombo.SelectedIndex = i;
                return;
            }
        }

        FontFamilyCombo.SelectedIndex = 0;
    }

    private string GetSelectedFontFamilyName()
    {
        if (FontFamilyCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string GetLegacyFontFamilyName(int selectedIndex)
    {
        return selectedIndex switch
        {
            1 => "微软雅黑",
            2 => "黑体",
            3 => "楷体",
            _ => string.Empty
        };
    }

    private static string SerializeColor(Windows.UI.Color color)
    {
        return $"{color.A},{color.R},{color.G},{color.B}";
    }

    private static bool TryParseColor(string? value, out Windows.UI.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        if (byte.TryParse(parts[0], out byte a) &&
            byte.TryParse(parts[1], out byte r) &&
            byte.TryParse(parts[2], out byte g) &&
            byte.TryParse(parts[3], out byte b))
        {
            color = Windows.UI.Color.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    private void LoadSettings()
    {
        _isApplyingSettings = true;

        try
        {
            FontSizeSlider.Value = Services.SettingsService.Get<double>("FontSize", 100);
            SpeedSlider.Value = Services.SettingsService.Get<double>("Speed", 6);
            OpacitySlider.Value = Services.SettingsService.Get<double>("Opacity", 100);
            DisplayAreaSlider.Value = Services.SettingsService.Get<double>("DisplayArea", 100);
            
            int den = Services.SettingsService.Get<int>("Density", 0);
            if (den == 0) DensityNormal.IsChecked = true;
            else if (den == 1) DensityMore.IsChecked = true;
            else DensityOverlap.IsChecked = true;

            int screenMode = Services.SettingsService.Get<int>("DisplayScreenMode", 0);
            if (screenMode == 0) ScreenPrimary.IsChecked = true;
            else if (screenMode == 1) ScreenAll.IsChecked = true;
            else if (screenMode == 3) ScreenSpan.IsChecked = true;
            else ScreenMouse.IsChecked = true;

            string? selectedFontName = Services.SettingsService.Get<string>("FontFamilyName");
            if (string.IsNullOrWhiteSpace(selectedFontName))
            {
                selectedFontName = GetLegacyFontFamilyName(Services.SettingsService.Get<int>("FontFamily", 0));
            }

            PopulateFontFamilyCombo(selectedFontName);
            
            if (TryParseColor(Services.SettingsService.Get<string>("DanmakuColor"), out var danmakuColor))
            {
                DanmakuColor.Color = danmakuColor;
            }

            if (TryParseColor(Services.SettingsService.Get<string>("BorderColor"), out var borderColor))
            {
                BorderColor.Color = borderColor;
            }

            if (TryParseColor(Services.SettingsService.Get<string>("ShadowColor"), out var shadowColor))
            {
                ShadowColor.Color = shadowColor;
            }
            
            BoldToggle.IsOn = Services.SettingsService.Get<bool>("Bold", true);
            BorderToggle.IsOn = Services.SettingsService.Get<bool>("Border", false);
            BorderThicknessSlider.Value = Services.SettingsService.Get<double>("BorderThickness", 2);
            ShadowToggle.IsOn = Services.SettingsService.Get<bool>("Shadow", true);
            ShadowBlurSlider.Value = Services.SettingsService.Get<double>("ShadowBlur", 4);
            ShadowDepthSlider.Value = Services.SettingsService.Get<double>("ShadowDepth", 2);
            ShadowOpacitySlider.Value = Services.SettingsService.Get<double>("ShadowOpacity", 100);
            LanguageCombo.SelectedIndex = LocalizationService.NormalizeLanguageIndex(Services.SettingsService.Get<int>("Language", 0));
            
            int ca = Services.SettingsService.Get<int>("CloseAction", 0);
            if (ca == 0) CloseActionMinimize.IsChecked = true;
            else CloseActionExit.IsChecked = true;
            
            CloseActionPrompt.IsChecked = Services.SettingsService.Get<bool>("ClosePrompt", false);
            AutoStartToggle.IsOn = Services.SettingsService.Get<bool>("AutoStart", false);

            int perfMode = Services.SettingsService.Get<int>("PerformanceMode", 0);
            if (perfMode == 1) PerfBalanced.IsChecked = true;
            else if (perfMode == 2) PerfGame.IsChecked = true;
            else if (perfMode == 3) PerfEco.IsChecked = true;
            else PerfFluent.IsChecked = true;
        }
        catch { }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void SetupSettingsAutoSave()
    {
        FontSizeSlider.ValueChanged += (s,e) => SaveSettings();
        SpeedSlider.ValueChanged += (s,e) => SaveSettings();
        OpacitySlider.ValueChanged += (s,e) => SaveSettings();
        DisplayAreaSlider.ValueChanged += (s,e) => SaveSettings();
        DensityNormal.Checked += (s,e) => SaveSettings();
        DensityMore.Checked += (s,e) => SaveSettings();
        DensityOverlap.Checked += (s,e) => SaveSettings();
        ScreenPrimary.Checked += (s,e) => SaveSettings();
        ScreenAll.Checked += (s,e) => SaveSettings();
        ScreenSpan.Checked += (s,e) => SaveSettings();
        ScreenMouse.Checked += (s,e) => SaveSettings();
        FontFamilyCombo.SelectionChanged += (s,e) => SaveSettings();
        DanmakuColor.ColorChanged += (s,e) => SaveSettings();
        BorderColor.ColorChanged += (s,e) => SaveSettings();
        ShadowColor.ColorChanged += (s,e) => SaveSettings();
        BoldToggle.Toggled += (s,e) => SaveSettings();
        BorderToggle.Toggled += (s,e) => SaveSettings();
        BorderThicknessSlider.ValueChanged += (s,e) => SaveSettings();
        ShadowToggle.Toggled += (s,e) => SaveSettings();
        ShadowBlurSlider.ValueChanged += (s,e) => SaveSettings();
        ShadowDepthSlider.ValueChanged += (s,e) => SaveSettings();
        ShadowOpacitySlider.ValueChanged += (s,e) => SaveSettings();
        LanguageCombo.SelectionChanged += (s,e) => SaveSettings();
        CloseActionMinimize.Checked += (s,e) => SaveSettings();
        CloseActionExit.Checked += (s,e) => SaveSettings();
        CloseActionPrompt.Checked += (s,e) => SaveSettings();
        CloseActionPrompt.Unchecked += (s,e) => SaveSettings();
        PerfFluent.Checked += (s,e) => SaveSettings();
        PerfBalanced.Checked += (s,e) => SaveSettings();
        PerfGame.Checked += (s,e) => SaveSettings();
        PerfEco.Checked += (s,e) => SaveSettings();
        AutoStartToggle.Toggled += async (s,e) => 
        {
            if (_isUpdatingAutoStartToggle)
            {
                return;
            }

            await SetAutoStartAsync(AutoStartToggle.IsOn);
        };
    }

    private async Task SyncAutoStartToggleAsync()
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskId);
            ApplyStartupTaskStateToUi(startupTask.State);
        }
        catch
        {
            _isUpdatingAutoStartToggle = true;
            AutoStartToggle.IsOn = false;
            AutoStartToggle.IsEnabled = false;
            _isUpdatingAutoStartToggle = false;
            Services.SettingsService.Set("AutoStart", false);
        }
    }

    private async Task SetAutoStartAsync(bool enable)
    {
        try
        {
            AutoStartToggle.IsEnabled = false;

            var startupTask = await StartupTask.GetAsync(StartupTaskId);
            var state = startupTask.State;

            if (enable)
            {
                if (state == StartupTaskState.Disabled)
                {
                    state = await startupTask.RequestEnableAsync();
                }
                else if (state == StartupTaskState.DisabledByUser)
                {
                    await ShowStartupTaskMessageAsync(LocalizationService.Instance.StartupDisabledByUserMessage);
                }
                else if (state == StartupTaskState.DisabledByPolicy)
                {
                    await ShowStartupTaskMessageAsync(LocalizationService.Instance.StartupDisabledByPolicyMessage);
                }
            }
            else
            {
                if (state == StartupTaskState.Enabled)
                {
                    startupTask.Disable();
                    state = startupTask.State;
                }
                else if (state == StartupTaskState.EnabledByPolicy)
                {
                    await ShowStartupTaskMessageAsync(LocalizationService.Instance.StartupEnabledByPolicyMessage);
                }
            }

            ApplyStartupTaskStateToUi(state);
        }
        catch
        {
            await ShowStartupTaskMessageAsync(LocalizationService.Instance.StartupTaskUnavailableMessage);
            await SyncAutoStartToggleAsync();
        }
    }

    private void ApplyStartupTaskStateToUi(StartupTaskState state)
    {
        bool enabled = state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy;
        bool configurable = state != StartupTaskState.DisabledByPolicy && state != StartupTaskState.EnabledByPolicy;

        _isUpdatingAutoStartToggle = true;
        AutoStartToggle.IsOn = enabled;
        AutoStartToggle.IsEnabled = configurable;
        _isUpdatingAutoStartToggle = false;

        Services.SettingsService.Set("AutoStart", enabled);
    }

    private async Task ShowStartupTaskMessageAsync(string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationService.Instance.DialogTitle,
                Content = message,
                CloseButtonText = LocalizationService.Instance.DialogConfirm,
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch
        {
        }
    }

    private void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        var settings = DanmakuStyleSettings.Load();
        string text = $"{LocalizationService.Instance.TestNotificationAppName}: {LocalizationService.Instance.TestNotificationTitle} {LocalizationService.Instance.TestNotificationMessagePrefix} - {DateTime.Now:HH:mm:ss}";
        _renderer.ShowDanmaku(text, null, settings);
    }

    private async void OnNewDanmaku(damuku_kano.Models.NotificationItem item)
    {
        string text = $"{item.AppName}: {item.Title} {item.Message}";
        byte[]? iconPng = null;

        if (item.AppLogoStream != null)
        {
            try
            {
                using var stream = await item.AppLogoStream.OpenReadAsync();
                using var memStream = new MemoryStream();
                using (var netStream = stream.AsStreamForRead())
                {
                    await netStream.CopyToAsync(memStream);
                }
                iconPng = memStream.ToArray();
            }
            catch { }
        }

        var settings = DanmakuStyleSettings.Load();
        _renderer.ShowDanmaku(text, iconPng, settings);
    }
}
