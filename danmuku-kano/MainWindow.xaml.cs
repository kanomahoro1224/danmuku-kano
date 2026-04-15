using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using damuku_kano.Services;
using System.Runtime.InteropServices;
using WinRT.Interop;
using System.Windows.Interop;
using System.Windows.Media;

namespace damuku_kano;

public class MuiHelper : System.ComponentModel.INotifyPropertyChanged
{
    public static MuiHelper Instance { get; } = new MuiHelper();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(null));

    public string AppTitle => "Kano " + Lang("弹幕通知");
    public string NavStyle => Lang("弹幕样式");
    public string NavHistory => Lang("历史通知");
    public string TextProjectBasedOn => Lang("此项目基于 Windows App SDK (WinUI 3)。");
    public string TextNoNativeTransparency => Lang("由于 WinUI 3 目前没有原生的透明无边界窗口支持（类似WPF的AllowsTransparency=True），");
    public string TextComplexHooks => Lang("实现全屏透明的弹幕叠加层需要复杂的 Win32 P/Invoke Hooks。");
    public string TextFontSize => Lang("字体大小(%)");
    public string TextSpeed => Lang("弹幕速度");
    public string TextOpacity => Lang("不透明度(%)");
    public string TextDisplayArea => Lang("显示区域(%)");
    public string TextDensity => Lang("弹幕密度");
    public string TextDensityNormal => Lang("正常");
    public string TextDensityMore => Lang("较多");
    public string TextDensityOverlap => Lang("重叠");
    public string TextFontAndColor => Lang("颜色与字体");
    public string TextDefaultFont => Lang("默认字体");
    public string TextDanmakuColorConfig => Lang("弹幕颜色设定");
    public string TextBold => Lang("使用粗体");
    public string TextShadow => Lang("发光/阴影效果");
    public string TextTestDanmaku => Lang("发送测试通知弹幕 (调试)");
    
    public string TextSystemSettings => Lang("系统设置");
    public string TextLanguage => Lang("界面语言");
    public string TextCloseAction => Lang("关闭主界面时：");
    public string TextMinimizeToTray => Lang("最小化到系统托盘");
    public string TextExitApp => Lang("退出程序");
    public string TextClosePrompt => Lang("关闭时提示");
    public string TextAutoStart => Lang("开机时自动启动");

    public string TrayAppShow => Lang("显示主界面");
    public string TrayAppExit => Lang("完全退出");

    public string DialogTitle => Lang("提示");
    public string DialogConfirmMin => Lang("确定要最小化到系统托盘吗？");
    public string DialogConfirmExit => Lang("确定要退出程序吗？");
    public string DialogConfirm => Lang("确定");
    public string DialogCancel => Lang("取消");

    private string Lang(string chs)
    {
        int langIndex = Services.SettingsService.Get<int>("Language", 0);
        string lang = "zh-Hans";
        if (langIndex == 1) lang = "zh-Hant";
        else if (langIndex == 2) lang = "en-US";
        else if (langIndex == 3) lang = "ja-JP";

        if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return chs switch {
                "弹幕通知" => "Danmaku Notification",
                "弹幕样式" => "Danmaku Style",
                "历史通知" => "History",
                "此项目基于 Windows App SDK (WinUI 3)。" => "Based on Windows App SDK (WinUI 3).",
                "由于 WinUI 3 目前没有原生的透明无边界窗口支持（类似WPF的AllowsTransparency=True），" => "Due to no native transparent borderless window support in WinUI 3,",
                "实现全屏透明的弹幕叠加层需要复杂的 Win32 P/Invoke Hooks。" => "requires complex Win32 P/Invoke Hooks to implement full-screen transparent danmaku layer.",
                "字体大小(%)" => "Font Size (%)",
                "弹幕速度" => "Danmaku Speed",
                "不透明度(%)" => "Opacity (%)",
                "显示区域(%)" => "Display Area (%)",
                "弹幕密度" => "Density",
                "正常" => "Normal",
                "较多" => "More",
                "重叠" => "Overlap",
                "颜色与字体" => "Color and Font",
                "默认字体" => "Default Font",
                "弹幕颜色设定" => "Color Settings",
                "使用粗体" => "Bold",
                "发光/阴影效果" => "Glow/Shadow Effect",
                "发送测试通知弹幕 (调试)" => "Send Test Danmaku (Debug)",
                "系统设置" => "System Settings",
                "界面语言" => "Interface Language",
                "关闭主界面时：" => "When closing main window:",
                "最小化到系统托盘" => "Minimize to tray",
                "退出程序" => "Exit program",
                "关闭时提示" => "Prompt on close",
                "开机时自动启动" => "Start with Windows",
                "显示主界面" => "Show Main Window",
                "完全退出" => "Exit",
                "提示" => "Prompt",
                "确定要最小化到系统托盘吗？" => "Are you sure to minimize to system tray?",
                "确定要退出程序吗？" => "Are you sure to exit the program?",
                "确定" => "OK",
                "取消" => "Cancel",
                _ => chs
            };
        }
        else if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return chs switch {
                "弹幕通知" => "弾幕通知",
                "弹幕样式" => "弾幕スタイル",
                "历史通知" => "履歴",
                "此项目基于 Windows App SDK (WinUI 3)。" => "このプロジェクトは Windows App SDK (WinUI 3) をベースにしています。",
                "由于 WinUI 3 目前没有原生的透明无边界窗口支持（类似WPF的AllowsTransparency=True），" => "WinUI 3には現在、ネイティブの透明な境界線なしウィンドウのサポートがありません。",
                "实现全屏透明的弹幕叠加层需要复杂的 Win32 P/Invoke Hooks。" => "フルスクリーンの透明レイヤーを実装するには複雑なWin32 P/Invoke Hookが必要です。",
                "字体大小(%)" => "フォントサイズ(%)",
                "弹幕速度" => "弾幕の速度",
                "不透明度(%)" => "不透明度(%)",
                "显示区域(%)" => "表示エリア(%)",
                "弹幕密度" => "密度",
                "正常" => "標準",
                "较多" => "多い",
                "重叠" => "重なり許可",
                "颜色与字体" => "色とフォント",
                "默认字体" => "デフォルト",
                "弹幕颜色设定" => "弾幕の色設定",
                "使用粗体" => "太字",
                "发光/阴影效果" => "発光/影効果",
                "发送测试通知弹幕 (调试)" => "テスト弾幕を送信",
                "系统设置" => "システム設定",
                "界面语言" => "言語",
                "关闭主界面时：" => "メイン画面を閉じる時：",
                "最小化到系统托盘" => "システムトレイに最小化",
                "退出程序" => "終了",
                "关闭时提示" => "閉じる時に確認",
                "开机时自动启动" => "自動起動",
                "显示主界面" => "メイン画面を表示",
                "完全退出" => "完全に終了",
                "提示" => "確認",
                "确定要最小化到系统托盘吗？" => "システムトレイに最小化しますか？",
                "确定要退出程序吗？" => "プログラムを終了しますか？",
                "确定" => "OK",
                "取消" => "キャンセル",
                _ => chs
            };
        }
        else if (lang.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || lang.Equals("zh-TW", StringComparison.OrdinalIgnoreCase))
        {
            return chs switch {
                "弹幕通知" => "彈幕通知",
                "弹幕样式" => "彈幕樣式",
                "历史通知" => "歷史通知",
                "此项目基于 Windows App SDK (WinUI 3)。" => "此專案基於 Windows App SDK (WinUI 3)。",
                "由于 WinUI 3 目前没有原生的透明无边界窗口支持（类似WPF的AllowsTransparency=True），" => "由於 WinUI 3 目前沒有原生的透明無邊界視窗支援，",
                "实现全屏透明的弹幕叠加层需要复杂的 Win32 P/Invoke Hooks。" => "實現全螢幕透明的彈幕疊加層需要複雜的 Win32 P/Invoke Hooks。",
                "字体大小(%)" => "字體大小(%)",
                "弹幕速度" => "彈幕速度",
                "不透明度(%)" => "不透明度(%)",
                "显示区域(%)" => "顯示區域(%)",
                "弹幕密度" => "彈幕密度",
                "正常" => "正常",
                "较多" => "較多",
                "重叠" => "重疊",
                "颜色与字体" => "顏色與字體",
                "默认字体" => "預設字體",
                "弹幕颜色设定" => "彈幕顏色設定",
                "使用粗体" => "使用粗體",
                "发光/阴影效果" => "發光/陰影效果",
                "发送测试通知弹幕 (调试)" => "發送測試通知彈幕 (除錯)",
                "系统设置" => "系統設置",
                "界面语言" => "介面語言",
                "关闭主界面时：" => "關閉主介面時：",
                "最小化到系统托盘" => "最小化到系統匣",
                "退出程序" => "退出程式",
                "关闭时提示" => "關閉時提示",
                "开机时自动启动" => "開機時自動啟動",
                "显示主界面" => "顯示主介面",
                "完全退出" => "完全退出",
                "提示" => "提示",
                "确定要最小化到系统托盘吗？" => "確定要最小化到系統匣嗎？",
                "确定要退出程序吗？" => "確定要退出程式嗎？",
                "确定" => "確定",
                "取消" => "取消",
                _ => chs
            };
        }
        return chs;
    }
}

public sealed partial class MainWindow : Window
{
    private NotificationService _notificationService;
    private System.Windows.Forms.NotifyIcon _notifyIcon;

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
            StylePane.Visibility = Visibility.Collapsed;
            SystemSettingsPane.Visibility = Visibility.Visible;
            HistoryPane.Visibility = Visibility.Collapsed;
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            StylePane.Visibility = item.Tag?.ToString() == "Style" ? Visibility.Visible : Visibility.Collapsed;
            SystemSettingsPane.Visibility = Visibility.Collapsed;
            HistoryPane.Visibility = item.Tag?.ToString() == "History" ? Visibility.Visible : Visibility.Collapsed;
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
        
        _notifyIcon.Text = MuiHelper.Instance.AppTitle;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (s, e) =>
        {
            this.AppWindow.Show();
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = new System.Windows.Forms.ToolStripMenuItem(MuiHelper.Instance.TrayAppShow);
        showItem.Click += (s, e) => this.AppWindow.Show();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem(MuiHelper.Instance.TrayAppExit);
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
                Title = MuiHelper.Instance.DialogTitle,
                Content = CloseActionMinimize.IsChecked == true ? MuiHelper.Instance.DialogConfirmMin : MuiHelper.Instance.DialogConfirmExit,
                PrimaryButtonText = MuiHelper.Instance.DialogConfirm,
                CloseButtonText = MuiHelper.Instance.DialogCancel,
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
        Services.SettingsService.Set("FontSize", FontSizeSlider.Value);
        Services.SettingsService.Set("Speed", SpeedSlider.Value);
        Services.SettingsService.Set("Opacity", OpacitySlider.Value);
        Services.SettingsService.Set("DisplayArea", DisplayAreaSlider.Value);
        Services.SettingsService.Set("Density", DensityNormal.IsChecked == true ? 0 : (DensityMore.IsChecked == true ? 1 : 2));
        Services.SettingsService.Set("FontFamily", FontFamilyCombo.SelectedIndex);
        
        var color = DanmakuColor.Color;
        Services.SettingsService.Set("DanmakuColor", $"{color.A},{color.R},{color.G},{color.B}");
        
        Services.SettingsService.Set("Bold", BoldToggle.IsOn);
        Services.SettingsService.Set("Shadow", ShadowToggle.IsOn);
        
        int langIndex = LanguageCombo.SelectedIndex;
        Services.SettingsService.Set("Language", langIndex);
        try
        {
            if (langIndex == 0) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh-Hans";
            else if (langIndex == 1) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh-Hant";
            else if (langIndex == 2) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
            else if (langIndex == 3) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "ja-JP";
        }
        catch { }
        
        MuiHelper.Instance.Refresh(); // Refresh localized strings
        
        Services.SettingsService.Set("CloseAction", CloseActionMinimize.IsChecked == true ? 0 : 1);
        Services.SettingsService.Set("ClosePrompt", CloseActionPrompt.IsChecked == true);
        
        Services.SettingsService.Set("AutoStart", AutoStartToggle.IsOn);
        
        UpdateTrayMenuStrings();
    }
    
    private void UpdateTrayMenuStrings()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Text = MuiHelper.Instance.AppTitle;
            if (_notifyIcon.ContextMenuStrip != null && _notifyIcon.ContextMenuStrip.Items.Count >= 2)
            {
                _notifyIcon.ContextMenuStrip.Items[0].Text = MuiHelper.Instance.TrayAppShow;
                _notifyIcon.ContextMenuStrip.Items[1].Text = MuiHelper.Instance.TrayAppExit;
            }
        }
    }

    private void LoadSettings()
    {
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
            
            FontFamilyCombo.SelectedIndex = Services.SettingsService.Get<int>("FontFamily", 0);
            
            var cStr = Services.SettingsService.Get<string>("DanmakuColor");
            if (!string.IsNullOrEmpty(cStr))
            {
                var parts = cStr.Split(',');
                if (parts.Length == 4 && byte.TryParse(parts[0], out byte a) && byte.TryParse(parts[1], out byte r) && byte.TryParse(parts[2], out byte g) && byte.TryParse(parts[3], out byte b))
                {
                    DanmakuColor.Color = Windows.UI.Color.FromArgb(a, r, g, b);
                }
            }
            
            BoldToggle.IsOn = Services.SettingsService.Get<bool>("Bold", true);
            ShadowToggle.IsOn = Services.SettingsService.Get<bool>("Shadow", true);
            LanguageCombo.SelectedIndex = Services.SettingsService.Get<int>("Language", 0);
            
            int ca = Services.SettingsService.Get<int>("CloseAction", 0);
            if (ca == 0) CloseActionMinimize.IsChecked = true;
            else CloseActionExit.IsChecked = true;
            
            CloseActionPrompt.IsChecked = Services.SettingsService.Get<bool>("ClosePrompt", false);
            AutoStartToggle.IsOn = Services.SettingsService.Get<bool>("AutoStart", false);
        }
        catch { }
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
        BoldToggle.Toggled += (s,e) => SaveSettings();
        ShadowToggle.Toggled += (s,e) => SaveSettings();
        LanguageCombo.SelectionChanged += (s,e) => SaveSettings();
        CloseActionMinimize.Checked += (s,e) => SaveSettings();
        CloseActionExit.Checked += (s,e) => SaveSettings();
        CloseActionPrompt.Checked += (s,e) => SaveSettings();
        CloseActionPrompt.Unchecked += (s,e) => SaveSettings();
        AutoStartToggle.Toggled += (s,e) => 
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
                // Fix: In unpackaged single-file app, GetCurrentProcess().MainModule.FileName sometimes points to temp extraction folder.
                // We should use AppContext.BaseDirectory + ExecutableName, or Environment.ProcessPath.
                string exePath = System.Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    // Adding a space and "autostart" argument so we can know it started automatically
                    key?.SetValue("KanoDanmaku", $"\"{exePath}\" --autostart");
                }
            }
            else
            {
                key?.DeleteValue("KanoDanmaku", false);
            }
        }
        catch { }
    }

    private void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        _notificationService_OnNewDanmaku(new damuku_kano.Models.NotificationItem
        {
            AppName = "Kano弹幕通知",
            Title = "测试通知",
            Message = "测试弹幕 - " + DateTime.Now.ToString("HH:mm:ss"),
            Time = DateTime.Now.ToString("HH:mm")
        });
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private class TrackInfo
    {
        public System.Windows.Window Window { get; set; }
        public double RightEdge
        {
            get
            {
                if (Window == null) return -1;
                var sp = Window.Content as System.Windows.FrameworkElement;
                return Window.Left + (sp?.DesiredSize.Width ?? 0); 
            }
        }
    }
    private TrackInfo[] _tracks = new TrackInfo[200];

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
            SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
            IsHitTestVisible = false // 鼠标穿透
        };

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
            if (_tracks[i] == null || _tracks[i].RightEdge < screenWidth - minGap)
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

        danmakuWindow.Left = spawnX;
        danmakuWindow.Top = y;

        System.Windows.Media.FontFamily fontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei");
        if (FontFamilyCombo != null && FontFamilyCombo.SelectedItem is ComboBoxItem cbi)
        {
            string fd = cbi.Content.ToString();
            if (fd != "默认字体") fontFamily = new System.Windows.Media.FontFamily(fd);
        }

        var col = DanmakuColor.Color;
        var myBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(col.A, col.R, col.G, col.B));

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

        // 构建弹幕外观：只需文字带边缘黑色阴影或发光即可（无边框纯净的抗锯齿效果）
        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = msg,
            FontSize = fontSize,
            FontFamily = fontFamily,
            FontWeight = BoldToggle?.IsOn != false ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
            Foreground = myBrush,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        var effect = ShadowToggle?.IsOn != false ? new System.Windows.Media.Effects.DropShadowEffect // 加上阴影使得弹幕在任何颜色背景下即使没有底色也能看清楚
        {
            Color = System.Windows.Media.Colors.Black,
            BlurRadius = 4,
            ShadowDepth = 2,
            Opacity = 1
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

        danmakuWindow.Content = stackPanel;

        double currentX = spawnX;
        double speed = SpeedSlider != null ? SpeedSlider.Value : 6.0;

        // To calculate element width, measure it before it is fully rendered (or just use a big enough assumed stop coordinate)
        stackPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetX = -stackPanel.DesiredSize.Width - 50;

        _tracks[selectedTrack] = new TrackInfo { Window = danmakuWindow };

        danmakuWindow.Show();

        // 再次强制鼠标穿透（WS_EX_TRANSPARENT）来配合 WPF 的透明底
        var hwnd = new System.Windows.Interop.WindowInteropHelper(danmakuWindow).Handle;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);

        // 针对具体的弹幕窗口，进一步确保 CompositionTarget 采用硬件渲染
        var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        if (hwndSource != null && hwndSource.CompositionTarget != null)
        {
            hwndSource.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.Default;
        }

        // Animate the window based on actual monitor frame deltas using CompositionTarget
        long lastTime = 0;
        EventHandler renderingHandler = null;
        renderingHandler = (s, e) =>
        {
            var args = (System.Windows.Media.RenderingEventArgs)e;
            if (lastTime == 0)
            {
                lastTime = args.RenderingTime.Ticks;
                return;
            }

            // ticks diff / 16.66ms representing elapsed frame count ratio (assuming speed was based on 60fps)
            double dtFrames = (args.RenderingTime.Ticks - lastTime) / 166666.666;
            lastTime = args.RenderingTime.Ticks;

            currentX -= speed * dtFrames;

            if (currentX <= targetX)
            {
                System.Windows.Media.CompositionTarget.Rendering -= renderingHandler;
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
