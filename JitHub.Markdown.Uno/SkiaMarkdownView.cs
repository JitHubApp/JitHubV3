using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace JitHub.Markdown.Uno;

public class SkiaMarkdownView : ContentControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(SkiaMarkdownView),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme),
        typeof(MarkdownTheme),
        typeof(SkiaMarkdownView),
        new PropertyMetadata(MarkdownTheme.Light, OnThemeChanged));

    public static readonly DependencyProperty ImageBaseUriProperty = DependencyProperty.Register(
        nameof(ImageBaseUri),
        typeof(Uri),
        typeof(SkiaMarkdownView),
        new PropertyMetadata(null, OnThemeChanged));

    public static readonly DependencyProperty SelectionEnabledProperty = DependencyProperty.Register(
        nameof(SelectionEnabled),
        typeof(bool),
        typeof(SkiaMarkdownView),
        new PropertyMetadata(true, OnSelectionEnabledChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownTheme Theme
    {
        get => (MarkdownTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public Uri? ImageBaseUri
    {
        get => (Uri?)GetValue(ImageBaseUriProperty);
        set => SetValue(ImageBaseUriProperty, value);
    }

    public bool SelectionEnabled
    {
        get => (bool)GetValue(SelectionEnabledProperty);
        set => SetValue(SelectionEnabledProperty, value);
    }

    public SelectionRange? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            InvalidateRender();
        }
    }

    public SkiaMarkdownView()
    {
        _engine = MarkdownEngine.CreateDefault();
        _layoutEngine = new MarkdownLayoutEngine();
        _textMeasurer = new SkiaTextMeasurer();
        _renderer = new SkiaMarkdownRenderer();

        _canvas = new Canvas();
        _image = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _canvas.Children.Add(_image);
        Content = _canvas;

        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerCanceled += OnPointerCanceled;
        _canvas.PointerCaptureLost += OnPointerCaptureLost;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        RebuildDocumentAndLayout();
    }

    private readonly MarkdownEngine _engine;
    private readonly MarkdownLayoutEngine _layoutEngine;
    private readonly SkiaTextMeasurer _textMeasurer;
    private readonly SkiaMarkdownRenderer _renderer;
    private readonly Canvas _canvas;
    private readonly Image _image;

    private MarkdownDocumentModel? _document;
    private MarkdownLayout? _layout;
    private SelectionRange? _selection;

    private ScrollViewer? _scrollViewer;
    private double _viewportTop;
    private double _viewportHeight;

    private WriteableBitmap? _bitmap;
    private int _bitmapPixelWidth;
    private int _bitmapPixelHeight;

    private readonly ConcurrentDictionary<Uri, SKImage> _imageCache = new();
    private readonly ConcurrentDictionary<Uri, Task> _imageLoads = new();
    private readonly HttpClient _http = new();

    private readonly SelectionPointerInteraction _pointerInteraction = new();
    private bool _hasPointerCapture;

    private uint? _activePointerId;
    private Microsoft.UI.Xaml.Input.Pointer? _activePointer;
    private bool _isPointerDown;

    private bool _touchLongPressArmed;
    private bool _touchSelectionStarted;
    private float _touchPressX;
    private float _touchPressY;
    private MarkdownHitTestResult _touchPressHit;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _touchLongPressTimer;

    private const float TouchMoveCancelThreshold = 8f;
    private static readonly TimeSpan TouchLongPressDelay = TimeSpan.FromMilliseconds(500);

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SkiaMarkdownView)d).RebuildDocumentAndLayout();
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SkiaMarkdownView)d).RebuildLayoutOnly();
    }

    private static void OnSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (SkiaMarkdownView)d;
        if (e.NewValue is bool enabled && !enabled)
        {
            view._pointerInteraction.ClearSelection();
            view.Selection = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindParentScrollViewer();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnScrollViewerViewChanged;
            UpdateViewportFromScrollViewer();
        }

        InvalidateRender();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hasPointerCapture = false;

        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnScrollViewerViewChanged;
        }

        foreach (var kvp in _imageCache)
        {
            kvp.Value.Dispose();
        }
        _imageCache.Clear();
        _imageLoads.Clear();
    }

    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_layout is null)
        {
            return;
        }

        var p = e.GetCurrentPoint(_canvas);
        var pos = p.Position;

        _isPointerDown = true;
        _activePointerId = e.Pointer.PointerId;
        _activePointer = e.Pointer;

        if (!MarkdownHitTester.TryHitTest(_layout, (float)pos.X, (float)pos.Y, out var hit))
        {
            return;
        }

        var isTouch = IsTouchPointer(e);
        if (isTouch)
        {
            // Mobile MVP: long-press to start selection; allow scroll/pan until long-press triggers.
            // Still arm link activation on tap, but cancel it if the user drags (scrolls).
            _touchPressX = (float)pos.X;
            _touchPressY = (float)pos.Y;
            _touchPressHit = hit;
            _touchSelectionStarted = false;
            _touchLongPressArmed = true;

            var down = _pointerInteraction.OnPointerDown(
                hit,
                x: (float)pos.X,
                y: (float)pos.Y,
                selectionEnabled: false,
                modifiers: new PointerModifiers(Shift: false));

            if (down.SelectionChanged)
            {
                Selection = down.Selection;
            }

            ArmTouchLongPress();
            return;
        }

        var mods = new PointerModifiers(Shift: e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift));

        var result = _pointerInteraction.OnPointerDown(
            hit,
            x: (float)pos.X,
            y: (float)pos.Y,
            selectionEnabled: SelectionEnabled,
            modifiers: mods);

        if (result.SelectionChanged)
        {
            Selection = result.Selection;
        }

        if (!_hasPointerCapture)
        {
            _hasPointerCapture = _canvas.CapturePointer(e.Pointer);
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_layout is null)
        {
            return;
        }

        if (!_isPointerDown)
        {
            return;
        }

        if (_activePointerId.HasValue && e.Pointer.PointerId != _activePointerId.Value)
        {
            return;
        }

        var p = e.GetCurrentPoint(_canvas);
        var pos = p.Position;

        // Touch: before long-press triggers, avoid handling (lets ScrollViewer pan).
        var isTouch = IsTouchPointer(e);
        if (isTouch && _touchLongPressArmed && !_touchSelectionStarted)
        {
            var dx = (float)pos.X - _touchPressX;
            var dy = (float)pos.Y - _touchPressY;
            var dist2 = (dx * dx) + (dy * dy);
            if (dist2 > (TouchMoveCancelThreshold * TouchMoveCancelThreshold))
            {
                CancelTouchLongPress();
            }

            if (MarkdownHitTester.TryHitTest(_layout, (float)pos.X, (float)pos.Y, out var moveHitForCancel))
            {
                _pointerInteraction.OnPointerMove(
                    moveHitForCancel,
                    x: (float)pos.X,
                    y: (float)pos.Y,
                    selectionEnabled: false);
            }

            return;
        }

        if (!MarkdownHitTester.TryHitTest(_layout, (float)pos.X, (float)pos.Y, out var hit))
        {
            return;
        }

        var result = _pointerInteraction.OnPointerMove(
            hit,
            x: (float)pos.X,
            y: (float)pos.Y,
            selectionEnabled: SelectionEnabled);

        if (result.SelectionChanged)
        {
            Selection = result.Selection;
        }

        // Handle moves when we're actively interacting (selection). This is important on WASM
        // where CapturePointer may not be supported.
        if (_hasPointerCapture || _touchSelectionStarted)
        {
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_layout is null)
        {
            return;
        }

        if (_activePointerId.HasValue && e.Pointer.PointerId != _activePointerId.Value)
        {
            return;
        }

        CancelTouchLongPress();

        var p = e.GetCurrentPoint(_canvas);
        var pos = p.Position;

        if (!MarkdownHitTester.TryHitTest(_layout, (float)pos.X, (float)pos.Y, out var hit))
        {
            ReleaseCapture(e);
            ResetPointerTracking();
            return;
        }

        var result = _pointerInteraction.OnPointerUp(hit, selectionEnabled: SelectionEnabled);
        if (result.SelectionChanged)
        {
            Selection = result.Selection;
        }

        if (!string.IsNullOrWhiteSpace(result.ActivateLinkUrl))
        {
            _ = OpenLinkAsync(result.ActivateLinkUrl);
        }

        ReleaseCapture(e);
        _isPointerDown = false;

        // Only handle release when we actually performed an interaction.
        e.Handled = _hasPointerCapture || _touchSelectionStarted || !string.IsNullOrWhiteSpace(result.ActivateLinkUrl);

        ResetPointerTracking();
    }

    private void OnPointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CancelTouchLongPress();
        ReleaseCapture(e);
        ResetPointerTracking();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hasPointerCapture = false;
    }

    private void ReleaseCapture(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_hasPointerCapture)
        {
            _canvas.ReleasePointerCapture(e.Pointer);
            _hasPointerCapture = false;
        }
    }

    private void ResetPointerTracking()
    {
        _activePointerId = null;
        _activePointer = null;
        _isPointerDown = false;
        _touchLongPressArmed = false;
        _touchSelectionStarted = false;
    }

    private void ArmTouchLongPress()
    {
        CancelTouchLongPress();

        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq is null)
        {
            return;
        }

        _touchLongPressTimer = dq.CreateTimer();
        _touchLongPressTimer.Interval = TouchLongPressDelay;
        _touchLongPressTimer.IsRepeating = false;
        _touchLongPressTimer.Tick += OnTouchLongPress;
        _touchLongPressTimer.Start();
    }

    private void CancelTouchLongPress()
    {
        if (_touchLongPressTimer is not null)
        {
            _touchLongPressTimer.Tick -= OnTouchLongPress;
            _touchLongPressTimer.Stop();
            _touchLongPressTimer = null;
        }

        _touchLongPressArmed = false;
    }

    private void OnTouchLongPress(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_isPointerDown || !_touchLongPressArmed || _layout is null)
        {
            return;
        }

        // Start selection at the original touch-down hit.
        var result = _pointerInteraction.OnPointerDown(
            _touchPressHit,
            x: _touchPressX,
            y: _touchPressY,
            selectionEnabled: SelectionEnabled,
            modifiers: new PointerModifiers(Shift: false));

        if (result.SelectionChanged)
        {
            Selection = result.Selection;
        }

        _touchSelectionStarted = true;
        _touchLongPressArmed = false;

        if (!_hasPointerCapture && _activePointer is not null)
        {
            _hasPointerCapture = _canvas.CapturePointer(_activePointer);
        }
    }

    private static async Task OpenLinkAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        try
        {
            _ = await Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            // Ignore launch failures.
        }
    }

    private static bool IsTouchPointer(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Uno/WinUI expose PointerDeviceType in slightly different API shapes across targets.
        // Using ToString() keeps this compile-safe and avoids comparing different enums with the same name.
        try
        {
            return string.Equals(e.Pointer.PointerDeviceType.ToString(), "Touch", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildLayoutOnly();
    }

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateViewportFromScrollViewer();
        InvalidateRender();
    }

    private void UpdateViewportFromScrollViewer()
    {
        if (_scrollViewer is null)
        {
            _viewportTop = 0;
            _viewportHeight = ActualHeight;
            return;
        }

        // Compute the intersection of this control with the ScrollViewer viewport.
        // IMPORTANT: the ScrollViewer's VerticalOffset is for the full content; this control may not start at y=0
        // (e.g. when a TextBox/header exists above it). We therefore calculate the visible region relative to *this* control.
        try
        {
            var transform = TransformToVisual(_scrollViewer);
            var topLeftInViewport = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            var viewportRect = new Windows.Foundation.Rect(0, 0, _scrollViewer.ViewportWidth, _scrollViewer.ViewportHeight);
            var controlRectInViewport = new Windows.Foundation.Rect(topLeftInViewport.X, topLeftInViewport.Y, ActualWidth, ActualHeight);

            var x1 = Math.Max(viewportRect.X, controlRectInViewport.X);
            var y1 = Math.Max(viewportRect.Y, controlRectInViewport.Y);
            var x2 = Math.Min(viewportRect.X + viewportRect.Width, controlRectInViewport.X + controlRectInViewport.Width);
            var y2 = Math.Min(viewportRect.Y + viewportRect.Height, controlRectInViewport.Y + controlRectInViewport.Height);

            if (x2 <= x1 || y2 <= y1)
            {
                _viewportTop = 0;
                _viewportHeight = 0;
                _image.Visibility = Visibility.Collapsed;
                return;
            }

            var visible = new Windows.Foundation.Rect(x1, y1, x2 - x1, y2 - y1);

            _image.Visibility = Visibility.Visible;

            // Convert visible rect from ScrollViewer viewport space into control-local coordinates.
            _viewportTop = Math.Max(0, visible.Y - controlRectInViewport.Y);
            _viewportHeight = Math.Max(0, visible.Height);

            Canvas.SetTop(_image, _viewportTop);
        }
        catch
        {
            // If transforms fail for any reason, fall back to rendering the whole control.
            _viewportTop = 0;
            _viewportHeight = ActualHeight;
            _image.Visibility = Visibility.Visible;
            Canvas.SetTop(_image, 0);
        }
    }

    private void RebuildDocumentAndLayout()
    {
        _document = _engine.Parse(Markdown ?? string.Empty);
        RebuildLayoutOnly();
    }

    private void RebuildLayoutOnly()
    {
        if (_document is null)
        {
            return;
        }

        var width = (float)Math.Max(1, ActualWidth);
        var scale = GetScale();
        var theme = GetEffectiveTheme();

        _layout = _layoutEngine.Layout(_document, width: width, theme: theme, scale: scale, textMeasurer: _textMeasurer);
        Height = _layout.Height;

        UpdateViewportFromScrollViewer();
        InvalidateRender();
    }

    private float GetScale()
    {
        var s = XamlRoot?.RasterizationScale ?? 1.0;
        return (float)Math.Max(0.5, s);
    }

    private MarkdownTheme GetEffectiveTheme()
    {
        var t = Theme ?? MarkdownTheme.Light;
        var baseUri = ImageBaseUri;
        if (baseUri is null)
        {
            return t;
        }

        return new MarkdownTheme
        {
            Typography = t.Typography,
            Colors = t.Colors,
            Metrics = t.Metrics,
            Selection = t.Selection,
            ImageBaseUri = baseUri,
        };
    }

    private void InvalidateRender()
    {
        if (_layout is null)
        {
            return;
        }

        if (_viewportHeight <= 0 || ActualWidth <= 0)
        {
            return;
        }

        var scale = GetScale();
        var pixelWidth = (int)Math.Max(1, Math.Ceiling(ActualWidth * scale));
        var pixelHeight = (int)Math.Max(1, Math.Ceiling((_viewportHeight > 0 ? _viewportHeight : ActualHeight) * scale));

        if (_bitmap is null || pixelWidth != _bitmapPixelWidth || pixelHeight != _bitmapPixelHeight)
        {
            _bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
            _bitmapPixelWidth = pixelWidth;
            _bitmapPixelHeight = pixelHeight;
            _image.Source = _bitmap;
        }

        RenderToBitmap(_bitmap, pixelWidth, pixelHeight, scale);
    }

    private void RenderToBitmap(WriteableBitmap bitmap, int pixelWidth, int pixelHeight, float scale)
    {
        if (_layout is null)
        {
            return;
        }

        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var rowBytes = info.RowBytes;

        var buffer = new byte[rowBytes * pixelHeight];

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();
            using var surface = SKSurface.Create(info, ptr, rowBytes);

            var canvas = surface.Canvas;
            var theme = GetEffectiveTheme();

            var bg = theme.Colors.PageBackground;
            canvas.Clear(new SKColor((byte)bg.R, (byte)bg.G, (byte)bg.B, (byte)bg.A));

            var viewportTop = (float)_viewportTop;
            var viewportHeight = (float)Math.Max(1, _viewportHeight > 0 ? _viewportHeight : ActualHeight);

            canvas.Save();
            canvas.Scale(scale);
            canvas.Translate(0, -viewportTop);

            _renderer.Render(_layout, new RenderContext
            {
                Canvas = canvas,
                Theme = theme,
                Viewport = new RectF(0, viewportTop, _layout.Width, viewportHeight),
                Scale = 1,
                Overscan = 48,
                ImageResolver = ResolveImage,
                Selection = _selection,
            });

            canvas.Restore();
            surface.Flush();
        }
        finally
        {
            handle.Free();
        }

        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(buffer, 0, buffer.Length);
        bitmap.Invalidate();
    }

    public async Task CopySelectionToClipboardAsync(bool includePlainText = true)
    {
        if (_document is null || _layout is null || _selection is null)
        {
            return;
        }

        if (!SelectionSourceMapper.TryMapToSource(Markdown ?? string.Empty, _document, _selection.Value, out var sourceSel))
        {
            return;
        }

        var selectedMarkdown = sourceSel.Slice(Markdown ?? string.Empty);
        if (string.IsNullOrEmpty(selectedMarkdown))
        {
            return;
        }

        var plain = includePlainText ? MarkdownPlainTextExtractor.Extract(selectedMarkdown) : selectedMarkdown;

        var package = new DataPackage();
        package.RequestedOperation = DataPackageOperation.Copy;

        package.SetData("text/markdown", selectedMarkdown);
        package.SetText(plain);

        Clipboard.SetContent(package);
        Clipboard.Flush();

        await Task.CompletedTask;
    }

    private SKImage? ResolveImage(Uri uri)
    {
        if (_imageCache.TryGetValue(uri, out var cached))
        {
            return cached;
        }

        if (_imageLoads.ContainsKey(uri))
        {
            return null;
        }

        _imageLoads[uri] = LoadImageAsync(uri);
        return null;
    }

    private async Task LoadImageAsync(Uri uri)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(uri).ConfigureAwait(false);
            using var data = SKData.CreateCopy(bytes);
            var image = SKImage.FromEncodedData(data);
            if (image is not null)
            {
                _imageCache[uri] = image;
            }
        }
        catch
        {
            // Ignore image load failures; placeholder remains.
        }
        finally
        {
            _imageLoads.TryRemove(uri, out _);

            var dq = DispatcherQueue;
            if (dq is not null)
            {
                dq.TryEnqueue(InvalidateRender);
            }
        }
    }

    private ScrollViewer? FindParentScrollViewer()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is ScrollViewer sv)
            {
                return sv;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
