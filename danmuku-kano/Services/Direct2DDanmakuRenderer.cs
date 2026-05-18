using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using Vortice;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using Forms = System.Windows.Forms;

namespace damuku_kano.Services;

public sealed class Direct2DDanmakuRenderer : IDisposable
{
    private readonly ID2D1Factory _d2dFactory;
    private readonly IDWriteFactory _dwFactory;
    private readonly IWICImagingFactory _wicFactory;
    private readonly Dictionary<string, ScreenOverlay> _overlays = new();
    private readonly object _lock = new();
    private Thread? _renderThread;
    private volatile bool _running;
    private WndProcDelegate? _wndProcDelegate;
    private bool _classRegistered;

    public Direct2DDanmakuRenderer()
    {
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>();
        _dwFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _wicFactory = new IWICImagingFactory();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "D2D-Danmaku" };
        _renderThread.SetApartmentState(ApartmentState.STA);
        _renderThread.Start();
    }

    /// <summary>
    /// Show a danmaku on a specific screen. Pass null to show on all screens.
    /// </summary>
    public void ShowDanmaku(string text, byte[]? iconPng, DanmakuStyleSettings settings, Forms.Screen? targetScreen = null)
    {
        lock (_lock)
        {
            if (targetScreen != null)
            {
                var overlay = GetOrCreateOverlay(targetScreen);
                if (overlay != null)
                {
                    var item = CreateItem(text, iconPng, settings, overlay);
                    _overlays[targetScreen.DeviceName].Items.Add(item);
                }
            }
            else
            {
                foreach (var kvp in _overlays)
                {
                    var item = CreateItem(text, iconPng, settings, kvp.Value);
                    kvp.Value.Items.Add(item);
                }
            }
        }
    }

    private DanmakuItem CreateItem(string text, byte[]? iconPng, DanmakuStyleSettings settings, ScreenOverlay overlay)
    {
        var item = new DanmakuItem
        {
            Text = text,
            IconPng = iconPng,
            Settings = settings,
            StartTime = Stopwatch.GetTimestamp()
        };
        AssignTrack(item, overlay);
        return item;
    }

    public void Dispose()
    {
        _running = false;
        _renderThread?.Join(2000);

        lock (_lock)
        {
            foreach (var overlay in _overlays.Values)
            {
                foreach (var item in overlay.Items)
                {
                    item.TextLayout?.Dispose();
                    item.IconBitmap?.Dispose();
                }
                overlay.Dispose();
            }
            _overlays.Clear();
        }

        _wicFactory.Dispose();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
    }

    private void RenderLoop()
    {
        CreateOverlays(Forms.Screen.AllScreens);

        while (_running)
        {
            while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            lock (_lock)
            {
                foreach (var overlay in _overlays.Values)
                {
                    RenderOverlay(overlay);
                }
            }

            DwmFlush();
        }
    }

    private void CreateOverlays(Forms.Screen[] screens)
    {
        _wndProcDelegate = WndProc;
        var hInstance = GetModuleHandleW(null);
        string className = "DanmakuD2DOverlay";

        if (!_classRegistered)
        {
            var wc = new WNDCLASSW
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = hInstance,
                lpszClassName = className
            };
            RegisterClassW(ref wc);
            _classRegistered = true;
        }

        uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST
                     | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        foreach (var screen in screens)
        {
            var workArea = screen.WorkingArea;
            int w = workArea.Width;
            int h = workArea.Height;

            var hwnd = CreateWindowExW(
                exStyle, className, "DanmakuOverlay", WS_POPUP,
                workArea.Left, workArea.Top, w, h,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            ShowWindow(hwnd, 8);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);

            var screenDC = GetDC(IntPtr.Zero);
            var memDC = CreateCompatibleDC(screenDC);

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;
            var hBitmap = CreateDIBSection(screenDC, ref bmi, 0, out _, IntPtr.Zero, 0);
            var oldBitmap = SelectObject(memDC, hBitmap);
            ReleaseDC(IntPtr.Zero, screenDC);

            var props = new RenderTargetProperties
            {
                Type = RenderTargetType.Default,
                PixelFormat = new Vortice.DCommon.PixelFormat(
                    Vortice.DXGI.Format.B8G8R8A8_UNorm,
                    Vortice.DCommon.AlphaMode.Premultiplied),
                DpiX = 96, DpiY = 96
            };

            var renderTarget = _d2dFactory.CreateDCRenderTarget(props);
            renderTarget.BindDC(memDC, new RawRect(0, 0, w, h));

            _overlays[screen.DeviceName] = new ScreenOverlay
            {
                Screen = screen,
                Hwnd = hwnd,
                MemDC = memDC,
                HBitmap = hBitmap,
                OldBitmap = oldBitmap,
                RenderTarget = renderTarget,
                Width = w,
                Height = h
            };
        }
    }

    private ScreenOverlay? GetOrCreateOverlay(Forms.Screen screen)
    {
        if (_overlays.TryGetValue(screen.DeviceName, out var existing))
            return existing;

        // Screen not yet known (e.g. hot-plugged monitor) — create on the fly
        CreateOverlays(new[] { screen });
        return _overlays.GetValueOrDefault(screen.DeviceName);
    }

    private void RenderOverlay(ScreenOverlay overlay)
    {
        var rt = overlay.RenderTarget;
        if (rt == null) return;

        rt.BeginDraw();
        rt.Clear(new Color4(0, 0, 0, 0));

        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        for (int i = overlay.Items.Count - 1; i >= 0; i--)
        {
            var item = overlay.Items[i];
            double elapsed = (now - item.StartTime) / freq;
            double x = item.SpawnX - elapsed * item.Settings.PixelsPerSecond;

            if (x < -item.TotalWidth - 50)
            {
                item.TextLayout?.Dispose();
                item.IconBitmap?.Dispose();
                overlay.Items.RemoveAt(i);
                continue;
            }

            EnsureResources(item, rt);
            DrawDanmaku(item, (float)x, rt);
        }

        rt.EndDraw();

        var ptSrc = new POINT(0, 0);
        var size = new SIZE(overlay.Width, overlay.Height);
        var ptDst = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(overlay.Hwnd, IntPtr.Zero, ref ptDst, ref size, overlay.MemDC, ref ptSrc, 0, ref blend, 2);
    }

    private void EnsureResources(DanmakuItem item, ID2D1DCRenderTarget rt)
    {
        if (item.TextLayout == null)
        {
            var s = item.Settings;
            using var format = _dwFactory.CreateTextFormat(
                s.FontFamilyName, null,
                s.Bold ? DWriteFontWeight.Bold : DWriteFontWeight.Normal,
                DWriteFontStyle.Normal, DWriteFontStretch.Normal,
                (float)s.FontSize);

            item.TextLayout = _dwFactory.CreateTextLayout(
                item.Text, format, float.MaxValue, (float)s.FontSize * 2);

            var metrics = item.TextLayout.Metrics;
            item.TextWidth = metrics.Width;
            item.TextHeight = metrics.Height;
            item.TotalWidth = item.TextWidth
                + (item.IconPng != null ? (float)s.FontSize + 8 : 0) + 20;
        }

        if (item.IconBitmap == null && item.IconPng != null)
        {
            try { item.IconBitmap = LoadBitmapFromPng(item.IconPng, rt); }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load icon: {ex.Message}");
                item.IconPng = null;
            }
        }
    }

    private void DrawDanmaku(DanmakuItem item, float x, ID2D1DCRenderTarget rt)
    {
        if (item.TextLayout == null) return;

        var s = item.Settings;
        float y = item.TrackY;
        float opacity = (float)(s.OpacityPercent / 100.0);
        float iconOffset = 0;

        if (item.IconBitmap != null)
        {
            float iconSize = (float)s.FontSize;
            var destRect = new Vortice.Mathematics.Rect(x + 10, y, iconSize, iconSize);
            rt.DrawBitmap(item.IconBitmap, opacity,
                Vortice.Direct2D1.BitmapInterpolationMode.Linear, destRect);
            iconOffset = iconSize + 8;
        }

        float textX = x + 10 + iconOffset;
        float textY = y;

        if (s.Shadow)
        {
            float sd = (float)s.ShadowDepth;
            float so = (float)s.ShadowOpacity * opacity;
            using var shadowBrush = rt.CreateSolidColorBrush(
                new Color4(s.ShadowColor.R / 255f, s.ShadowColor.G / 255f,
                           s.ShadowColor.B / 255f, so));
            rt.DrawTextLayout(
                new Vector2(textX + sd, textY + sd), item.TextLayout, shadowBrush);
        }

        if (s.BorderThickness > 0)
        {
            float bt = (float)s.BorderThickness;
            using var strokeBrush = rt.CreateSolidColorBrush(
                new Color4(s.BorderColor.R / 255f, s.BorderColor.G / 255f,
                           s.BorderColor.B / 255f, opacity));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                rt.DrawTextLayout(
                    new Vector2(textX + dx * bt, textY + dy * bt),
                    item.TextLayout, strokeBrush);
            }
        }

        using var fillBrush = rt.CreateSolidColorBrush(
            new Color4(s.Color.R / 255f, s.Color.G / 255f,
                       s.Color.B / 255f, opacity));
        rt.DrawTextLayout(
            new Vector2(textX, textY), item.TextLayout, fillBrush);
    }

    private void AssignTrack(DanmakuItem item, ScreenOverlay overlay)
    {
        var s = item.Settings;
        double trackHeight = s.FontSize + 24;
        int totalTracks = Math.Max(1, (int)(overlay.Height / trackHeight));
        int activeCount = Math.Clamp(
            (int)(totalTracks * (s.DisplayAreaPercent / 100.0)), 1, totalTracks);

        double minGap = s.Density switch { 1 => 20, 2 => -300, _ => 100 };
        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        var available = new List<int>();
        for (int i = 0; i < activeCount; i++)
        {
            bool occupied = false;
            foreach (var existing in overlay.Items)
            {
                if (existing.TrackIndex != i) continue;
                double elapsed = (now - existing.StartTime) / freq;
                double rightEdge = existing.SpawnX
                    - elapsed * existing.Settings.PixelsPerSecond + existing.TotalWidth;
                if (rightEdge > overlay.Width - minGap) { occupied = true; break; }
            }
            if (!occupied) available.Add(i);
        }

        int track = available.Count > 0
            ? available[Random.Shared.Next(available.Count)]
            : Random.Shared.Next(0, activeCount);

        item.TrackIndex = track;
        item.TrackY = (float)(track * trackHeight);
        item.SpawnX = overlay.Width;
    }

    private ID2D1Bitmap? LoadBitmapFromPng(byte[] pngData, ID2D1DCRenderTarget rt)
    {
        using var stream = _wicFactory.CreateStream();
        stream.Initialize(pngData);
        using var decoder = _wicFactory.CreateDecoderFromStream(
            stream, DecodeOptions.CacheOnLoad);
        using var frame = decoder.GetFrame(0);
        using var converter = _wicFactory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA);
        return rt.CreateBitmapFromWicBitmap(converter);
    }

    private sealed class ScreenOverlay : IDisposable
    {
        public Forms.Screen Screen { get; init; } = null!;
        public IntPtr Hwnd;
        public IntPtr MemDC;
        public IntPtr HBitmap;
        public IntPtr OldBitmap;
        public ID2D1DCRenderTarget? RenderTarget;
        public int Width;
        public int Height;
        public List<DanmakuItem> Items { get; } = new();

        public void Dispose()
        {
            RenderTarget?.Dispose();

            if (MemDC != IntPtr.Zero)
            {
                SelectObject(MemDC, OldBitmap);
                DeleteObject(HBitmap);
                DeleteDC(MemDC);
                MemDC = IntPtr.Zero;
            }

            if (Hwnd != IntPtr.Zero)
            {
                DestroyWindow(Hwnd);
                Hwnd = IntPtr.Zero;
            }
        }
    }

    private sealed class DanmakuItem
    {
        public string Text { get; init; } = "";
        public byte[]? IconPng { get; set; }
        public DanmakuStyleSettings Settings { get; init; } = null!;
        public double StartTime { get; init; }
        public double SpawnX { get; set; }
        public int TrackIndex { get; set; }
        public float TrackY { get; set; }
        public float TextWidth { get; set; }
        public float TextHeight { get; set; }
        public float TotalWidth { get; set; }
        public IDWriteTextLayout? TextLayout { get; set; }
        public ID2D1Bitmap? IconBitmap { get; set; }
    }

    #region P/Invoke

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam, lParam;
        public uint time; public int pt_x, pt_y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASSW wc);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
        uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProcW(hWnd, msg, wParam, lParam);

    #endregion
}
