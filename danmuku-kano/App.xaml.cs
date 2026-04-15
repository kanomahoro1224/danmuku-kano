using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
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
            try
            {
                int lang = Services.SettingsService.Get<int>("Language", 0);
                if (lang == 0) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh-Hans";
                else if (lang == 1) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh-Hant";
                else if (lang == 2) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US";
                else if (lang == 3) Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "ja-JP";
            }
            catch { }

            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Contains("--autostart"))
            {
                // Just activate in background if that's supported, else don't bring to front
                // But for WinUI 3 the easiest way without complex native calls is just to launch and then minimize:
            }
            
            if (!cmdArgs.Contains("--autostart"))
            {
                _window.Activate();
            }
        }
    }
}
