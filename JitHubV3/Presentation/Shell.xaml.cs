using Microsoft.Extensions.Logging;

namespace JitHubV3.Presentation;

public sealed partial class Shell : UserControl
{
    private const double StatusBarBreakpointWidth = 600d;
    private ILogger<Shell>? _logger;
    private double _lastMeasuredRootGridWidth;
    private bool? _lastAppliedIsNarrow;
    private bool _isUnloaded;

    public Shell()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RootGrid.SizeChanged += OnRootGridSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _logger ??= (Application.Current as App)?.Services?.GetService<ILogger<Shell>>();

        if (DataContext is ShellViewModel vm)
        {
            vm.StatusBar.AttachToCurrentThread();
            vm.StatusBar.Set(isBusy: false, isRefreshing: false);
        }

        ApplyStatusBarLayout("Loaded");

        _ = DispatcherQueue.TryEnqueue(() => ApplyStatusBarLayout("Loaded.Enqueue"));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;

        // On some platforms (notably Skia desktop), named XAML fields may already be cleared
        // during teardown. Make unhooking idempotent and null-safe.
        if (RootGrid is not null)
        {
            RootGrid.SizeChanged -= OnRootGridSizeChanged;
        }

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _lastMeasuredRootGridWidth = e.NewSize.Width;
        ApplyStatusBarLayout("RootGrid.SizeChanged");
    }

    private void ApplyStatusBarLayout(string reason)
    {
        if (_isUnloaded || RootGrid is null || TopStatusBar is null || BottomStatusBar is null)
        {
            return;
        }

        var gridWidth = RootGrid.ActualWidth;
        var xamlRootWidth = XamlRoot?.Size.Width ?? 0d;
        var rasterizationScale = XamlRoot?.RasterizationScale ?? 0d;

        var effectiveWidth = _lastMeasuredRootGridWidth > 0
            ? _lastMeasuredRootGridWidth
            : (gridWidth > 0 ? gridWidth : xamlRootWidth);

        var isNarrow = effectiveWidth > 0 && effectiveWidth < StatusBarBreakpointWidth;

        if (_lastAppliedIsNarrow == isNarrow)
        {
            return;
        }

        _lastAppliedIsNarrow = isNarrow;

        TopStatusBar.Visibility = isNarrow ? Visibility.Visible : Visibility.Collapsed;
        BottomStatusBar.Visibility = isNarrow ? Visibility.Collapsed : Visibility.Visible;

        _logger?.LogDebug(
            "Shell status bar layout ({Reason}): MeasuredWidth={MeasuredWidth:0.##}, GridWidth={GridWidth:0.##}, XamlRootWidth={XamlRootWidth:0.##}, RasterizationScale={RasterizationScale:0.###}, EffectiveWidth={EffectiveWidth:0.##}, Breakpoint={Breakpoint:0.##}, IsNarrow={IsNarrow}, Top={Top}, Bottom={Bottom}",
            reason,
            _lastMeasuredRootGridWidth,
            gridWidth,
            xamlRootWidth,
            rasterizationScale,
            effectiveWidth,
            StatusBarBreakpointWidth,
            isNarrow,
            TopStatusBar.Visibility,
            BottomStatusBar.Visibility);
    }

#if WINDOWS || __SKIA__ || __ANDROID__ || __IOS__ || __MACOS__ || __MACCATALYST__
    public ContentControl ContentControl => Splash;
#endif
}
