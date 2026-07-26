using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using Vortice;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using Forms = System.Windows.Forms;

namespace damuku_kano.Services;

public sealed class Direct2DDanmakuRenderer : IDisposable
{
    private const int MaxPending = 50;           // per-screen queue cap; oldest dropped beyond this
    private const double MaxQueueWaitSeconds = 5; // after this, spawn on the least-bad track
    private const int MinStripHeight = 60;
    private const int InitialStripHeight = 240;

    private readonly ID2D1Factory1 _d2dFactory;
    private readonly IDWriteFactory _dwFactory;
    private readonly IWICImagingFactory _wicFactory;
    private readonly Dictionary<string, ScreenOverlay> _overlays = new();
    private readonly object _lock = new();
    private Thread? _renderThread;
    private volatile bool _running;
    private volatile int _maxFps;
    private readonly AutoResetEvent _wake = new(false);
    private WndProcDelegate? _wndProcDelegate;
    private bool _classRegistered;

    // DirectComposition backend (shared across screens); when unavailable we fall
    // back to the legacy UpdateLayeredWindow path per overlay.
    private bool _useDComp;
    private ID3D11Device? _d3dDevice;
    private IDXGIDevice? _dxgiDevice;
    private IDXGIFactory2? _dxgiFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private IDCompositionDevice? _dcompDevice;

    public Direct2DDanmakuRenderer()
    {
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
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
                if (overlay != null) Enqueue(overlay, text, iconPng, settings);
            }
            else
            {
                foreach (var kvp in _overlays)
                {
                    Enqueue(kvp.Value, text, iconPng, settings);
                }
            }
        }

        // Wake the render thread immediately instead of waiting out the idle sleep
        _wake.Set();
    }

    private static void Enqueue(ScreenOverlay overlay, string text, byte[]? iconPng, DanmakuStyleSettings settings)
    {
        overlay.Pending.Enqueue(new DanmakuItem
        {
            Text = text,
            IconPng = iconPng,
            Settings = settings,
            EnqueueTime = Stopwatch.GetTimestamp()
        });

        while (overlay.Pending.Count > MaxPending)
        {
            overlay.Pending.Dequeue();
        }
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        _renderThread?.Join(2000);

        lock (_lock)
        {
            foreach (var overlay in _overlays.Values)
            {
                DisposeItems(overlay);
                DisposePresentation(overlay);
            }
            _overlays.Clear();
            DisposeDCompBackend();
        }

        _wake.Dispose();
        _wicFactory.Dispose();
        _dwFactory.Dispose();
        _d2dFactory.Dispose();
    }

    private static void DisposeItems(ScreenOverlay overlay)
    {
        foreach (var item in overlay.Items)
        {
            item.Sprite?.Dispose();
            item.Sprite = null;
        }
        overlay.Items.Clear();
        overlay.Pending.Clear();
    }

    private void RenderLoop()
    {
        lock (_lock)
        {
            _useDComp = InitDCompBackend();
            CreateOverlays(Forms.Screen.AllScreens);
        }
        PrewarmText();

        TimeBeginPeriod(1);
        long lastFrame = Stopwatch.GetTimestamp();
        long lastTopmostAssert = 0;
        int failures = 0;

        while (_running)
        {
            try
            {
                while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }

                long now = Stopwatch.GetTimestamp();
                bool assertTopmost = now - lastTopmostAssert > Stopwatch.Frequency;
                if (assertTopmost) lastTopmostAssert = now;

                bool anyVisible = false;
                lock (_lock)
                {
                    foreach (var overlay in _overlays.Values)
                    {
                        ProcessPending(overlay);

                        if (overlay.Items.Count > 0)
                        {
                            int required = ComputeRequiredHeight(overlay);
                            if (!overlay.Visible)
                            {
                                EnsureSurfaceHeight(overlay, required);
                                ShowWindow(overlay.Hwnd, 8);
                                SetWindowPos(overlay.Hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                                overlay.Visible = true;
                            }
                            else
                            {
                                if (required > overlay.SurfaceHeight)
                                {
                                    EnsureSurfaceHeight(overlay, required);
                                }
                                if (assertTopmost)
                                {
                                    // Topmost windows created after ours end up above us in the
                                    // topmost band, so re-assert while danmaku are on screen.
                                    SetWindowPos(overlay.Hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                                }
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
                failures = 0;
            }
            catch (Exception ex)
            {
                // Device lost / surface trouble: rebuild everything, falling back to the
                // legacy path if it keeps failing.
                Debug.WriteLine($"Danmaku render failure: {ex.Message}");
                failures++;
                try
                {
                    lock (_lock)
                    {
                        RebuildAllPresentation(forceLegacy: failures >= 2);
                    }
                }
                catch { }
                Thread.Sleep(200);
            }
        }

        TimeEndPeriod(1);
    }

    #region Track assignment / queue

    private void ProcessPending(ScreenOverlay overlay)
    {
        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        while (overlay.Pending.Count > 0)
        {
            var item = overlay.Pending.Peek();
            var s = item.Settings;

            double trackHeight = s.FontSize + 24;
            int totalTracks = Math.Max(1, (int)(overlay.FullHeight / trackHeight));
            int activeCount = Math.Clamp(
                (int)(totalTracks * (s.DisplayAreaPercent / 100.0)), 1, totalTracks);

            int track;
            if (s.Density == 2)
            {
                // "Overlap" density intentionally allows collisions
                track = Random.Shared.Next(0, activeCount);
            }
            else
            {
                track = FindFreeTrack(overlay, s, activeCount, trackHeight, now, freq, out int fallback);
                if (track < 0)
                {
                    double waited = (now - item.EnqueueTime) / freq;
                    if (waited < MaxQueueWaitSeconds) break; // wait for a track to free up
                    track = fallback;                        // waited too long: least-bad track
                }
            }

            overlay.Pending.Dequeue();
            item.TrackIndex = track;
            item.TrackHeight = (float)trackHeight;
            item.TrackY = (float)(track * trackHeight);
            item.SpawnX = overlay.Width;
            item.StartTime = now;
            overlay.Items.Add(item);
        }
    }

    private static int FindFreeTrack(ScreenOverlay overlay, DanmakuStyleSettings s,
        int activeCount, double trackHeight, double now, double freq, out int fallback)
    {
        double minGap = s.Density == 1 ? 20 : 100;
        double v2 = s.PixelsPerSecond;

        List<int>? free = null;
        fallback = 0;
        double bestTail = double.MaxValue;

        for (int i = 0; i < activeCount; i++)
        {
            float bandTop = (float)(i * trackHeight);
            float bandBottom = (float)(bandTop + trackHeight);
            bool occupied = false;
            double maxTail = double.MinValue;

            foreach (var existing in overlay.Items)
            {
                // Compare vertical bands, not indices: items spawned with a different
                // font size live on a different track grid.
                if (existing.TrackY >= bandBottom || existing.TrackY + existing.TrackHeight <= bandTop)
                {
                    continue;
                }

                double v1 = existing.Settings.PixelsPerSecond;
                double elapsed = (now - existing.StartTime) / freq;
                double tail = existing.SpawnX - elapsed * v1 + existing.TotalWidth;
                if (tail > maxTail) maxTail = tail;

                if (tail > overlay.Width - minGap) { occupied = true; break; }

                // Faster newcomers must not catch the slower item before it exits
                if (v2 > v1 && (overlay.Width - tail) / (v2 - v1) < tail / v1)
                {
                    occupied = true;
                    break;
                }
            }

            if (!occupied)
            {
                (free ??= new List<int>()).Add(i);
            }

            double tailScore = maxTail == double.MinValue ? 0 : maxTail;
            if (tailScore < bestTail)
            {
                bestTail = tailScore;
                fallback = i;
            }
        }

        return free != null ? free[Random.Shared.Next(free.Count)] : -1;
    }

    private static int ComputeRequiredHeight(ScreenOverlay overlay)
    {
        float required = MinStripHeight;
        foreach (var item in overlay.Items)
        {
            float bottom = item.TrackY + item.TrackHeight;
            if (bottom > required) required = bottom;
        }
        return (int)MathF.Ceiling(required);
    }

    #endregion

    #region Backend / overlay lifecycle

    private bool InitDCompBackend()
    {
        if (SettingsService.Get<bool>("ForceLegacyRenderer", false)) return false;

        try
        {
            var result = D3D11.D3D11CreateDevice(null, DriverType.Hardware,
                DeviceCreationFlags.BgraSupport, null, out ID3D11Device? device);
            if (result.Failure || device == null) return false;

            _d3dDevice = device;
            _dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using (var adapter = _dxgiDevice.GetAdapter())
            {
                _dxgiFactory = adapter.GetParent<IDXGIFactory2>();
            }
            _d2dDevice = _d2dFactory.CreateDevice(_dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
            _dcompDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dxgiDevice);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DirectComposition unavailable, using layered window path: {ex.Message}");
            DisposeDCompBackend();
            return false;
        }
    }

    private void DisposeDCompBackend()
    {
        _d2dContext?.Dispose(); _d2dContext = null;
        _d2dDevice?.Dispose(); _d2dDevice = null;
        _dcompDevice?.Dispose(); _dcompDevice = null;
        _dxgiFactory?.Dispose(); _dxgiFactory = null;
        _dxgiDevice?.Dispose(); _dxgiDevice = null;
        _d3dDevice?.Dispose(); _d3dDevice = null;
    }

    private void CreateOverlays(Forms.Screen[] screens)
    {
        foreach (var screen in screens)
        {
            if (_overlays.ContainsKey(screen.DeviceName)) continue;

            var workArea = screen.WorkingArea;
            var overlay = new ScreenOverlay
            {
                Screen = screen,
                Width = workArea.Width,
                FullHeight = workArea.Height
            };
            CreatePresentation(overlay);
            _overlays[screen.DeviceName] = overlay;
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

    private void CreatePresentation(ScreenOverlay overlay)
    {
        _wndProcDelegate ??= WndProc;
        var hInstance = GetModuleHandleW(null);
        const string className = "DanmakuD2DOverlay";

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
        if (_useDComp) exStyle |= WS_EX_NOREDIRECTIONBITMAP;

        var workArea = overlay.Screen.WorkingArea;
        int h = Math.Min(overlay.FullHeight, InitialStripHeight);
        overlay.OriginX = workArea.Left;
        overlay.OriginY = workArea.Top;

        overlay.Hwnd = CreateWindowExW(
            exStyle, className, "DanmakuOverlay", WS_POPUP,
            workArea.Left, workArea.Top, overlay.Width, h,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        overlay.Visible = false;
        overlay.SurfaceHeight = h;

        if (_useDComp)
        {
            // Layered + fully opaque alpha keeps the window click-through while
            // DirectComposition supplies the actual pixels.
            SetLayeredWindowAttributes(overlay.Hwnd, 0, 255, LWA_ALPHA);

            var desc = new SwapChainDescription1
            {
                Width = (uint)overlay.Width,
                Height = (uint)h,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Premultiplied,
                Flags = SwapChainFlags.None
            };
            overlay.SwapChain = _dxgiFactory!.CreateSwapChainForComposition(_d3dDevice!, desc, null);
            _dcompDevice!.CreateTargetForHwnd(overlay.Hwnd, true, out IDCompositionTarget dcompTarget).CheckError();
            overlay.DcompTarget = dcompTarget;
            _dcompDevice.CreateVisual(out IDCompositionVisual dcompVisual).CheckError();
            overlay.DcompVisual = dcompVisual;
            overlay.DcompVisual.SetContent(overlay.SwapChain);
            overlay.DcompTarget.SetRoot(overlay.DcompVisual);
            _dcompDevice.Commit();
        }
        else
        {
            CreateLegacySurface(overlay, h);
        }
    }

    private void CreateLegacySurface(ScreenOverlay overlay, int h)
    {
        var screenDC = GetDC(IntPtr.Zero);
        if (overlay.MemDC == IntPtr.Zero)
        {
            overlay.MemDC = CreateCompatibleDC(screenDC);
        }

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = overlay.Width;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;
        var hBitmap = CreateDIBSection(screenDC, ref bmi, 0, out _, IntPtr.Zero, 0);
        var previous = SelectObject(overlay.MemDC, hBitmap);
        if (overlay.OldBitmap == IntPtr.Zero)
        {
            overlay.OldBitmap = previous;
        }
        else
        {
            DeleteObject(previous);
        }
        overlay.HBitmap = hBitmap;
        ReleaseDC(IntPtr.Zero, screenDC);

        if (overlay.RenderTarget == null)
        {
            var props = new RenderTargetProperties
            {
                Type = RenderTargetType.Default,
                PixelFormat = new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied),
                DpiX = 96,
                DpiY = 96
            };
            overlay.RenderTarget = _d2dFactory.CreateDCRenderTarget(props);
        }
        overlay.RenderTarget.BindDC(overlay.MemDC, new RawRect(0, 0, overlay.Width, h));
        overlay.SurfaceHeight = h;
    }

    private void EnsureSurfaceHeight(ScreenOverlay overlay, int required)
    {
        required = Math.Clamp(required, MinStripHeight, overlay.FullHeight);
        if (required == overlay.SurfaceHeight) return;

        if (_useDComp)
        {
            SetWindowPos(overlay.Hwnd, IntPtr.Zero, 0, 0, overlay.Width, required,
                0x0002 | 0x0004 | 0x0010); // SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE
            overlay.SwapChain!.ResizeBuffers(2, (uint)overlay.Width, (uint)required,
                Format.B8G8R8A8_UNorm, SwapChainFlags.None);
            overlay.SurfaceHeight = required;
        }
        else
        {
            // UpdateLayeredWindow resizes the window itself on the next push
            CreateLegacySurface(overlay, required);
        }
    }

    private void DisposePresentation(ScreenOverlay overlay)
    {
        overlay.DcompVisual?.Dispose(); overlay.DcompVisual = null;
        overlay.DcompTarget?.Dispose(); overlay.DcompTarget = null;
        overlay.SwapChain?.Dispose(); overlay.SwapChain = null;

        overlay.RenderTarget?.Dispose(); overlay.RenderTarget = null;
        if (overlay.MemDC != IntPtr.Zero)
        {
            SelectObject(overlay.MemDC, overlay.OldBitmap);
            DeleteObject(overlay.HBitmap);
            DeleteDC(overlay.MemDC);
            overlay.MemDC = IntPtr.Zero;
            overlay.HBitmap = IntPtr.Zero;
            overlay.OldBitmap = IntPtr.Zero;
        }

        if (overlay.Hwnd != IntPtr.Zero)
        {
            DestroyWindow(overlay.Hwnd);
            overlay.Hwnd = IntPtr.Zero;
        }
        overlay.Visible = false;
    }

    private void RebuildAllPresentation(bool forceLegacy)
    {
        foreach (var overlay in _overlays.Values)
        {
            foreach (var item in overlay.Items)
            {
                item.Sprite?.Dispose();
                item.Sprite = null;
            }
            DisposePresentation(overlay);
        }
        DisposeDCompBackend();

        _useDComp = !forceLegacy && InitDCompBackend();
        foreach (var overlay in _overlays.Values)
        {
            CreatePresentation(overlay);
        }
    }

    #endregion

    #region Rendering

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
        }
        catch { }
    }

    private void RenderOverlay(ScreenOverlay overlay)
    {
        if (_useDComp)
        {
            RenderDComp(overlay);
        }
        else
        {
            RenderLegacy(overlay);
        }
    }

    private void RenderLegacy(ScreenOverlay overlay)
    {
        var rt = overlay.RenderTarget;
        if (rt == null) return;

        RenderItems(rt, overlay);

        var ptSrc = new POINT(0, 0);
        var size = new SIZE(overlay.Width, overlay.SurfaceHeight);
        var ptDst = new POINT(overlay.OriginX, overlay.OriginY);
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(overlay.Hwnd, IntPtr.Zero, ref ptDst, ref size, overlay.MemDC, ref ptSrc, 0, ref blend, 2);
    }

    private void RenderDComp(ScreenOverlay overlay)
    {
        var context = _d2dContext;
        var swapChain = overlay.SwapChain;
        if (context == null || swapChain == null) return;

        using (var surface = swapChain.GetBuffer<IDXGISurface>(0))
        using (var target = context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
                   new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied),
                   96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw)))
        {
            context.Target = target;
            try
            {
                RenderItems(context, overlay);
            }
            finally
            {
                context.Target = null;
            }
        }

        var result = swapChain.Present(0, PresentFlags.None);
        if (result.Failure)
        {
            throw new InvalidOperationException($"Present failed: {result.Code:X}");
        }
    }

    private void RenderItems(ID2D1RenderTarget rt, ScreenOverlay overlay)
    {
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
                item.Sprite?.Dispose();
                item.Sprite = null;
                overlay.Items.RemoveAt(i);
                continue;
            }

            if (item.Sprite == null)
            {
                BakeSprite(item, rt);
            }

            if (item.Sprite != null && x < overlay.Width)
            {
                float opacity = (float)(item.Settings.OpacityPercent / 100.0);
                var dest = new Rect(
                    (float)x + 10 + item.SpriteOffsetX,
                    item.TrackY + item.SpriteOffsetY,
                    item.SpriteWidth, item.SpriteHeight);
                rt.DrawBitmap(item.Sprite, opacity, Vortice.Direct2D1.BitmapInterpolationMode.Linear, dest);
            }
        }

        rt.EndDraw();
    }

    /// <summary>
    /// Rasterize icon + shadow + outline + fill once into a bitmap; per-frame work
    /// becomes a single DrawBitmap instead of up to 10 text passes.
    /// </summary>
    private void BakeSprite(DanmakuItem item, ID2D1RenderTarget rt)
    {
        var s = item.Settings;

        using var format = _dwFactory.CreateTextFormat(
            s.FontFamilyName, null!,
            s.Bold ? DWriteFontWeight.Bold : DWriteFontWeight.Normal,
            DWriteFontStyle.Normal, DWriteFontStretch.Normal,
            (float)s.FontSize);
        using var layout = _dwFactory.CreateTextLayout(
            item.Text, format, float.MaxValue, (float)s.FontSize * 2);

        var metrics = layout.Metrics;
        float textW = metrics.Width;
        float textH = metrics.Height;

        float border = (float)s.BorderThickness;
        float blur = s.Shadow ? (float)s.ShadowBlur : 0;
        float depth = s.Shadow ? (float)s.ShadowDepth : 0;
        float padLeft = border + blur + 2;
        float padTop = border + blur + 2;
        float padRight = border + blur + depth + 2;
        float padBottom = border + blur + depth + 2;

        float iconSize = item.IconPng != null ? (float)s.FontSize : 0;
        float iconAdvance = item.IconPng != null ? iconSize + 8 : 0;

        int w = (int)MathF.Ceiling(padLeft + iconAdvance + textW + padRight);
        int h = (int)MathF.Ceiling(padTop + MathF.Max(textH, iconSize) + padBottom);
        if (w <= 0 || h <= 0) return;

        using var sprite = rt.CreateCompatibleRenderTarget(
            new Vortice.Mathematics.Size(w, h), new SizeI(w, h),
            new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied),
            CompatibleRenderTargetOptions.None);

        sprite.BeginDraw();
        sprite.Clear(new Color4(0, 0, 0, 0));

        float textX = padLeft + iconAdvance;
        float textY = padTop;

        if (item.IconPng != null)
        {
            try
            {
                using var icon = LoadBitmapFromPng(item.IconPng, sprite);
                if (icon != null)
                {
                    sprite.DrawBitmap(icon, 1f, Vortice.Direct2D1.BitmapInterpolationMode.Linear,
                        new Rect(padLeft, padTop, iconSize, iconSize));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load icon: {ex.Message}");
            }
        }

        using var brush = sprite.CreateSolidColorBrush(new Color4(1, 1, 1, 1));

        if (s.Shadow)
        {
            float so = Math.Clamp((float)s.ShadowOpacity, 0f, 1f);
            var shadowPos = new Vector2(textX + depth, textY + depth);

            if (blur > 0)
            {
                // Approximate a Gaussian glow with rings of offset draws — bake-time only
                brush.Color = new Color4(s.ShadowColor.R / 255f, s.ShadowColor.G / 255f,
                                         s.ShadowColor.B / 255f, Math.Clamp(so * 0.22f, 0f, 1f));
                for (int ring = 1; ring <= 2; ring++)
                {
                    float radius = blur * ring / 2f;
                    for (int k = 0; k < 8; k++)
                    {
                        float angle = k * MathF.PI / 4f;
                        sprite.DrawTextLayout(
                            shadowPos + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius),
                            layout, brush);
                    }
                }
                brush.Color = new Color4(s.ShadowColor.R / 255f, s.ShadowColor.G / 255f,
                                         s.ShadowColor.B / 255f, Math.Clamp(so * 0.6f, 0f, 1f));
                sprite.DrawTextLayout(shadowPos, layout, brush);
            }
            else
            {
                brush.Color = new Color4(s.ShadowColor.R / 255f, s.ShadowColor.G / 255f,
                                         s.ShadowColor.B / 255f, so);
                sprite.DrawTextLayout(shadowPos, layout, brush);
            }
        }

        if (border > 0)
        {
            brush.Color = new Color4(s.BorderColor.R / 255f, s.BorderColor.G / 255f,
                                     s.BorderColor.B / 255f, 1f);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                sprite.DrawTextLayout(
                    new Vector2(textX + dx * border, textY + dy * border), layout, brush);
            }
        }

        brush.Color = new Color4(s.Color.R / 255f, s.Color.G / 255f, s.Color.B / 255f, 1f);
        sprite.DrawTextLayout(new Vector2(textX, textY), layout, brush);

        sprite.EndDraw();

        item.Sprite = sprite.Bitmap;
        item.SpriteOffsetX = -padLeft;
        item.SpriteOffsetY = -padTop;
        item.SpriteWidth = w;
        item.SpriteHeight = h;
        item.TotalWidth = iconAdvance + textW + 20;
    }

    private ID2D1Bitmap? LoadBitmapFromPng(byte[] pngData, ID2D1RenderTarget rt)
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

    #endregion

    private sealed class ScreenOverlay
    {
        public Forms.Screen Screen = null!;
        public int Width;
        public int FullHeight;
        public int SurfaceHeight;
        public int OriginX;
        public int OriginY;
        public bool Visible;
        public IntPtr Hwnd;

        // Legacy UpdateLayeredWindow path
        public IntPtr MemDC;
        public IntPtr HBitmap;
        public IntPtr OldBitmap;
        public ID2D1DCRenderTarget? RenderTarget;

        // DirectComposition path
        public IDXGISwapChain1? SwapChain;
        public IDCompositionTarget? DcompTarget;
        public IDCompositionVisual? DcompVisual;

        public List<DanmakuItem> Items { get; } = new();
        public Queue<DanmakuItem> Pending { get; } = new();
    }

    private sealed class DanmakuItem
    {
        public string Text = "";
        public byte[]? IconPng;
        public DanmakuStyleSettings Settings = null!;
        public double EnqueueTime;
        public double StartTime;
        public double SpawnX;
        public int TrackIndex;
        public float TrackY;
        public float TrackHeight;
        public float TotalWidth;
        public ID2D1Bitmap? Sprite;
        public float SpriteOffsetX;
        public float SpriteOffsetY;
        public int SpriteWidth;
        public int SpriteHeight;
    }

    #region P/Invoke

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint LWA_ALPHA = 0x00000002;
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
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

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

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProcW(hWnd, msg, wParam, lParam);

    #endregion
}
