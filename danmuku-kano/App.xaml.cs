using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using damuku_kano.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace damuku_kano
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            UnhandledException += (s, e) =>
            {
                CrashLog.Write("XAML UnhandledException", e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                CrashLog.Write("AppDomain UnhandledException: " + (e.ExceptionObject?.ToString() ?? "unknown"));
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                CrashLog.Write("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            LocalizationService.Instance.ApplySavedLanguage();

            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();

            var isAutoStart = Environment.GetCommandLineArgs()
                .Any(arg => string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase));
            if (isAutoStart)
            {
                _window.AppWindow.Hide();
                return;
            }

            _window.Activate();
        }
    }
}
