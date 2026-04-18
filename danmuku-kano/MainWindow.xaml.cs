using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using damuku_kano.Services;
using System.Runtime.InteropServices;
using WinRT.Interop;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.ApplicationModel;

namespace damuku_kano;

public sealed partial class MainWindow : Window
{
    private const string StartupTaskId = "KanoDanmakuStartupId";
    private NotificationService _notificationService;
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

        _notificationService = new NotificationService();
        _notificationService.OnNewDanmaku += _notificationService_OnNewDanmaku;
        
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
        
        UpdateTrayMenuStrings();
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
        _notificationService_OnNewDanmaku(new damuku_kano.Models.NotificationItem
        {
            AppName = LocalizationService.Instance.TestNotificationAppName,
            Title = LocalizationService.Instance.TestNotificationTitle,
            Message = $"{LocalizationService.Instance.TestNotificationMessagePrefix} - {DateTime.Now:HH:mm:ss}",
            Time = DateTime.Now.ToString("HH:mm")
        });
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    private static void ApplyDanmakuWindowExtendedStyles(System.Windows.Window window)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private static void InitializeDanmakuOverlayWindow(System.Windows.Window window)
    {
        ApplyDanmakuWindowExtendedStyles(window);

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        source?.AddHook(DanmakuOverlayWindowProc);
    }

    private static IntPtr DanmakuOverlayWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    private static System.Windows.Media.Color ToWpfColor(Windows.UI.Color color)
    {
        return System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private class TrackInfo
    {
        public System.Windows.Window Window { get; set; } = null!;
        public System.Windows.FrameworkElement? Element { get; set; }
        public double RightEdge
        {
            get
            {
                if (Window == null) return -1;
                if (Element == null)
                {
                    return -1;
                }

                double x = 0;
                if (Element.GetValue(System.Windows.Controls.Canvas.LeftProperty) is double left && !double.IsNaN(left))
                {
                    x = left;
                }

                double width = Element.ActualWidth > 0 ? Element.ActualWidth : Element.DesiredSize.Width;
                return Window.Left + x + width;
            }
        }
    }

    private sealed class OutlinedTextElement : System.Windows.FrameworkElement
    {
        public string Text { get; init; } = string.Empty;
        public System.Windows.Media.FontFamily FontFamily { get; init; } = new("Microsoft YaHei");
        public double FontSize { get; init; }
        public System.Windows.FontWeight FontWeight { get; init; } = System.Windows.FontWeights.Normal;
        public System.Windows.Media.Brush Fill { get; init; } = System.Windows.Media.Brushes.White;
        public System.Windows.Media.Brush? Stroke { get; init; }
        public double StrokeThickness { get; init; }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        {
            var text = CreateFormattedText();
            return new System.Windows.Size(
                text.WidthIncludingTrailingWhitespace + StrokeThickness * 2,
                text.Height + StrokeThickness * 2);
        }

        protected override void OnRender(System.Windows.Media.DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var text = CreateFormattedText();
            var geometry = text.BuildGeometry(new System.Windows.Point(StrokeThickness, StrokeThickness));
            var pen = Stroke != null && StrokeThickness > 0
                ? new System.Windows.Media.Pen(Stroke, StrokeThickness)
                : null;

            if (pen != null)
            {
                pen.LineJoin = System.Windows.Media.PenLineJoin.Round;
            }

            drawingContext.DrawGeometry(Fill, pen, geometry);
        }

        private System.Windows.Media.FormattedText CreateFormattedText()
        {
            double pixelsPerDip;
            try
            {
                pixelsPerDip = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch
            {
                pixelsPerDip = 1.0;
            }

            return new System.Windows.Media.FormattedText(
                Text,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(
                    FontFamily,
                    System.Windows.FontStyles.Normal,
                    FontWeight,
                    System.Windows.FontStretches.Normal),
                FontSize,
                Fill,
                pixelsPerDip);
        }
    }

    private TrackInfo?[] _tracks = new TrackInfo?[200];

    private async void _notificationService_OnNewDanmaku(damuku_kano.Models.NotificationItem item)
    {
        string msg = $"{item.AppName}: {item.Title} {item.Message}";

        // 采用隐藏引用的 WPF 创建纯透明弹幕层（根据原始代码建议，WPF 更适合带阴影和平滑抗锯齿边缘的透明层效果）
        var danmakuWindow = new System.Windows.Window
        {
            WindowStyle = System.Windows.WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
            Focusable = false,
            IsHitTestVisible = false // 鼠标穿透
        };
        danmakuWindow.SourceInitialized += (_, _) => InitializeDanmakuOverlayWindow(danmakuWindow);

        var screenWidth = System.Windows.SystemParameters.WorkArea.Width;
        var screenHeight = System.Windows.SystemParameters.WorkArea.Height;
        
        // Base Font size is 36, Slider is percentage (50~300)
        double fontSizePct = FontSizeSlider != null ? FontSizeSlider.Value : 100;
        double fontSize = 36 * (fontSizePct / 100.0);
        
        double trackHeight = fontSize + 24; // + padding
        
        int totalTracks = (int)(screenHeight / trackHeight);
        if (totalTracks > _tracks.Length) totalTracks = _tracks.Length;

        // Display Area percentage
        double dsAreaPct = DisplayAreaSlider != null ? DisplayAreaSlider.Value : 100.0;
        int endTrack = (int)(totalTracks * (dsAreaPct / 100.0));
        if (endTrack < 1) endTrack = 1;
        if (endTrack > totalTracks) endTrack = totalTracks;
        int startTrack = 0;
        
        int selectedTrack = -1;
        System.Collections.Generic.List<int> availableTracks = new System.Collections.Generic.List<int>();
        
        // Define gap based on density
        double minGap = 100;
        if (DensityMore?.IsChecked == true) minGap = 20;
        else if (DensityOverlap?.IsChecked == true) minGap = -300; // allow overlap

        for (int i = startTrack; i < endTrack; i++)
        {
            var track = _tracks[i];
            if (track == null || track.RightEdge < screenWidth - minGap)
            {
                availableTracks.Add(i);
            }
        }

        double spawnX = screenWidth;

        if (availableTracks.Count == 0)
        {
            selectedTrack = new Random().Next(startTrack, endTrack);
            if (DensityNormal?.IsChecked == true)
            {
                // Push it to the right of the existing one to Strictly avoid overlap
                var tk = _tracks[selectedTrack];
                if (tk != null && tk.RightEdge > spawnX - minGap)
                {
                    spawnX = tk.RightEdge + minGap;
                }
            }
        }
        else
        {
            selectedTrack = availableTracks[new Random().Next(0, availableTracks.Count)];
        }

        int y = (int)(selectedTrack * trackHeight);

        string selectedFontName = GetSelectedFontFamilyName();
        System.Windows.Media.FontFamily fontFamily = string.IsNullOrWhiteSpace(selectedFontName)
            ? new System.Windows.Media.FontFamily("Microsoft YaHei")
            : new System.Windows.Media.FontFamily(selectedFontName);

        var danmakuBrush = new System.Windows.Media.SolidColorBrush(ToWpfColor(DanmakuColor.Color));
        double borderThickness = BorderToggle?.IsOn == true ? Math.Max(0, BorderThicknessSlider.Value) : 0;
        System.Windows.Media.Brush? borderBrush = borderThickness > 0
            ? new System.Windows.Media.SolidColorBrush(ToWpfColor(BorderColor.Color))
            : null;

        // Load Icon
        System.Windows.Media.ImageSource? iconSource = null;
        if (item.AppLogoStream != null)
        {
            try
            {
                using var stream = await item.AppLogoStream.OpenReadAsync();
                var memStream = new System.IO.MemoryStream();
                using (var netStream = stream.AsStreamForRead())
                {
                    await netStream.CopyToAsync(memStream);
                }
                memStream.Position = 0;
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memStream;
                bitmap.EndInit();
                bitmap.Freeze();
                iconSource = bitmap;
            }
            catch { }
        }

        var textBlock = new OutlinedTextElement
        {
            Text = msg,
            FontSize = fontSize,
            FontFamily = fontFamily,
            FontWeight = BoldToggle?.IsOn != false ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
            Fill = danmakuBrush,
            Stroke = borderBrush,
            StrokeThickness = borderThickness,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            SnapsToDevicePixels = false
        };

        var effect = ShadowToggle?.IsOn != false ? new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = ToWpfColor(ShadowColor.Color),
            BlurRadius = Math.Max(0, ShadowBlurSlider.Value),
            ShadowDepth = Math.Max(0, ShadowDepthSlider.Value),
            Opacity = Math.Clamp(ShadowOpacitySlider.Value / 100.0, 0, 1)
        } : null;

        var stackPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new System.Windows.Thickness(10),
            Opacity = OpacitySlider != null ? OpacitySlider.Value / 100.0 : 1.0,
            Effect = effect,
            CacheMode = new System.Windows.Media.BitmapCache { EnableClearType = false, SnapsToDevicePixels = false }
        };

        if (iconSource != null)
        {
            stackPanel.Children.Add(new System.Windows.Controls.Image
            {
                Source = iconSource,
                Width = fontSize,
                Height = fontSize,
                Margin = new System.Windows.Thickness(0, 0, 8, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
        }
        
        stackPanel.Children.Add(textBlock);

        danmakuWindow.Left = spawnX;
        danmakuWindow.Top = y;
        danmakuWindow.Content = stackPanel;

        double currentX = spawnX;
        double speed = SpeedSlider != null ? SpeedSlider.Value : 6.0;

        stackPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetX = -stackPanel.DesiredSize.Width - 50;

        _tracks[selectedTrack] = new TrackInfo { Window = danmakuWindow, Element = stackPanel };

        danmakuWindow.Show();

        // 再次确认 overlay 样式，防止 WPF 在 Show 过程中覆盖扩展样式。
        ApplyDanmakuWindowExtendedStyles(danmakuWindow);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(danmakuWindow).Handle;

        // 针对具体的弹幕窗口，进一步确保 CompositionTarget 采用硬件渲染
        var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        if (hwndSource != null && hwndSource.CompositionTarget != null)
        {
            hwndSource.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.Default;
        }

        // Animate the top-level transparent window based on monitor frame deltas.
        long lastTime = 0;
        EventHandler? renderingHandler = null;
        renderingHandler = (s, e) =>
        {
            var args = (System.Windows.Media.RenderingEventArgs)e;
            if (lastTime == 0)
            {
                lastTime = args.RenderingTime.Ticks;
                return;
            }

            double dtFrames = (args.RenderingTime.Ticks - lastTime) / 166666.666;
            lastTime = args.RenderingTime.Ticks;

            currentX -= speed * dtFrames;

            if (currentX <= targetX)
            {
                if (renderingHandler != null)
                {
                    System.Windows.Media.CompositionTarget.Rendering -= renderingHandler;
                }

                danmakuWindow.Close();
                if (_tracks[selectedTrack]?.Window == danmakuWindow)
                {
                    _tracks[selectedTrack] = null;
                }
            }
            else
            {
                danmakuWindow.Left = currentX;
            }
        };

        System.Windows.Media.CompositionTarget.Rendering += renderingHandler;
    }
}
