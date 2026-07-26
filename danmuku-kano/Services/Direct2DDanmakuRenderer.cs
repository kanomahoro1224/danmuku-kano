using System;
using System.Collections.Concurrent;
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
    // Overlays (and their windows/DCs) are owned by the render thread;
    // other threads only enqueue requests.
    private readonly Dictionary<string, ScreenOverlay> _overlays = new();
    private readonly ConcurrentQueue<DanmakuRequest> _requests = new();
    // Extra overlay spanning all screens for DisplayScreenMode 3; created on demand.
    // Real device names look like "\\.\DISPLAY1", so this key cannot collide.
    private const string SpanKey = "\\SPAN";
    private bool _spanActive;
    private volatile bool _displayChanged;
    private long _pendingResync;
    private Thread? _renderThread;
    private volatile bool _running;
    private volatile int _maxFps;
    private readonly AutoResetEvent _wake = new(false);
    private WndProcDelegate? _wndProcDelegate;
    private bool _classRegistered;
    private bool _disposed;

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
    /// 0 = follow the display refresh rate (DwmFlush), >0 = cap the render loop at that FPS.
    /// </summary>
    public void SetMaxFps(int maxFps) => _maxFps = maxFps;

    private static readonly char[] NewlineChars = { '\r', '\n' };

    /// <summary>
    /// Queue a danmaku; the target screen(s) follow settings.DisplayScreenMode
    /// (0 = primary, 1 = every screen gets its own copy, 2 = screen under the
    /// mouse cursor, 3 = one continuous flow across all screens).
    /// </summary>
    public void ShowDanmaku(string text, byte[]? iconPng, DanmakuStyleSettings settings)
    {
        // Toast bodies are often multi-line; a multi-line layout would spill into
        // the tracks below, so danmaku are always a single line.
        string singleLine = string.Join(' ', text.Split(NewlineChars,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        _requests.Enqueue(new DanmakuRequest
        {
            Text = singleLine,
            IconPng = iconPng,
            Settings = settings
        });

        // Wake the render thread immediately instead of waiting out the idle sleep
        _wake.Set();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _running = false;
        _wake.Set();
        // Overlay windows belong to the render thread and are destroyed there on exit.
        _renderThread?.Join(2000);

        _wake.Dispose();
        _wicFactory.Dispose();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
    }

    private void RenderLoop()
    {
        try
        {
            SyncOverlays();
            CrashLog.Write("Renderer started");
            PrewarmText();

            TimeBeginPeriod(1);
            long lastFrame = Stopwatch.GetTimestamp();
            long lastTopmostAssert = 0;

            while (_running)
            {
                while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }

                if (_displayChanged)
                {
                    _displayChanged = false;
                    SyncOverlays();
                    // Displays settle in waves (driver re-detection, shell work-area
                    // updates) and WinForms' Screen cache may refresh after we ran —
                    // sync once more a second later to catch the final layout.
                    _pendingResync = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                }
                else if (_pendingResync != 0 && Stopwatch.GetTimestamp() >= _pendingResync)
                {
                    _pendingResync = 0;
                    SyncOverlays();
                }

                DispatchRequests();

                long now = Stopwatch.GetTimestamp();
                bool assertTopmost = now - lastTopmostAssert > Stopwatch.Frequency;
                if (assertTopmost) lastTopmostAssert = now;

                bool anyVisible = false;
                foreach (var overlay in _overlays.Values)
                {
                    SpawnPending(overlay);

                    if (overlay.Items.Count > 0)
                    {
                        if (!overlay.Visible)
                        {
                            ShowWindow(overlay.Hwnd, 8);
                            SetWindowPos(overlay.Hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                            overlay.Visible = true;
                        }
                        else if (assertTopmost)
                        {
                            // Topmost windows created after ours end up above us in the
                            // topmost band, so re-assert while danmaku are on screen.
                            SetWindowPos(overlay.Hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                        }

                        RenderOverlay(overlay);
                        anyVisible = true;
                    }
                    else if (overlay.Visible)
                    {
                        // Hide when idle: a visible full-screen layered window forces DWM
                        // composition and blocks games from using independent flip.
                        ShowWindow(overlay.Hwnd, 0);
                        overlay.Visible = false;
                    }
                }

                if (!anyVisible)
                {
                    _wake.WaitOne(50);
                    lastFrame = Stopwatch.GetTimestamp();
                    continue;
                }

                int maxFps = _maxFps;
                if (maxFps <= 0)
                {
                    DwmFlush();
                }
                else
                {
                    long targetTicks = Stopwatch.Frequency / maxFps;
                    long elapsed = Stopwatch.GetTimestamp() - lastFrame;
                    int sleepMs = (int)((targetTicks - elapsed) * 1000 / Stopwatch.Frequency);
                    if (sleepMs > 0) Thread.Sleep(sleepMs);
                }
                lastFrame = Stopwatch.GetTimestamp();
            }

            TimeEndPeriod(1);
        }
        catch (Exception ex)
        {
            // Never let the render thread take the process down with it
            CrashLog.Write("Danmaku render thread terminated", ex);
        }
        finally
        {
            // Overlay windows belong to this thread; DestroyWindow fails cross-thread.
            foreach (var overlay in _overlays.Values)
            {
                DisposeItems(overlay);
                overlay.Dispose();
            }
            _overlays.Clear();
        }
    }

    private void CreateOverlays(Forms.Screen[] screens)
    {
        foreach (var screen in screens)
        {
            if (_overlays.ContainsKey(screen.DeviceName)) continue;

            var workArea = screen.WorkingArea;
            CreateOverlay(screen.DeviceName, workArea.Left, workArea.Top, workArea.Width, workArea.Height);
        }
    }

    private void CreateOverlay(string key, int left, int top, int w, int h)
    {
        _wndProcDelegate ??= WndProc;
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

        var hwnd = CreateWindowExW(
            exStyle, className, "DanmakuOverlay", WS_POPUP,
            left, top, w, h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

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

        _overlays[key] = new ScreenOverlay
        {
            Hwnd = hwnd,
            MemDC = memDC,
            HBitmap = hBitmap,
            OldBitmap = oldBitmap,
            RenderTarget = renderTarget,
            Brush = renderTarget.CreateSolidColorBrush(new Color4(1, 1, 1, 1)),
            OriginX = left,
            OriginY = top,
            Width = w,
            Height = h
        };
    }

    /// <summary>
    /// Rect for the cross-screen overlay: horizontal union of all work areas,
    /// vertical intersection — danmaku only travel through the band every screen
    /// shows, so they are never clipped by a screen edge mid-flight.
    /// Returns null when the screens don't overlap vertically.
    /// </summary>
    private static (int Left, int Top, int Width, int Height)? ComputeSpanRect(Forms.Screen[] screens)
    {
        if (screens.Length == 0) return null;

        int left = int.MaxValue, right = int.MinValue;
        int top = int.MinValue, bottom = int.MaxValue;
        foreach (var screen in screens)
        {
            var workArea = screen.WorkingArea;
            left = Math.Min(left, workArea.Left);
            right = Math.Max(right, workArea.Right);
            top = Math.Max(top, workArea.Top);
            bottom = Math.Min(bottom, workArea.Bottom);
        }

        if (bottom - top < 40) return null; // not even one small track fits
        return (left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Bring the overlay set in line with the current monitor layout: drop overlays
    /// for unplugged screens, recreate ones whose geometry changed, add new screens.
    /// Runs on the render thread only (overlay windows belong to it).
    /// </summary>
    private void SyncOverlays()
    {
        Forms.Screen[] screens;
        try
        {
            screens = Forms.Screen.AllScreens;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Screen enumeration failed", ex);
            return;
        }

        var spanRect = _spanActive ? ComputeSpanRect(screens) : null;
        Dictionary<string, Queue<DanmakuItem>>? carriedPending = null;

        foreach (var key in new List<string>(_overlays.Keys))
        {
            var overlay = _overlays[key];

            (int Left, int Top, int Width, int Height)? desired;
            if (key == SpanKey)
            {
                desired = spanRect;
            }
            else
            {
                var screen = Array.Find(screens, x => x.DeviceName == key);
                desired = screen == null
                    ? null
                    : (screen.WorkingArea.Left, screen.WorkingArea.Top,
                       screen.WorkingArea.Width, screen.WorkingArea.Height);
            }

            if (desired is { } rect &&
                rect.Left == overlay.OriginX && rect.Top == overlay.OriginY &&
                rect.Width == overlay.Width && rect.Height == overlay.Height)
            {
                continue;
            }

            // Geometry changed: recreate the overlay. On-screen danmaku are tied to
            // the old coordinates and get dropped, queued ones are carried over.
            if (desired != null && overlay.Pending.Count > 0)
            {
                carriedPending ??= new Dictionary<string, Queue<DanmakuItem>>();
                carriedPending[key] = new Queue<DanmakuItem>(overlay.Pending);
                overlay.Pending.Clear();
            }

            DisposeItems(overlay);
            overlay.Dispose();
            _overlays.Remove(key);
        }

        try
        {
            CreateOverlays(screens);
            if (spanRect is { } span && !_overlays.ContainsKey(SpanKey))
            {
                CreateOverlay(SpanKey, span.Left, span.Top, span.Width, span.Height);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Overlay creation failed", ex);
        }

        if (carriedPending != null)
        {
            foreach (var kvp in carriedPending)
            {
                if (_overlays.TryGetValue(kvp.Key, out var overlay))
                {
                    while (kvp.Value.Count > 0) overlay.Pending.Enqueue(kvp.Value.Dequeue());
                }
            }
        }
    }

    private void DispatchRequests()
    {
        while (_requests.TryDequeue(out var request))
        {
            switch (request.Settings.DisplayScreenMode)
            {
                case 1: // every screen shows its own copy
                    EnqueueMirrored(request);
                    break;

                case 2: // screen under the mouse cursor
                {
                    string device = Forms.Screen.FromPoint(Forms.Cursor.Position).DeviceName;
                    if (!_overlays.TryGetValue(device, out var target))
                    {
                        // Screen not yet known (e.g. hot-plugged monitor) — pick it up now
                        SyncOverlays();
                        if (!_overlays.TryGetValue(device, out target))
                        {
                            target = FindPrimaryOverlay();
                        }
                    }
                    target?.Pending.Enqueue(NewItem(request));
                    break;
                }

                case 3: // one continuous flow across all screens
                {
                    _spanActive = true;
                    if (!_overlays.TryGetValue(SpanKey, out var span))
                    {
                        SyncOverlays();
                        _overlays.TryGetValue(SpanKey, out span);
                    }

                    if (span != null)
                    {
                        span.Pending.Enqueue(NewItem(request));
                    }
                    else
                    {
                        // Screens share no vertical band (e.g. stacked) — a crossing
                        // danmaku would get clipped, so fall back to per-screen copies.
                        EnqueueMirrored(request);
                    }
                    break;
                }

                default: // primary screen
                    FindPrimaryOverlay()?.Pending.Enqueue(NewItem(request));
                    break;
            }
        }
    }

    private void EnqueueMirrored(DanmakuRequest request)
    {
        foreach (var kvp in _overlays)
        {
            if (kvp.Key != SpanKey)
            {
                kvp.Value.Pending.Enqueue(NewItem(request));
            }
        }
    }

    private ScreenOverlay? FindPrimaryOverlay()
    {
        var primary = Forms.Screen.PrimaryScreen;
        if (primary != null && _overlays.TryGetValue(primary.DeviceName, out var overlay))
        {
            return overlay;
        }

        foreach (var kvp in _overlays)
        {
            if (kvp.Key != SpanKey) return kvp.Value;
        }
        return null;
    }

    private static DanmakuItem NewItem(DanmakuRequest request) => new()
    {
        Text = request.Text,
        IconPng = request.IconPng,
        Settings = request.Settings
    };

    /// <summary>
    /// Move queued danmaku onto free tracks in arrival order. An item stays queued
    /// until some track can take it without overlapping (unless density allows overlap).
    /// </summary>
    private void SpawnPending(ScreenOverlay overlay)
    {
        while (overlay.Pending.Count > 0)
        {
            var item = overlay.Pending.Peek();
            EnsureTextLayout(item); // need the real width before picking a track
            if (!TryAssignTrack(item, overlay))
            {
                break; // keep FIFO order; retry next frame
            }

            item.StartTime = Stopwatch.GetTimestamp();
            overlay.Pending.Dequeue();
            overlay.Items.Add(item);
        }
    }

    private static void DisposeItems(ScreenOverlay overlay)
    {
        foreach (var item in overlay.Items)
        {
            item.TextLayout?.Dispose();
            item.IconBitmap?.Dispose();
        }
        overlay.Items.Clear();

        while (overlay.Pending.Count > 0)
        {
            var pending = overlay.Pending.Dequeue();
            pending.TextLayout?.Dispose();
            pending.IconBitmap?.Dispose();
        }
    }

    /// <summary>
    /// First DirectWrite layout on a cold font cache can take seconds; pay that
    /// cost at startup instead of on the first danmaku.
    /// </summary>
    private void PrewarmText()
    {
        try
        {
            using var format = _dwFactory.CreateTextFormat(
                "Microsoft YaHei", null!, DWriteFontWeight.Bold,
                DWriteFontStyle.Normal, DWriteFontStretch.Normal, 36f);
            using var layout = _dwFactory.CreateTextLayout("弹幕预热 Warmup", format, float.MaxValue, 72f);
            _ = layout.Metrics;

            foreach (var overlay in _overlays.Values)
            {
                var rt = overlay.RenderTarget;
                if (rt == null || overlay.Brush == null) continue;
                rt.BeginDraw();
                rt.Clear(new Color4(0, 0, 0, 0));
                rt.DrawTextLayout(new Vector2(0, 0), layout, overlay.Brush);
                rt.Clear(new Color4(0, 0, 0, 0));
                rt.EndDraw();
            }
        }
        catch { }
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

            EnsureIcon(item, rt); // text layout was created before the item spawned
            if (x < overlay.Width)
            {
                DrawDanmaku(item, (float)x, overlay);
            }
        }

        rt.EndDraw();

        var ptSrc = new POINT(0, 0);
        var size = new SIZE(overlay.Width, overlay.Height);
        var ptDst = new POINT(overlay.OriginX, overlay.OriginY);
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(overlay.Hwnd, IntPtr.Zero, ref ptDst, ref size, overlay.MemDC, ref ptSrc, 0, ref blend, 2);
    }

    private void EnsureTextLayout(DanmakuItem item)
    {
        if (item.TextLayout != null) return;

        var s = item.Settings;
        using var format = _dwFactory.CreateTextFormat(
            s.FontFamilyName, null!,
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

    private void EnsureIcon(DanmakuItem item, ID2D1DCRenderTarget rt)
    {
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

    private void DrawDanmaku(DanmakuItem item, float x, ScreenOverlay overlay)
    {
        if (item.TextLayout == null) return;

        var rt = overlay.RenderTarget;
        var brush = overlay.Brush;
        if (rt == null || brush == null) return;

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
            brush.Color = new Color4(s.ShadowColor.R / 255f, s.ShadowColor.G / 255f,
                                     s.ShadowColor.B / 255f, so);
            rt.DrawTextLayout(
                new Vector2(textX + sd, textY + sd), item.TextLayout, brush);
        }

        if (s.BorderThickness > 0)
        {
            float bt = (float)s.BorderThickness;
            brush.Color = new Color4(s.BorderColor.R / 255f, s.BorderColor.G / 255f,
                                     s.BorderColor.B / 255f, opacity);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                rt.DrawTextLayout(
                    new Vector2(textX + dx * bt, textY + dy * bt),
                    item.TextLayout, brush);
            }
        }

        brush.Color = new Color4(s.Color.R / 255f, s.Color.G / 255f,
                                 s.Color.B / 255f, opacity);
        rt.DrawTextLayout(
            new Vector2(textX, textY), item.TextLayout, brush);
    }

    /// <summary>
    /// Try to place the item on a free track. Returns false when every track is
    /// occupied and the density setting does not allow overlap — the caller keeps
    /// the item queued instead of drawing it on top of another danmaku.
    /// </summary>
    private static bool TryAssignTrack(DanmakuItem item, ScreenOverlay overlay)
    {
        var s = item.Settings;
        // Line boxes run ~1.3–1.4× the font size; below ~48px the +24 padding covers
        // that, above it the multiplier keeps neighbouring tracks from overlapping.
        double trackHeight = Math.Max(s.FontSize + 24, s.FontSize * 1.5);
        int totalTracks = Math.Max(1, (int)(overlay.Height / trackHeight));
        int activeCount = Math.Clamp(
            (int)(totalTracks * (s.DisplayAreaPercent / 100.0)), 1, totalTracks);

        bool allowOverlap = s.Density == 2;
        double minGap = s.Density switch { 1 => 20, 2 => -300, _ => 100 };
        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;
        double speed = s.PixelsPerSecond;

        var available = new List<int>();
        for (int i = 0; i < activeCount; i++)
        {
            bool occupied = false;
            foreach (var existing in overlay.Items)
            {
                if (existing.TrackIndex != i) continue;
                double elapsed = (now - existing.StartTime) / freq;
                double existingSpeed = existing.Settings.PixelsPerSecond;
                double rightEdge = existing.SpawnX
                    - elapsed * existingSpeed + existing.TotalWidth;
                if (rightEdge > overlay.Width - minGap) { occupied = true; break; }

                // A faster newcomer must not catch the slower one before its tail exits
                if (!allowOverlap && speed > existingSpeed && rightEdge > 0 &&
                    overlay.Width - rightEdge < (speed - existingSpeed) * (rightEdge / existingSpeed))
                {
                    occupied = true;
                    break;
                }
            }
            if (!occupied) available.Add(i);
        }

        int track;
        if (available.Count > 0)
        {
            track = available[Random.Shared.Next(available.Count)];
        }
        else if (allowOverlap)
        {
            track = Random.Shared.Next(0, activeCount);
        }
        else
        {
            return false;
        }

        item.TrackIndex = track;
        item.TrackY = (float)(track * trackHeight);
        item.SpawnX = overlay.Width;
        return true;
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
        public IntPtr Hwnd;
        public IntPtr MemDC;
        public IntPtr HBitmap;
        public IntPtr OldBitmap;
        public ID2D1DCRenderTarget? RenderTarget;
        public ID2D1SolidColorBrush? Brush;
        public int Width;
        public int Height;
        public int OriginX;
        public int OriginY;
        public bool Visible;
        public List<DanmakuItem> Items { get; } = new();
        public Queue<DanmakuItem> Pending { get; } = new();

        public void Dispose()
        {
            Brush?.Dispose();
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

    private sealed class DanmakuRequest
    {
        public string Text { get; init; } = "";
        public byte[]? IconPng { get; init; }
        public DanmakuStyleSettings Settings { get; init; } = null!;
    }

    private sealed class DanmakuItem
    {
        public string Text { get; init; } = "";
        public byte[]? IconPng { get; set; }
        public DanmakuStyleSettings Settings { get; init; } = null!;
        public double StartTime { get; set; } // set when the item leaves the queue
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

    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_DISPLAYCHANGE = 0x007E;
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

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uPeriod);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Resolution / monitor / work-area changes: resync overlays on the next frame
        if (msg == WM_DISPLAYCHANGE || msg == WM_SETTINGCHANGE)
        {
            _displayChanged = true;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    #endregion
}
