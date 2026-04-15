using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using damuku_kano.Models;
using damuku_kano.Services;

namespace damuku_kano;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private Window? _dummyWindow;
    private bool _isExiting;

    public NotificationService NotificationService { get; }
    public TrayIconService TrayIconService { get; }
    public DanmakuOverlayService OverlayService { get; }
    public LocalizationService Strings => LocalizationService.Instance;

    public App()
    {
        Strings.ApplySavedLanguage();
        InitializeComponent();

        TrayIconService = new TrayIconService();
        OverlayService = new DanmakuOverlayService();
        NotificationService = new NotificationService();

        TrayIconService.ShowRequested += (_, _) => ShowMainWindow();
        TrayIconService.ExitRequested += (_, _) => ExitApplication();
        NotificationService.OnNewDanmaku += HandleNewDanmaku;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dummyWindow = new Window(); // Keep process alive
        var presenter = (OverlappedPresenter)_dummyWindow.AppWindow.Presenter;
        presenter.IsAlwaysOnTop = false;
        presenter.SetBorderAndTitleBar(false, false);
        _dummyWindow.AppWindow.IsShownInSwitchers = false;

        TrayIconService.UpdateStrings();
        ShowMainWindow();
        await NotificationService.InitializeAsync();
    }

    public void ShowMainWindow()
    {
        if (_isExiting)
        {
            return;
        }

        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(this);
            _mainWindow.Closed += MainWindow_Closed;
            AppIconHelper.ApplyWindowIcon(_mainWindow);
        }

        _mainWindow.Activate();
    }

    public async Task HandleMainWindowClosingAsync(MainWindow window, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        int closeAction = SettingsService.Get<int>("CloseAction", 0);
        bool showPrompt = SettingsService.Get<bool>("ClosePrompt", false);

        if (showPrompt)
        {
            args.Cancel = true;

            var dialog = new ContentDialog
            {
                Title = Strings.DialogTitle,
                Content = closeAction == 0 ? Strings.DialogConfirmMin : Strings.DialogConfirmExit,
                PrimaryButtonText = Strings.DialogConfirm,
                CloseButtonText = Strings.DialogCancel,
                XamlRoot = window.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }
        }

        if (closeAction == 0)
        {
            args.Cancel = false;
            return;
        }

        args.Cancel = true;
        ExitApplication();
    }

    public void ApplyLanguage(int languageIndex)
    {
        Strings.ApplyLanguage(languageIndex);
        TrayIconService.UpdateStrings();
    }

    public void ShowTestDanmaku()
    {
        HandleNewDanmaku(new NotificationItem
        {
            AppName = Strings.TestNotificationAppName,
            Title = Strings.TestNotificationTitle,
            Message = $"{Strings.TestNotificationMessagePrefix} - {DateTime.Now:HH:mm:ss}",
            Time = DateTime.Now.ToString("HH:mm")
        });
    }

    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        TrayIconService.Dispose();
        NotificationService.Dispose();
        OverlayService.Dispose();

        Exit();
    }

    private void HandleNewDanmaku(NotificationItem item)
    {
        _ = OverlayService.ShowNotificationAsync(item, DanmakuStyleSettings.Load());
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ReferenceEquals(sender, _mainWindow))
        {
            _mainWindow.Closed -= MainWindow_Closed;
            _mainWindow = null;
        }
    }
}
