using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using damuku_kano.Models;
using WinRT;
using Forms = System.Windows.Forms;

namespace damuku_kano.Services;

public sealed class DanmakuOverlayService : IDisposable
{
    private readonly Dictionary<string, OverlayHost> _hosts = new();
    private bool _isDisposed;

    public async Task ShowNotificationAsync(NotificationItem item, DanmakuStyleSettings settings)
    {
        if (_isDisposed)
        {
            return;
        }

        var targetScreen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var host = GetOrCreateHost(targetScreen);
        await host.ShowDanmakuAsync(item, settings);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (var host in _hosts.Values)
        {
            host.Dispose();
        }

        _hosts.Clear();
        _isDisposed = true;
    }

    private OverlayHost GetOrCreateHost(Forms.Screen screen)
    {
        if (_hosts.TryGetValue(screen.DeviceName, out var host))
        {
            host.UpdateScreen(screen);
            return host;
        }

        host = new OverlayHost(screen);
        _hosts[screen.DeviceName] = host;
        return host;
    }

    private sealed class OverlayHost : IDisposable
    {
        private readonly Window _window;
        private readonly Canvas _canvas;
        private OverlayTrackState?[] _tracks = Array.Empty<OverlayTrackState?>();
        private Forms.Screen _screen;
        private bool _stylesApplied;

        public OverlayHost(Forms.Screen screen)
        {
            _screen = screen;
            _canvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };

            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                Content = _canvas,
                IsHitTestVisible = false
            };

            RenderOptions.ProcessRenderMode = RenderMode.Default; // Ensure it's not set to software
            RenderOptions.SetEdgeMode(_window, EdgeMode.Aliased);

            _window.SourceInitialized += (_, _) => ApplyWindowStyles();
            UpdateScreen(screen);
        }

        public void Dispose()
        {
            foreach (UIElement child in _canvas.Children)
            {
                child.RenderTransform?.BeginAnimation(TranslateTransform.XProperty, null);
            }

            _canvas.Children.Clear();
            _window.Close();
        }

        public void UpdateScreen(Forms.Screen screen)
        {
            _screen = screen;

            var workArea = screen.WorkingArea;
            _window.Left = workArea.Left;
            _window.Top = workArea.Top;
            _window.Width = workArea.Width;
            _window.Height = workArea.Height;

            _canvas.Width = workArea.Width;
            _canvas.Height = workArea.Height;
        }

        public async Task ShowDanmakuAsync(NotificationItem item, DanmakuStyleSettings settings)
        {
            UpdateScreen(_screen);

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            var workArea = _screen.WorkingArea;
            double fontSize = settings.FontSize;
            double trackHeight = fontSize + 24;
            int totalTracks = Math.Max(1, (int)(workArea.Height / trackHeight));
            int activeTrackCount = Math.Clamp((int)(totalTracks * (settings.DisplayAreaPercent / 100.0)), 1, totalTracks);

            EnsureTrackCapacity(totalTracks);

            double minGap = settings.Density switch
            {
                1 => 20,
                2 => -300,
                _ => 100
            };

            var availableTracks = new List<int>();
            for (int i = 0; i < activeTrackCount; i++)
            {
                var track = _tracks[i];
                if (track == null || track.CurrentRightEdge <= workArea.Width - minGap)
                {
                    availableTracks.Add(i);
                }
            }

            int selectedTrack;
            double spawnX = workArea.Width;

            if (availableTracks.Count == 0)
            {
                selectedTrack = Random.Shared.Next(0, activeTrackCount);
                var occupiedTrack = _tracks[selectedTrack];
                if (settings.Density == 0 && occupiedTrack != null && occupiedTrack.CurrentRightEdge > spawnX - minGap)
                {
                    spawnX = occupiedTrack.CurrentRightEdge + minGap;
                }
            }
            else
            {
                selectedTrack = availableTracks[Random.Shared.Next(0, availableTracks.Count)];
            }

            var element = await BuildDanmakuElementAsync(item, settings);
            element.CacheMode = new BitmapCache { EnableClearType = true, SnapsToDevicePixels = true, RenderAtScale = 1.0 };
            element.RenderTransform = new TranslateTransform(spawnX, 0);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double targetX = -element.DesiredSize.Width - 50;
            double top = selectedTrack * trackHeight;
            double pixelsPerSecond = settings.PixelsPerSecond;
            double distance = spawnX - targetX;
            double durationSeconds = Math.Max(0.1, distance / pixelsPerSecond);

            Canvas.SetLeft(element, 0);
            Canvas.SetTop(element, top);
            _canvas.Children.Add(element);

            _tracks[selectedTrack] = new OverlayTrackState(element, spawnX, element.DesiredSize.Width, pixelsPerSecond);

            var animation = new DoubleAnimation
            {
                From = spawnX,
                To = targetX,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                FillBehavior = FillBehavior.Stop
            };

            // Hardware acceleration hints for WPF animations
            Timeline.SetDesiredFrameRate(animation, 60);

            animation.Completed += (_, _) =>
            {
                element.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _canvas.Children.Remove(element);

                if (_tracks[selectedTrack]?.Element == element)
                {
                    _tracks[selectedTrack] = null;
                }

                if (_canvas.Children.Count == 0)
                {
                    _window.Hide();
                }
            };

            element.RenderTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void EnsureTrackCapacity(int totalTracks)
        {
            if (_tracks.Length == totalTracks)
            {
                return;
            }

            Array.Resize(ref _tracks, totalTracks);
        }

        private void ApplyWindowStyles()
        {
            if (_stylesApplied)
            {
                return;
            }

            const int GwlExStyle = -20;
            const int WsExTransparent = 0x00000020;
            const int WsExToolWindow = 0x00000080;

            var hwnd = new WindowInteropHelper(_window).Handle;
            int exStyle = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, exStyle | WsExTransparent | WsExToolWindow);

            _stylesApplied = true;
        }

        private static async Task<FrameworkElement> BuildDanmakuElementAsync(NotificationItem item, DanmakuStyleSettings settings)
        {
            var container = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(10),
                Opacity = settings.OpacityPercent / 100.0,
                IsHitTestVisible = false
            };

            if (settings.Shadow)
            {
                container.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 4,
                    ShadowDepth = 2,
                    Opacity = 1
                };
            }

            var icon = await LoadLogoImageAsync(item);
            if (icon != null)
            {
                container.Children.Add(new Image
                {
                    Source = icon,
                    Width = settings.FontSize,
                    Height = settings.FontSize,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                });
            }

            container.Children.Add(new TextBlock
            {
                Text = $"{item.AppName}: {item.Title} {item.Message}",
                FontSize = settings.FontSize,
                FontFamily = new FontFamily(settings.FontFamilyName),
                FontWeight = settings.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(settings.Color),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.None,
                IsHitTestVisible = false
            });

            return container;
        }

        private static async Task<ImageSource?> LoadLogoImageAsync(NotificationItem item)
        {
            if (item.AppLogoStream == null)
            {
                return null;
            }

            try
            {
                using var stream = await item.AppLogoStream.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                using (var netStream = stream.AsStreamForRead())
                {
                    await netStream.CopyToAsync(memoryStream);
                }

                memoryStream.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    private sealed class OverlayTrackState
    {
        private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

        public OverlayTrackState(FrameworkElement element, double startLeft, double width, double pixelsPerSecond)
        {
            Element = element;
            StartLeft = startLeft;
            Width = width;
            PixelsPerSecond = pixelsPerSecond;
        }

        public FrameworkElement Element { get; }

        public double StartLeft { get; }

        public double Width { get; }

        public double PixelsPerSecond { get; }

        public double CurrentRightEdge
        {
            get
            {
                double elapsedSeconds = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
                double left = StartLeft - (elapsedSeconds * PixelsPerSecond);
                return left + Width;
            }
        }
    }
}
