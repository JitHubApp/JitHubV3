using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;

namespace JitHub.Markdown.Uno;

public sealed partial class MarkdownView : ContentControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownView),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme),
        typeof(MarkdownTheme),
        typeof(MarkdownView),
        new PropertyMetadata(MarkdownTheme.Light, OnThemeChanged));

    public static readonly DependencyProperty ImageBaseUriProperty = DependencyProperty.Register(
        nameof(ImageBaseUri),
        typeof(Uri),
        typeof(MarkdownView),
        new PropertyMetadata(null, OnThemeChanged));

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

    public MarkdownView()
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

    private ScrollViewer? _scrollViewer;
    private double _viewportTop;
    private double _viewportHeight;

    private WriteableBitmap? _bitmap;
    private int _bitmapPixelWidth;
    private int _bitmapPixelHeight;

    private readonly ConcurrentDictionary<Uri, SKImage> _imageCache = new();
    private readonly ConcurrentDictionary<Uri, Task> _imageLoads = new();
    private readonly HttpClient _http = new();

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).RebuildDocumentAndLayout();
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).RebuildLayoutOnly();
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-layout when width changes; height is driven by layout output.
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

        _viewportTop = _scrollViewer.VerticalOffset;
        _viewportHeight = _scrollViewer.ViewportHeight;

        if (_viewportHeight <= 0)
        {
            _viewportHeight = ActualHeight;
        }

        Canvas.SetTop(_image, _viewportTop);
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

        // Theme is init-only; create a shallow clone with ImageBaseUri applied.
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

        // Create a surface backed by a managed buffer, then copy into the WriteableBitmap.
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

            // Translate so the viewport segment is drawn into the small surface.
            canvas.Save();
            canvas.Scale(scale);
            canvas.Translate(0, -viewportTop);

            _renderer.Render(_layout, new RenderContext
            {
                Canvas = canvas,
                Theme = theme,
                Viewport = new RectF(0, viewportTop, _layout.Width, viewportHeight),
                Scale = 1, // already applied via canvas.Scale(scale)
                Overscan = 48,
                ImageResolver = ResolveImage,
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
