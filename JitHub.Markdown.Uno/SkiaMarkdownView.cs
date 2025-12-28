using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Extensions.Logging;
using Markdig;
using SkiaSharp;
using JitHub.Markdown;
using Uno.Extensions;

namespace JitHub.Markdown.Uno;

public class SkiaMarkdownView : ContentControl
{
    public static readonly DependencyProperty GitHubBaseUrlProperty = DependencyProperty.Register(
        nameof(GitHubBaseUrl),
        typeof(string),
        typeof(SkiaMarkdownView),
        new PropertyMetadata(string.Empty, OnGitHubEnrichmentsChanged));

    public static readonly DependencyProperty GitHubRepositorySlugProperty = DependencyProperty.Register(
        nameof(GitHubRepositorySlug),
        typeof(string),
        typeof(SkiaMarkdownView),
        new PropertyMetadata(string.Empty, OnGitHubEnrichmentsChanged));

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

    public static readonly DependencyProperty IsRightToLeftProperty = DependencyProperty.Register(
        nameof(IsRightToLeft),
        typeof(bool),
        typeof(SkiaMarkdownView),
        new PropertyMetadata(GetPlatformIsRtl(), OnIsRightToLeftChanged));

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

    /// <summary>
    /// Controls the base text direction for the markdown layout engine.
    /// Note: this is intentionally separate from XAML FlowDirection because applying RTL FlowDirection
    /// to a Skia-rendered surface will mirror the entire drawing output.
    /// </summary>
    public bool IsRightToLeft
    {
        get => (bool)GetValue(IsRightToLeftProperty);
        set => SetValue(IsRightToLeftProperty, value);
    }

    /// <summary>
    /// Optional base URL for GitHub enrichments (e.g. "https://github.com" or a mock like "https://example.invalid").
    /// When set (or when <see cref="GitHubRepositorySlug"/> is set), GitHub enrichments are enabled.
    /// </summary>
    public string GitHubBaseUrl
    {
        get => (string)GetValue(GitHubBaseUrlProperty);
        set => SetValue(GitHubBaseUrlProperty, value);
    }

    /// <summary>
    /// Optional repository slug in the form "owner/repo".
    /// Enables #123 and commit SHA linkification when provided.
    /// </summary>
    public string GitHubRepositorySlug
    {
        get => (string)GetValue(GitHubRepositorySlugProperty);
        set => SetValue(GitHubRepositorySlugProperty, value);
    }

    public SelectionRange? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            RequestRender();
        }
    }

    public event EventHandler<string>? LinkActivated;

    private SkiaMarkdownViewAutomationPeer? _automationPeer;

    protected override AutomationPeer OnCreateAutomationPeer()
        => _automationPeer ??= new SkiaMarkdownViewAutomationPeer(this);

    public SkiaMarkdownView()
    {
        IsTabStop = true;
        // Prevent XAML FlowDirection from mirroring the Skia surface.
        // RTL is handled by the layout engine (alignment, gutters, etc.).
        FlowDirection = FlowDirection.LeftToRight;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        // Uno docs: when nested in a ScrollViewer, a control can receive PointerCancelled as soon as scrolling is detected.
        // Setting ManipulationMode to something other than System prevents that behavior.
        ManipulationMode = ManipulationModes.None;

        // Uno docs (Routed Events): ensure pointer events bubble in managed code for this subtree.
        // We use reflection to avoid hard dependency on Uno.UI.Xaml types in targets where they're not referenced.
        TryEnableManagedPointerBubbling(this);

        _engine = CreateEngine(GitHubBaseUrl, GitHubRepositorySlug);
        _layoutEngine = new MarkdownLayoutEngine();
        _layoutEngine.DefaultIsRtl = IsRightToLeft;
        _textMeasurer = new SkiaTextShaper();
        _renderer = new SkiaMarkdownRenderer();

        var enableSyntaxDiag = false;
#if DEBUG
        enableSyntaxDiag = true;
#else
        // Opt-in for non-debug builds.
        enableSyntaxDiag = string.Equals(Environment.GetEnvironmentVariable("JITHUB_SYNTAXHL_DIAG"), "1", StringComparison.Ordinal);
#endif

        if (enableSyntaxDiag)
        {
            var log = this.Log();
            log.LogWarning("[SyntaxHL] diagnostics ENABLED");
            SyntaxHighlightDiagnostics.Enable(msg =>
            {
                // Always emit to Debug output (useful on platforms where logging is filtered).
                Debug.WriteLine(msg);
                // Also emit via Uno logging; Warning is far more likely to be visible by default.
                log.LogWarning(msg);
            });
        }

        _canvas = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _image = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = true,
        };

        _linkFocusOverlay = new Canvas
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        _canvas.Children.Add(_image);
        _canvas.Children.Add(_linkFocusOverlay);
        Content = _canvas;

        // Ensure we can receive pointer events even when the hit-test source is this ContentControl.
        AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerCanceled), true);
        AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), true);

        // NOTE: Do not register the same handlers multiple times on the subtree.
        // This control uses AddHandler(..., handledEventsToo: true) on itself plus managed bubbling.
        // Duplicating handlers on _canvas/_image will cause every input event to be processed multiple times,
        // which breaks selection reliability and severely harms performance.

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        LostFocus += OnLostFocus;

        RebuildDocumentAndLayout();
    }

    private static void OnGitHubEnrichmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SkiaMarkdownView)d).SyncGitHubEnrichments();
    }

    private void SyncGitHubEnrichments()
    {
        _engine = CreateEngine(GitHubBaseUrl, GitHubRepositorySlug);
        RebuildDocumentAndLayout();
    }

    private static MarkdownEngine CreateEngine(string baseUrl, string repositorySlug)
    {
        // Keep CreateDefault behavior unless enrichments are explicitly enabled.
        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(repositorySlug))
        {
            return MarkdownEngine.CreateDefault();
        }

        var options = new MarkdownParserOptions
        {
            ConfigurePipeline = b => b.UseAdvancedExtensions(),
        };

        var gh = new GitHubEnrichmentsOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://github.com" : baseUrl,
            RepositorySlug = string.IsNullOrWhiteSpace(repositorySlug) ? null : repositorySlug,
            AllowShortShas = true,
        };

        return MarkdownEngine.Create(options, new GitHubEnrichmentsPlugin(gh));
    }

    private static bool GetPlatformIsRtl()
        => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    private static void OnIsRightToLeftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (SkiaMarkdownView)d;
        view._layoutEngine.DefaultIsRtl = (bool)e.NewValue;
        view.RebuildLayoutOnly();
    }

    private MarkdownEngine _engine;
    private readonly MarkdownLayoutEngine _layoutEngine;
    private readonly ITextMeasurer _textMeasurer;
    private readonly SkiaMarkdownRenderer _renderer;
    private readonly Grid _canvas;
    private readonly Image _image;
    private readonly Canvas _linkFocusOverlay;

    private readonly List<Border> _linkFocusRects = new();

    private MarkdownDocumentModel? _document;
    private MarkdownLayout? _layout;
    private SelectionRange? _selection;

    private ScrollViewer? _scrollViewer;
    private double _viewportTop;
    private double _viewportHeight;

    private WriteableBitmap? _bitmap;
    private int _bitmapPixelWidth;
    private int _bitmapPixelHeight;

    private bool _renderQueued;
    private byte[]? _renderPixelBuffer;

    private readonly ConcurrentDictionary<Uri, SKImage> _imageCache = new();
    private readonly ConcurrentDictionary<Uri, Task> _imageLoads = new();
    private readonly HttpClient _http = new();

    private readonly SelectionPointerInteraction _pointerInteraction = new();
    private readonly SelectionKeyboardInteraction _keyboardInteraction = new();
    private bool _isShiftDown;
    private bool _hasPointerCapture;
    private bool _isSelecting;

#if DEBUG
    // Throttles high-signal selection diagnostics.
    private (int lineIndex, int runIndex, int textOffset)? _lastEndCaretDebug;
    private DateTimeOffset _lastEndCaretDebugAt;
#endif

    private uint? _activePointerId;
    private Microsoft.UI.Xaml.Input.Pointer? _activePointer;
    private bool _isPointerDown;
    private MarkdownHitTestResult? _lastPointerMoveHit;
    private Windows.Foundation.Point? _lastPointerPositionInImage;

    private bool _touchLongPressArmed;
    private bool _touchSelectionStarted;
    private float _touchPressX;
    private float _touchPressY;
    private MarkdownHitTestResult _touchPressHit;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _touchLongPressTimer;

    private const float TouchMoveCancelThreshold = 8f;
    private static readonly TimeSpan TouchLongPressDelay = TimeSpan.FromMilliseconds(500);

    private static void TryEnableManagedPointerBubbling(DependencyObject element)
    {
        try
        {
            var prop = element.GetType().GetProperty("EventsBubblingInManagedCode");
            if (prop is null || !prop.CanWrite)
            {
                return;
            }

            var enumType = prop.PropertyType;
            if (!enumType.IsEnum)
            {
                return;
            }

            var flags = new[] { "PointerPressed", "PointerMoved", "PointerReleased", "PointerCanceled" };
            ulong value = 0;
            foreach (var flag in flags)
            {
                var parsed = Enum.Parse(enumType, flag);
                value |= Convert.ToUInt64(parsed);
            }

            var boxed = Enum.ToObject(enumType, value);
            prop.SetValue(element, boxed);
        }
        catch
        {
            // Ignore: feature not available on this target.
        }
    }

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

        HookOutsidePointerDismiss();

        RequestRender();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hasPointerCapture = false;

        UnhookOutsidePointerDismiss();

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

        if (_renderPixelBuffer is not null)
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(_renderPixelBuffer);
            _renderPixelBuffer = null;
        }
    }

    private UIElement? _outsidePointerRoot;
    private PointerEventHandler? _outsidePointerPressedHandler;

    private void HookOutsidePointerDismiss()
    {
        // Attach to the top-level visual root so we can clear selection when the user clicks anywhere
        // outside this markdown view (even if the target doesn't take focus).
        if (_outsidePointerRoot is not null)
        {
            return;
        }

        var root = XamlRoot?.Content as UIElement;
        if (root is null)
        {
            return;
        }

        _outsidePointerRoot = root;
        _outsidePointerPressedHandler = new PointerEventHandler(OnOutsidePointerPressed);
        _outsidePointerRoot.AddHandler(UIElement.PointerPressedEvent, _outsidePointerPressedHandler, true);
    }

    private void UnhookOutsidePointerDismiss()
    {
        if (_outsidePointerRoot is null || _outsidePointerPressedHandler is null)
        {
            _outsidePointerRoot = null;
            _outsidePointerPressedHandler = null;
            return;
        }

        _outsidePointerRoot.RemoveHandler(UIElement.PointerPressedEvent, _outsidePointerPressedHandler);
        _outsidePointerRoot = null;
        _outsidePointerPressedHandler = null;
    }

    private void OnOutsidePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // If there's no active selection, nothing to dismiss.
        if (Selection is null)
        {
            return;
        }

        // If the click is within this view subtree, let normal pointer logic handle it.
        if (IsWithinThis(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ClearSelectionAndRefresh();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (Selection is null)
        {
            return;
        }

        ClearSelectionAndRefresh();
    }

    private bool IsWithinThis(DependencyObject? obj)
    {
        var cur = obj;
        while (cur is not null)
        {
            if (ReferenceEquals(cur, this))
            {
                return true;
            }

            cur = VisualTreeHelper.GetParent(cur);
        }

        return false;
    }

    private void ClearSelectionAndRefresh()
    {
        _pointerInteraction.ClearSelection();
        _keyboardInteraction.Selection = null;
        ClearKeyboardLinkFocus();
        Selection = null;
    }

    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var log = this.Log();
        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("[SkiaMarkdownView] PointerPressed id={PointerId} type={PointerType} selectionEnabled={SelectionEnabled}",
                e.Pointer.PointerId,
                e.Pointer.PointerDeviceType,
                SelectionEnabled);
        }

        if (_layout is null)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("[SkiaMarkdownView] PointerPressed ignored (no layout yet)");
            }
            return;
        }

        // Ensure keyboard focus moves here (TextBox above can otherwise keep focus).
        _ = Focus(FocusState.Pointer);

        var pos = GetPointerPositionForHitTest(e);

        if (string.Equals(e.Pointer.PointerDeviceType.ToString(), "Mouse", StringComparison.OrdinalIgnoreCase))
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (!props.IsLeftButtonPressed)
            {
                return;
            }
        }

        if (!MarkdownHitTester.TryHitTestNearest(_layout, (float)pos.X, (float)pos.Y, out var hit))
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("[SkiaMarkdownView] PointerPressed hit-test miss at ({X},{Y})", pos.X, pos.Y);
            }
            // If we leave _isPointerDown=true on a miss, some platforms may not deliver PointerReleased,
            // and then PointerMoved could keep extending selection indefinitely.
            ResetPointerTracking();
            return;
        }

        _isPointerDown = true;
        _activePointerId = e.Pointer.PointerId;
        _activePointer = e.Pointer;

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

        SyncSelectionFromPointerToKeyboard();
        ClearKeyboardLinkFocus();

        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("[SkiaMarkdownView] PointerDown selectionChanged={SelectionChanged} selection={Selection}",
                result.SelectionChanged,
                Selection?.ToString() ?? "<null>");
        }

        _isSelecting = SelectionEnabled && result.SelectionChanged;

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

        if (_activePointerId.HasValue && e.Pointer.PointerId != _activePointerId.Value)
        {
            return;
        }

        if (!_isPointerDown)
        {
            return;
        }

        // Some platforms may not deliver PointerReleased (or capture is lost). If the pointer is no longer
        // pressed/in contact, treat this as an implicit release so selection can't get stuck.
        try
        {
            var pp = e.GetCurrentPoint(this);
            var props = pp.Properties;

            var device = e.Pointer.PointerDeviceType.ToString();
            var isStillDown = pp.IsInContact;

            // For mouse we prefer the explicit left-button state.
            if (string.Equals(device, "Mouse", StringComparison.OrdinalIgnoreCase))
            {
                isStillDown = props.IsLeftButtonPressed;
            }

            if (!isStillDown)
            {
                var log = this.Log();
                if (log.IsEnabled(LogLevel.Debug))
                {
                    log.LogDebug("[SkiaMarkdownView] PointerMoved observed no buttons down; canceling stuck gesture (id={PointerId})", e.Pointer.PointerId);
                }
                ReleaseCapture(e);
                ResetPointerTracking();
                return;
            }
        }
        catch
        {
            // If anything about pointer properties isn't supported on this target, ignore.
        }

        var pos = GetPointerPositionForHitTest(e);

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

            if (MarkdownHitTester.TryHitTestNearest(_layout, (float)pos.X, (float)pos.Y, out var moveHitForCancel))
            {
                _pointerInteraction.OnPointerMove(
                    moveHitForCancel,
                    x: (float)pos.X,
                    y: (float)pos.Y,
                    selectionEnabled: false);
            }

            return;
        }

        var x = (float)pos.X;
        var y = (float)pos.Y;

        MarkdownHitTestResult hit;

        // Phase 6.6.4: fast-path when staying within the same line band.
        // PointerMoved is very hot, so avoid nearest-line lookup if we can.
        if (_lastPointerMoveHit is { } last && y >= last.Line.Y && y <= (last.Line.Y + last.Line.Height))
        {
            if (!MarkdownHitTester.TryHitTestLine(last.LineIndex, last.Line, x, out hit))
            {
                // Fallback if something about the cached line is no longer usable.
                if (!MarkdownHitTester.TryHitTestNearest(_layout, x, y, out hit))
                {
                    return;
                }
            }
        }
        else
        {
            if (!MarkdownHitTester.TryHitTestNearest(_layout, x, y, out hit))
            {
                return;
            }
        }

        _lastPointerMoveHit = hit;

    #if DEBUG
        LogEndCaretDiagnosticIfSuspicious(hit, x: x, y: y);
    #endif

        var result = _pointerInteraction.OnPointerMove(
            hit,
            x: x,
            y: y,
            selectionEnabled: SelectionEnabled);

        if (result.SelectionChanged)
        {
            Selection = result.Selection;
            _isSelecting = true;
        }

        SyncSelectionFromPointerToKeyboard();

        // Handle moves when we're actively selecting (CapturePointer may not be supported consistently).
        if (_hasPointerCapture || _touchSelectionStarted || _isSelecting)
        {
            e.Handled = true;
        }
    }

#if DEBUG
    private void LogEndCaretDiagnosticIfSuspicious(MarkdownHitTestResult hit, float x, float y)
    {
        // Only log for mouse-driven selection scenarios (the reported issue).
        if (!_isPointerDown || !SelectionEnabled || _layout is null)
        {
            return;
        }

        var run = hit.Run;
        if (run.Kind == NodeKind.Image || string.IsNullOrEmpty(run.Text))
        {
            return;
        }

        // Focus on the classic symptom: pointer is at/near the right edge, but caret/offset doesn't reach end.
        var right = run.Bounds.Right;
        var left = run.Bounds.X;

        // Ignore obviously out-of-bounds X.
        if (x < left - 64 || x > right + 256)
        {
            return;
        }

        var slop = 6f;
        var isNearRightEdge = !run.IsRightToLeft && x >= (right - slop);
        var isNearLeftEdgeRtl = run.IsRightToLeft && x <= (left + slop);
        if (!isNearRightEdge && !isNearLeftEdgeRtl)
        {
            return;
        }

        var expectedEndOffset = run.Text.Length;
        var endCaretX = MarkdownHitTester.GetCaretX(run, expectedEndOffset);

        var suspicious = false;
        if (!run.IsRightToLeft)
        {
            if (hit.TextOffset < expectedEndOffset)
            {
                suspicious = true;
            }
            else if (endCaretX < (right - 0.5f))
            {
                suspicious = true;
            }
        }
        else
        {
            // RTL: end offset is still Text.Length, but visually maps to the left edge.
            if (hit.TextOffset < expectedEndOffset)
            {
                suspicious = true;
            }
            else if (endCaretX > (left + 0.5f))
            {
                suspicious = true;
            }
        }

        if (!suspicious)
        {
            return;
        }

        // Throttle: log at most once per 500ms for the same (line,run,offset).
        var key = (hit.LineIndex, hit.RunIndex, hit.TextOffset);
        var now = DateTimeOffset.UtcNow;
        if (_lastEndCaretDebug is { } lastKey && lastKey == key && (now - _lastEndCaretDebugAt).TotalMilliseconds < 500)
        {
            return;
        }

        _lastEndCaretDebug = key;
        _lastEndCaretDebugAt = now;

        var log = this.Log();
        log.LogWarning(
            "[SkiaMarkdownView] EndCaret suspicious: x={X} y={Y} viewportTop={ViewportTop} scale={RasterScale} layoutW={LayoutW} | line={LineIndex} run={RunIndex} rtl={IsRtl} run=({L},{T},{W},{H}) right={Right} | hitOffset={HitOffset}/{Len} hitCaretX={HitCaretX} endCaretX={EndCaretX} glyphLast={GlyphLast}",
            x, y,
            _viewportTop,
            GetScale(),
            _layout.Width,
            hit.LineIndex,
            hit.RunIndex,
            run.IsRightToLeft,
            run.Bounds.X,
            run.Bounds.Y,
            run.Bounds.Width,
            run.Bounds.Height,
            right,
            hit.TextOffset,
            run.Text.Length,
            hit.CaretX,
            endCaretX,
            (!run.GlyphX.IsDefault && run.GlyphX.Length > 0) ? run.GlyphX[run.GlyphX.Length - 1] : float.NaN);
    }
#endif

    private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var log = this.Log();
        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("[SkiaMarkdownView] PointerReleased id={PointerId}", e.Pointer.PointerId);
        }
        if (_layout is null)
        {
            return;
        }

        if (_activePointerId.HasValue && e.Pointer.PointerId != _activePointerId.Value)
        {
            return;
        }

        CancelTouchLongPress();

        var pos = GetPointerPositionForHitTest(e);

        if (!MarkdownHitTester.TryHitTestNearest(_layout, (float)pos.X, (float)pos.Y, out var hit))
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

        SyncSelectionFromPointerToKeyboard();

        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("[SkiaMarkdownView] PointerUp selectionChanged={SelectionChanged} activateLink={ActivateLink}",
                result.SelectionChanged,
                string.IsNullOrWhiteSpace(result.ActivateLinkUrl) ? "<none>" : result.ActivateLinkUrl);
        }

        if (!string.IsNullOrWhiteSpace(result.ActivateLinkUrl))
        {
            _ = OpenLinkAsync(result.ActivateLinkUrl);
            LinkActivated?.Invoke(this, result.ActivateLinkUrl);
        }

        // Decide handled *before* we clear capture/flags.
        var shouldHandle = _hasPointerCapture || _touchSelectionStarted || _isSelecting || !string.IsNullOrWhiteSpace(result.ActivateLinkUrl);
        e.Handled = shouldHandle;

        ReleaseCapture(e);
        _isPointerDown = false;
        _isSelecting = false;

        ResetPointerTracking();
    }

    private void OnPointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CancelTouchLongPress();
        ReleaseCapture(e);
        _isSelecting = false;
        ResetPointerTracking();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Capture can be lost without a corresponding PointerReleased.
        // If we don't reset, the control keeps thinking the pointer is down and selection continues forever.
        CancelTouchLongPress();
        _hasPointerCapture = false;
        _isSelecting = false;
        ResetPointerTracking();
        e.Handled = true;
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
        _pointerInteraction.CancelPointer();
        _activePointerId = null;
        _activePointer = null;
        _isPointerDown = false;
        _lastPointerMoveHit = null;
        _lastPointerPositionInImage = null;
        _touchLongPressArmed = false;
        _touchSelectionStarted = false;
        _isSelecting = false;

        SyncSelectionFromPointerToKeyboard();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Shift)
        {
            _isShiftDown = true;
            return;
        }

        if (_layout is null)
        {
            return;
        }

        if (!TryMapKey(e.Key, out var cmd))
        {
            return;
        }

        SyncSelectionFromPointerToKeyboard();

        var shift = _isShiftDown;

        var result = _keyboardInteraction.OnKeyCommand(
            _layout,
            cmd,
            selectionEnabled: SelectionEnabled,
            shift: shift);

        if (!result.Handled)
        {
            return;
        }

        e.Handled = true;

        if (result.SelectionChanged && result.Selection is { } sel)
        {
            ApplySelectionFromKeyboard(sel);

            // Phase 6.6.2: Arrow-key caret navigation should keep the caret visible.
            // Tab/Shift+Tab focus changes are handled separately via EnsureFocusedLinkVisible().
            if (!result.FocusChanged && cmd is MarkdownKeyCommand.Left or MarkdownKeyCommand.Right or MarkdownKeyCommand.Up or MarkdownKeyCommand.Down)
            {
                EnsureCaretVisible(sel);
            }
        }

        if (result.FocusChanged)
        {
            UpdateLinkFocusOverlay();
            EnsureFocusedLinkVisible();
            NotifyAutomationFocusOrNameChanged();
        }

        if (!string.IsNullOrWhiteSpace(result.ActivateLinkUrl))
        {
            _ = OpenLinkAsync(result.ActivateLinkUrl!);
            LinkActivated?.Invoke(this, result.ActivateLinkUrl!);
        }
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Shift)
        {
            _isShiftDown = false;
        }
    }

    private void ApplySelectionFromKeyboard(SelectionRange selection)
    {
        _pointerInteraction.SetSelection(selection);
        SyncSelectionFromPointerToKeyboard();
        Selection = selection;
    }

    private void SyncSelectionFromPointerToKeyboard()
    {
        _keyboardInteraction.Selection = _pointerInteraction.Selection;
    }

    private static bool TryMapKey(VirtualKey key, out MarkdownKeyCommand cmd)
    {
        switch (key)
        {
            case VirtualKey.Left:
                cmd = MarkdownKeyCommand.Left;
                return true;
            case VirtualKey.Right:
                cmd = MarkdownKeyCommand.Right;
                return true;
            case VirtualKey.Up:
                cmd = MarkdownKeyCommand.Up;
                return true;
            case VirtualKey.Down:
                cmd = MarkdownKeyCommand.Down;
                return true;
            case VirtualKey.Tab:
                cmd = MarkdownKeyCommand.Tab;
                return true;
            case VirtualKey.Enter:
                cmd = MarkdownKeyCommand.Enter;
                return true;
            default:
                cmd = default;
                return false;
        }
    }

    private void ClearKeyboardLinkFocus()
    {
        _keyboardInteraction.ClearLinkFocus();
        if (_linkFocusOverlay.Visibility != Visibility.Collapsed)
        {
            _linkFocusOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateLinkFocusOverlay()
    {
        var focused = _keyboardInteraction.FocusedLink;
        if (_layout is null || focused is null)
        {
            _linkFocusOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var id = focused.Value.Id;
        var url = focused.Value.Url;
        var rects = GetLinkRunBounds(_layout, id, url);

        if (rects.Count == 0)
        {
            // Fallback: use the single cached bounds.
            rects.Add(focused.Value.Bounds);
        }

        // Long links may be split into multiple runs (e.g. word-by-word). Merge adjacent
        // rectangles so the highlight matches the original link across a line.
        rects = MergeAdjacentRectsByLine(rects);

        EnsureLinkFocusRects(rects.Count);

        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            var box = _linkFocusRects[i];

            Canvas.SetLeft(box, r.X);
            Canvas.SetTop(box, r.Y);
            box.Width = r.Width;
            box.Height = r.Height;
            box.Visibility = Visibility.Visible;
        }

        for (var i = rects.Count; i < _linkFocusRects.Count; i++)
        {
            _linkFocusRects[i].Visibility = Visibility.Collapsed;
        }

        _linkFocusOverlay.Visibility = Visibility.Visible;
    }

    private static List<RectF> MergeAdjacentRectsByLine(List<RectF> rects)
    {
        if (rects.Count <= 1)
        {
            return rects;
        }

        // `GetLinkRunBounds` already sorts top-to-bottom, left-to-right.
        const float yTolerance = 2f;
        const float xGapTolerance = 6f;

        var merged = new List<RectF>(rects.Count);
        var current = rects[0];

        for (var i = 1; i < rects.Count; i++)
        {
            var r = rects[i];

            // Consider rects to be on the same line if their vertical bands overlap substantially.
            var currentBottom = current.Y + current.Height;
            var rBottom = r.Y + r.Height;
            var overlapTop = Math.Max(current.Y, r.Y);
            var overlapBottom = Math.Min(currentBottom, rBottom);
            var overlap = overlapBottom - overlapTop;

            var minHeight = Math.Min(current.Height, r.Height);
            var sameLine = overlap >= (minHeight * 0.6f) || (Math.Abs(current.Y - r.Y) <= yTolerance && Math.Abs(currentBottom - rBottom) <= yTolerance);

            var currentRight = current.X + current.Width;
            var rRight = r.X + r.Width;
            var closeHorizontally = r.X <= (currentRight + xGapTolerance);

            if (sameLine && closeHorizontally)
            {
                var left = Math.Min(current.X, r.X);
                var top = Math.Min(current.Y, r.Y);
                var right = Math.Max(currentRight, rRight);
                var bottom = Math.Max(currentBottom, rBottom);
                current = new RectF(left, top, right - left, bottom - top);
            }
            else
            {
                merged.Add(current);
                current = r;
            }
        }

        merged.Add(current);
        return merged;
    }

    private void EnsureLinkFocusRects(int count)
    {
        while (_linkFocusRects.Count < count)
        {
            var box = new Border
            {
                IsHitTestVisible = false,
                BorderThickness = new Thickness(2),
                BorderBrush = GetLinkFocusBrush(),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Visibility = Visibility.Collapsed,
            };

            _linkFocusRects.Add(box);
            _linkFocusOverlay.Children.Add(box);
        }
    }

    private static List<RectF> GetLinkRunBounds(MarkdownLayout layout, NodeId id, string? url)
    {
        var rects = new List<RectF>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return rects;
        }

        for (var i = 0; i < layout.Blocks.Length; i++)
        {
            CollectLinkRunBounds(layout.Blocks[i], id, url, rects);
        }

        // Sort for stable overlay order (top-to-bottom, left-to-right).
        rects.Sort(static (a, b) =>
        {
            var cy = a.Y.CompareTo(b.Y);
            return cy != 0 ? cy : a.X.CompareTo(b.X);
        });

        return rects;
    }

    private static void CollectLinkRunBounds(BlockLayout block, NodeId id, string url, List<RectF> rects)
    {
        switch (block)
        {
            case ParagraphLayout p:
                CollectLinkRunBounds(p.Lines, id, url, rects);
                break;
            case HeadingLayout h:
                CollectLinkRunBounds(h.Lines, id, url, rects);
                break;
            case CodeBlockLayout c:
                CollectLinkRunBounds(c.Lines, id, url, rects);
                break;
            case BlockQuoteLayout q:
                foreach (var child in q.Blocks)
                {
                    CollectLinkRunBounds(child, id, url, rects);
                }
                break;
            case ListLayout l:
                foreach (var item in l.Items)
                {
                    CollectLinkRunBounds(item, id, url, rects);
                }
                break;
            case ListItemLayout li:
                foreach (var child in li.Blocks)
                {
                    CollectLinkRunBounds(child, id, url, rects);
                }
                break;
            case TableLayout t:
                foreach (var row in t.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var child in cell.Blocks)
                        {
                            CollectLinkRunBounds(child, id, url, rects);
                        }
                    }
                }
                break;
        }
    }

    private static void CollectLinkRunBounds(ImmutableArray<LineLayout> lines, NodeId id, string url, List<RectF> rects)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            for (var j = 0; j < line.Runs.Length; j++)
            {
                var run = line.Runs[j];
                if (run.Kind != NodeKind.Link)
                {
                    continue;
                }

                if (run.Id != id)
                {
                    continue;
                }

                if (!string.Equals(run.Url, url, StringComparison.Ordinal))
                {
                    continue;
                }

                if (run.Bounds.Width <= 0 || run.Bounds.Height <= 0)
                {
                    continue;
                }

                rects.Add(run.Bounds);
            }
        }
    }

    private void NotifyAutomationFocusOrNameChanged()
    {
        // If UIA focus remains on the view itself (common when we handle Tab internally),
        // updating the Name + raising a focus-changed event gives Narrator something to announce.
        try
        {
            var focused = _keyboardInteraction.FocusedLink;
            if (focused is { } fl)
            {
                AutomationProperties.SetName(this, fl.Url);
            }
        }
        catch
        {
        }

        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this);
            peer?.InvalidatePeer();
            peer?.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
        }
        catch
        {
            // Uno may not implement parts of UIA across all targets.
        }
    }

    private void EnsureFocusedLinkVisible()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        var focused = _keyboardInteraction.FocusedLink;
        if (focused is null)
        {
            return;
        }

        var b = focused.Value.Bounds;
        if (b.Height <= 0)
        {
            return;
        }

        if (!TryGetScrollViewerContentYForControl(out var controlTopInContent))
        {
            return;
        }

        var linkTop = controlTopInContent + b.Y;
        var linkBottom = linkTop + b.Height;

        var viewTop = _scrollViewer.VerticalOffset;
        var viewBottom = viewTop + _scrollViewer.ViewportHeight;

        const double padding = 16;
        double? target = null;

        if (linkTop < viewTop + padding)
        {
            target = linkTop - padding;
        }
        else if (linkBottom > viewBottom - padding)
        {
            target = linkBottom - (_scrollViewer.ViewportHeight - padding);
        }

        if (target is null)
        {
            return;
        }

        try
        {
            var clamped = Math.Max(0, Math.Min(_scrollViewer.ScrollableHeight, target.Value));
            _scrollViewer.ChangeView(horizontalOffset: null, verticalOffset: clamped, zoomFactor: null, disableAnimation: true);
        }
        catch
        {
            // Ignore: platform-specific ChangeView failures.
        }
    }

    private void EnsureCaretVisible(SelectionRange selection)
    {
        if (_scrollViewer is null || _layout is null)
        {
            return;
        }

        // Keep the active caret in view (even when selection is extended).
        var caret = selection.Active;

        SelectionGeometry geom;
        try
        {
            geom = SelectionGeometryBuilder.Build(_layout, new SelectionRange(caret, caret));
        }
        catch
        {
            return;
        }

        if (geom.Rects.Length == 0)
        {
            return;
        }

        var r = geom.Rects[0];
        if (r.Height <= 0)
        {
            return;
        }

        if (!TryGetScrollViewerContentYForControl(out var controlTopInContent))
        {
            return;
        }

        var caretTop = controlTopInContent + r.Y;
        var caretBottom = caretTop + r.Height;

        var viewTop = _scrollViewer.VerticalOffset;
        var viewBottom = viewTop + _scrollViewer.ViewportHeight;

        const double padding = 16;
        double? target = null;

        if (caretTop < viewTop + padding)
        {
            target = caretTop - padding;
        }
        else if (caretBottom > viewBottom - padding)
        {
            target = caretBottom - (_scrollViewer.ViewportHeight - padding);
        }

        if (target is null)
        {
            return;
        }

        try
        {
            var clamped = Math.Max(0, Math.Min(_scrollViewer.ScrollableHeight, target.Value));
            _scrollViewer.ChangeView(horizontalOffset: null, verticalOffset: clamped, zoomFactor: null, disableAnimation: true);
        }
        catch
        {
            // Ignore: platform-specific ChangeView failures.
        }
    }

    internal MarkdownAccessibilityTree? BuildAccessibilityTreeForAutomation()
    {
        if (_layout is null)
        {
            return null;
        }

        var viewportHeight = (float)_viewportHeight;
        if (viewportHeight <= 0)
        {
            viewportHeight = (float)Math.Max(0, ActualHeight);
        }

        return MarkdownAccessibilityTreeBuilder.Build(
            _layout,
            viewportTop: (float)_viewportTop,
            viewportHeight: viewportHeight,
            overscan: 0);
    }

    internal Windows.Foundation.Rect GetAutomationBoundingRect(RectF bounds)
    {
        try
        {
            var transform = TransformToVisual(null);
            var tl = transform.TransformPoint(new Windows.Foundation.Point(bounds.X, bounds.Y));
            var br = transform.TransformPoint(new Windows.Foundation.Point(bounds.X + bounds.Width, bounds.Y + bounds.Height));
            return new Windows.Foundation.Rect(tl, br);
        }
        catch
        {
            return new Windows.Foundation.Rect();
        }
    }

    internal bool IsLinkFocusedForAutomation(NodeId id, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var focused = _keyboardInteraction.FocusedLink;
        return focused is { } fl
            && fl.Id == id
            && string.Equals(fl.Url, url, StringComparison.Ordinal);
    }

    internal void FocusLinkForAutomation(NodeId id, string url, RectF bounds)
    {
        if (_layout is null)
        {
            return;
        }

        // Focus the control itself first.
        Focus(FocusState.Programmatic);

        // Align keyboard state with current selection.
        SyncSelectionFromPointerToKeyboard();

        if (_keyboardInteraction.TryFocusLink(_layout, id, url) && _keyboardInteraction.Selection is { } sel)
        {
            ApplySelectionFromKeyboard(sel);
            UpdateLinkFocusOverlay();
            EnsureFocusedLinkVisible();
            NotifyAutomationFocusOrNameChanged();
            return;
        }

        // Fallback: if we can't focus by NodeId, at least ensure bounds are visible.
        EnsureAutomationBoundsVisible(bounds);
    }

    internal void EnsureAutomationBoundsVisible(RectF bounds)
    {
        if (_scrollViewer is null)
        {
            return;
        }

        if (bounds.Height <= 0)
        {
            return;
        }

        if (!TryGetScrollViewerContentYForControl(out var controlTopInContent))
        {
            return;
        }

        var top = controlTopInContent + bounds.Y;
        var bottom = top + bounds.Height;

        var viewTop = _scrollViewer.VerticalOffset;
        var viewBottom = viewTop + _scrollViewer.ViewportHeight;

        const double padding = 16;
        double? target = null;

        if (top < viewTop + padding)
        {
            target = top - padding;
        }
        else if (bottom > viewBottom - padding)
        {
            target = bottom - (_scrollViewer.ViewportHeight - padding);
        }

        if (target is null)
        {
            return;
        }

        try
        {
            var clamped = Math.Max(0, Math.Min(_scrollViewer.ScrollableHeight, target.Value));
            _scrollViewer.ChangeView(horizontalOffset: null, verticalOffset: clamped, zoomFactor: null, disableAnimation: true);
        }
        catch
        {
            // Ignore: platform-specific ChangeView failures.
        }
    }

    internal void ActivateLinkForAutomation(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _ = OpenLinkAsync(url);
        LinkActivated?.Invoke(this, url);
    }

    private bool TryGetScrollViewerContentYForControl(out double controlTopInContent)
    {
        controlTopInContent = 0;
        if (_scrollViewer is null)
        {
            return false;
        }

        try
        {
            // Convert control-local Y into ScrollViewer content Y.
            // viewportY = controlContentY - VerticalOffset  => controlContentY = viewportY + VerticalOffset
            var transform = TransformToVisual(_scrollViewer);
            var topLeftInViewport = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            controlTopInContent = _scrollViewer.VerticalOffset + topLeftInViewport.Y;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Brush GetLinkFocusBrush()
    {
        if (Application.Current?.Resources is not null)
        {
            if (Application.Current.Resources.TryGetValue("SystemControlHighlightAccentBrush", out var accent) && accent is Brush ab)
            {
                return ab;
            }

            if (Application.Current.Resources.TryGetValue("SystemControlForegroundBaseHighBrush", out var fg) && fg is Brush fb)
            {
                return fb;
            }
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private Windows.Foundation.Point GetPointerPositionForHitTest(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var p = LayoutSpace.GetLayoutPoint(e, _image, this, _viewportTop, out var lastInImage);
        _lastPointerPositionInImage = lastInImage;
        return p;
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
        _isSelecting = true;
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

        RefreshSelectionFromScrollIfNeeded();

        RequestRender();
    }

    private void RefreshSelectionFromScrollIfNeeded()
    {
        // When the user scrolls (e.g. wheel) while dragging selection, some targets don't emit PointerMoved.
        // The viewport changes, which changes the pointer's layout-space Y. Recompute selection from the
        // last known viewport-local pointer position.
        if (_layout is null || !_isPointerDown || !_isSelecting)
        {
            return;
        }

        if (_lastPointerPositionInImage is not { } p)
        {
            return;
        }

        var x = (float)p.X;
        var y = (float)(p.Y + _viewportTop);

        if (!MarkdownHitTester.TryHitTestNearest(_layout, x, y, out var hit))
        {
            return;
        }

        _lastPointerMoveHit = hit;
        var result = _pointerInteraction.OnPointerMove(hit, x: x, y: y, selectionEnabled: SelectionEnabled);
        if (!result.SelectionChanged)
        {
            return;
        }

        Selection = result.Selection;
        _isSelecting = true;
        SyncSelectionFromPointerToKeyboard();
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

            _image.Margin = new Thickness(0, _viewportTop, 0, 0);
            UpdateLinkFocusOverlay();
        }
        catch
        {
            // If transforms fail for any reason, fall back to rendering the whole control.
            _viewportTop = 0;
            _viewportHeight = ActualHeight;
            _image.Visibility = Visibility.Visible;
            _image.Margin = new Thickness(0);
            UpdateLinkFocusOverlay();
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
        // IMPORTANT:
        // Layout/hit-testing must be expressed in device-independent units (DIPs).
        // Rendering already applies rasterization scaling via canvas.Scale(rasterizationScale)
        // and the bitmap is sized in pixels accordingly.
        // If layout is also built with rasterizationScale, caret positions and bounds end up
        // in a different coordinate space than pointer positions, which can make the last
        // characters at the end of a line appear unselectable on high-DPI.
        const float scale = 1f;
        var theme = GetEffectiveTheme();

        _layout = _layoutEngine.Layout(_document, width: width, theme: theme, scale: scale, textMeasurer: _textMeasurer);
        Height = _layout.Height;

        _lastPointerMoveHit = null;

        // Layout changed; clear any keyboard link focus bounds.
        SyncSelectionFromPointerToKeyboard();
        ClearKeyboardLinkFocus();

        UpdateViewportFromScrollViewer();
        RequestRender();
    }

    private void RequestRender()
    {
        if (_layout is null)
        {
            return;
        }

        // Phase 6.6.3: coalesce invalidations (scroll/drag can fire very frequently).
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;

        var queue = DispatcherQueue;
        if (!queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _renderQueued = false;
                InvalidateRender();
            }))
        {
            _renderQueued = false;
            InvalidateRender();
        }
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

        // Phase 6.6.3: avoid per-render allocations.
        var required = checked(rowBytes * pixelHeight);
        var buffer = _renderPixelBuffer;
        if (buffer is null || buffer.Length < required)
        {
            if (buffer is not null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }

            buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(required);
            _renderPixelBuffer = buffer;
        }

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
                Plugins = _engine.Plugins,
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
        stream.Write(buffer, 0, required);
        bitmap.Invalidate();
    }

    public async Task CopySelectionToClipboardAsync(bool includePlainText = true)
    {
        if (_document is null || _layout is null || _selection is null)
        {
            return;
        }

        if (!SelectionSourceMapper.TryMapToSource(
            Markdown ?? string.Empty,
            _document,
            _selection.Value,
            _engine.Plugins.SelectionMappers,
            out var sourceSel))
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
