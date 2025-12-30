using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using System.Reflection;

namespace JitHub.Dashboard.Layouts;

/// <summary>
/// An <see cref="ItemsRepeater"/> that hosts <see cref="CardDeckLayout"/> and performs a smooth
/// morph animation when the effective mode switches (Grid ↔ Deck) on resize.
///
/// Uses storyboarded animations against an additive <see cref="CompositeTransform"/>
/// (cross-platform, Uno-friendly) so it does not fight the layout's own transforms.
/// </summary>
public sealed class CardDeckPresenter : ItemsRepeater
{
    public CardDeckPresenter()
    {
        _layout = new CardDeckLayout();
        Layout = _layout;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        ElementPrepared += OnElementPrepared;
        ElementClearing += OnElementClearing;
        LayoutUpdated += OnLayoutUpdated;
    }

    private readonly CardDeckLayout _layout;
    private readonly HashSet<int> _realizedIndices = new();
    private readonly HashSet<int> _swipingIndices = new();
    private readonly Dictionary<int, Snapshot> _snapshots = new();
    private readonly Dictionary<int, Snapshot> _lastPassSnapshots = new();
    private readonly Dictionary<long, Snapshot> _snapshotsByCardId = new();
    private readonly Dictionary<long, Snapshot> _lastPassSnapshotsByCardId = new();
    private readonly Dictionary<UIElement, int> _elementToIndex = new();
    private readonly Dictionary<long, UIElement> _cardIdToElement = new();
    private readonly Dictionary<UIElement, long> _elementToCardId = new();
    private readonly Dictionary<UIElement, Storyboard> _activeMorphStoryboards = new();
    private readonly Dictionary<UIElement, Storyboard> _activeSwipeStoryboards = new();
    private readonly Dictionary<UIElement, ZAnimationState> _activeZAnimations = new();

    private bool _isFrameTrackingEnabled;
    private DateTimeOffset _frameTrackingUntilUtc;

    private bool _isLoaded;
    private bool _isUnloaded;
    private LayoutSignature? _lastSignature;
    private int _lastItemCount;
    private bool _pendingMorph;
    private bool _pendingMorphUsesCardId;

    private readonly record struct ZAnimationState(double FromOffsetZ, DateTimeOffset StartedUtc, int DurationMs);

    public Task<bool> SwipeAsync(long cardId, CardSwipeDirection direction)
        => SwipeAsync(cardId, direction, CancellationToken.None);

    public async Task<bool> SwipeAsync(long cardId, CardSwipeDirection direction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_isUnloaded || !_isLoaded)
        {
            return false;
        }

        var index = TryFindIndexByCardId(cardId);
        if (index is null)
        {
            return false;
        }

        var element = TryGetElement(index.Value);
        if (element is null)
        {
            return false;
        }

        StopMorphStoryboard(element);
        StopSwipeStoryboard(element, resetToNeutral: false);

        _swipingIndices.Add(index.Value);

        var (_, animationTransform) = CardDeckLayout.EnsureTransforms(element);

        // Pivot around the center for a nicer "toss" feel.
        animationTransform.CenterX = element.RenderSize.Width / 2;
        animationTransform.CenterY = element.RenderSize.Height / 2;

        // Keep starting point as-is (supports swiping mid-morph).
        var fromX = animationTransform.TranslateX;
        var fromY = animationTransform.TranslateY;
        var fromR = animationTransform.Rotation;
        var fromOpacity = element.Opacity;

        var distanceX = Math.Max(ActualWidth, element.RenderSize.Width) + element.RenderSize.Width + 48;
        var distanceY = Math.Max(ActualHeight, element.RenderSize.Height) + element.RenderSize.Height + 48;

        double toX = 0;
        double toY = 0;
        double toR;

        switch (direction)
        {
            case CardSwipeDirection.Left:
                toX = -distanceX;
                toR = -8;
                break;
            case CardSwipeDirection.Right:
                toX = distanceX;
                toR = 8;
                break;
            case CardSwipeDirection.Up:
                toY = -distanceY;
                toR = -6;
                break;
            case CardSwipeDirection.Down:
                toY = distanceY;
                toR = 6;
                break;
            default:
                toR = 0;
                break;
        }

        const int durationMs = 280;
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var moveEase = new CubicEase { EasingMode = EasingMode.EaseIn };
        var opacityEase = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        var storyboard = new Storyboard();

        var xAnim = new DoubleAnimation { From = fromX, To = toX, Duration = duration, EasingFunction = moveEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(xAnim, animationTransform);
        Storyboard.SetTargetProperty(xAnim, nameof(CompositeTransform.TranslateX));
        storyboard.Children.Add(xAnim);

        var yAnim = new DoubleAnimation { From = fromY, To = toY, Duration = duration, EasingFunction = moveEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(yAnim, animationTransform);
        Storyboard.SetTargetProperty(yAnim, nameof(CompositeTransform.TranslateY));
        storyboard.Children.Add(yAnim);

        var rAnim = new DoubleAnimation { From = fromR, To = toR, Duration = duration, EasingFunction = moveEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(rAnim, animationTransform);
        Storyboard.SetTargetProperty(rAnim, nameof(CompositeTransform.Rotation));
        storyboard.Children.Add(rAnim);

        var oAnim = new DoubleAnimation { From = fromOpacity, To = 0, Duration = duration, EasingFunction = opacityEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(oAnim, element);
        Storyboard.SetTargetProperty(oAnim, nameof(UIElement.Opacity));
        storyboard.Children.Add(oAnim);

        var tcs = new TaskCompletionSource<bool>();

        _activeSwipeStoryboards[element] = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (_activeSwipeStoryboards.TryGetValue(element, out var current) && ReferenceEquals(current, storyboard))
            {
                _activeSwipeStoryboards.Remove(element);
            }

            _swipingIndices.Remove(index.Value);
            element.IsHitTestVisible = false;
            tcs.TrySetResult(true);
        };

        element.IsHitTestVisible = false;
        storyboard.Begin();

        using (cancellationToken.Register(() =>
        {
            StopSwipeStoryboard(element, resetToNeutral: true);
            _swipingIndices.Remove(index.Value);
            tcs.TrySetCanceled(cancellationToken);
        }))
        {
            return await tcs.Task;
        }
    }

    public CardDeckLayoutMode Mode
    {
        get => (CardDeckLayoutMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode),
            typeof(CardDeckLayoutMode),
            typeof(CardDeckPresenter),
            new PropertyMetadata(CardDeckLayoutMode.Auto, OnLayoutSettingsChanged));

    public double ModeWidthThreshold
    {
        get => (double)GetValue(ModeWidthThresholdProperty);
        set => SetValue(ModeWidthThresholdProperty, value);
    }

    public static readonly DependencyProperty ModeWidthThresholdProperty =
        DependencyProperty.Register(
            nameof(ModeWidthThreshold),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(900d, OnLayoutSettingsChanged));

    public double CardMinWidth
    {
        get => (double)GetValue(CardMinWidthProperty);
        set => SetValue(CardMinWidthProperty, value);
    }

    public static readonly DependencyProperty CardMinWidthProperty =
        DependencyProperty.Register(
            nameof(CardMinWidth),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(320d, OnLayoutSettingsChanged));

    public double CardHeight
    {
        get => (double)GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public static readonly DependencyProperty CardHeightProperty =
        DependencyProperty.Register(
            nameof(CardHeight),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(180d, OnLayoutSettingsChanged));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(16d, OnLayoutSettingsChanged));

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(
            nameof(MaxColumns),
            typeof(int),
            typeof(CardDeckPresenter),
            new PropertyMetadata(3, OnLayoutSettingsChanged));

    public int DeckMaxVisibleCount
    {
        get => (int)GetValue(DeckMaxVisibleCountProperty);
        set => SetValue(DeckMaxVisibleCountProperty, value);
    }

    public static readonly DependencyProperty DeckMaxVisibleCountProperty =
        DependencyProperty.Register(
            nameof(DeckMaxVisibleCount),
            typeof(int),
            typeof(CardDeckPresenter),
            new PropertyMetadata(5, OnLayoutSettingsChanged));

    public double DeckOffsetY
    {
        get => (double)GetValue(DeckOffsetYProperty);
        set => SetValue(DeckOffsetYProperty, value);
    }

    public static readonly DependencyProperty DeckOffsetYProperty =
        DependencyProperty.Register(
            nameof(DeckOffsetY),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(8d, OnLayoutSettingsChanged));

    public double DeckAngleStepDegrees
    {
        get => (double)GetValue(DeckAngleStepDegreesProperty);
        set => SetValue(DeckAngleStepDegreesProperty, value);
    }

    public static readonly DependencyProperty DeckAngleStepDegreesProperty =
        DependencyProperty.Register(
            nameof(DeckAngleStepDegrees),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(1.25d, OnLayoutSettingsChanged));

    public double DeckScaleStep
    {
        get => (double)GetValue(DeckScaleStepProperty);
        set => SetValue(DeckScaleStepProperty, value);
    }

    public static readonly DependencyProperty DeckScaleStepProperty =
        DependencyProperty.Register(
            nameof(DeckScaleStep),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(0.02d, OnLayoutSettingsChanged));

    public double DeckBaseZ
    {
        get => (double)GetValue(DeckBaseZProperty);
        set => SetValue(DeckBaseZProperty, value);
    }

    public static readonly DependencyProperty DeckBaseZProperty =
        DependencyProperty.Register(
            nameof(DeckBaseZ),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(8d, OnLayoutSettingsChanged));

    public double DeckZStep
    {
        get => (double)GetValue(DeckZStepProperty);
        set => SetValue(DeckZStepProperty, value);
    }

    public static readonly DependencyProperty DeckZStepProperty =
        DependencyProperty.Register(
            nameof(DeckZStep),
            typeof(double),
            typeof(CardDeckPresenter),
            new PropertyMetadata(6d, OnLayoutSettingsChanged));

    public int MorphDurationMilliseconds
    {
        get => (int)GetValue(MorphDurationMillisecondsProperty);
        set => SetValue(MorphDurationMillisecondsProperty, value);
    }

    public static readonly DependencyProperty MorphDurationMillisecondsProperty =
        DependencyProperty.Register(
            nameof(MorphDurationMilliseconds),
            typeof(int),
            typeof(CardDeckPresenter),
            new PropertyMetadata(420));

    private static void OnLayoutSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CardDeckPresenter presenter)
        {
            presenter.ApplySettingsToLayout();
            presenter.InvalidateMeasure();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        _isUnloaded = false;

        ApplySettingsToLayout();
        _lastSignature = ComputeSignature(ActualWidth);
        _lastItemCount = ItemsSourceView?.Count ?? 0;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _isLoaded = false;

        StopFrameTracking();
        StopAllMorphStoryboards();
        StopAllSwipeStoryboards();

        _pendingMorph = false;
        _pendingMorphUsesCardId = false;
        _snapshots.Clear();
        _lastPassSnapshots.Clear();
        _snapshotsByCardId.Clear();
        _lastPassSnapshotsByCardId.Clear();
        _realizedIndices.Clear();
        _swipingIndices.Clear();
        _elementToIndex.Clear();
        _cardIdToElement.Clear();
        _elementToCardId.Clear();
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        var element = args.Element;
        if (element is null)
        {
            return;
        }

        _elementToIndex[element] = args.Index;
        _realizedIndices.Add(args.Index);

        var data = (element as FrameworkElement)?.DataContext;
        if (TryExtractCardId(data) is long id)
        {
            _cardIdToElement[id] = element;
            _elementToCardId[element] = id;
        }

        // If an element appears during a morph (virtualization), fade it in subtly.
        if (_pendingMorph)
        {
            TryAnimateAppear(element);
        }
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        StopMorphStoryboard(args.Element);
        StopSwipeStoryboard(args.Element, resetToNeutral: false);

        if (_elementToCardId.TryGetValue(args.Element, out var cardId))
        {
            _elementToCardId.Remove(args.Element);
            if (_cardIdToElement.TryGetValue(cardId, out var current) && ReferenceEquals(current, args.Element))
            {
                _cardIdToElement.Remove(cardId);
            }
        }

        if (_elementToIndex.TryGetValue(args.Element, out var index))
        {
            _realizedIndices.Remove(index);
            _elementToIndex.Remove(args.Element);
        }
    }

    private void OnLayoutUpdated(object? sender, object e)
    {
        if (_isUnloaded || !_isLoaded)
        {
            return;
        }

        // Detect mode changes *after* layout has settled, using snapshots from the previous
        // layout pass. This avoids capturing snapshots too late (e.g., SizeChanged fires
        // after resize/layout, so deltas become ~0 and no animation is visible).
        var signature = ComputeSignature(ActualWidth);
        var itemCount = ItemsSourceView?.Count ?? 0;
        if (_lastSignature is null)
        {
            _lastSignature = signature;
            _lastItemCount = itemCount;
            CaptureCurrentLayoutSnapshots(_lastPassSnapshots);
            CaptureCurrentLayoutSnapshotsByCardId(_lastPassSnapshotsByCardId);
            return;
        }

        var countChanged = _lastItemCount != itemCount;
        if (!_pendingMorph && (_lastSignature.Value != signature || countChanged))
        {
            _lastSignature = signature;
            _lastItemCount = itemCount;
            CopySnapshots(_lastPassSnapshots, _snapshots);
            CopySnapshots(_lastPassSnapshotsByCardId, _snapshotsByCardId);
            _pendingMorphUsesCardId = countChanged && _snapshotsByCardId.Count > 0;
            _pendingMorph = _pendingMorphUsesCardId ? _snapshotsByCardId.Count > 0 : _snapshots.Count > 0;
        }

        if (_pendingMorph)
        {
            RunMorphAnimations();
        }

        CaptureCurrentLayoutSnapshots(_lastPassSnapshots);
        CaptureCurrentLayoutSnapshotsByCardId(_lastPassSnapshotsByCardId);
    }

    private void ApplySettingsToLayout()
    {
        _layout.Mode = Mode;
        _layout.ModeWidthThreshold = ModeWidthThreshold;
        _layout.CardMinWidth = CardMinWidth;
        _layout.CardHeight = CardHeight;
        _layout.Spacing = Spacing;
        _layout.MaxColumns = MaxColumns;
        _layout.DeckMaxVisibleCount = DeckMaxVisibleCount;
        _layout.DeckOffsetY = DeckOffsetY;
        _layout.DeckAngleStepDegrees = DeckAngleStepDegrees;
        _layout.DeckScaleStep = DeckScaleStep;
        _layout.DeckBaseZ = DeckBaseZ;
        _layout.DeckZStep = DeckZStep;
    }

    private CardDeckLayoutMode ResolveMode(double width)
    {
        if (Mode != CardDeckLayoutMode.Auto)
        {
            return Mode;
        }

        return width >= ModeWidthThreshold ? CardDeckLayoutMode.Grid : CardDeckLayoutMode.Deck;
    }

    private LayoutSignature ComputeSignature(double width)
    {
        var resolvedMode = ResolveMode(width);
        if (resolvedMode == CardDeckLayoutMode.Grid)
        {
            return new LayoutSignature(resolvedMode, ComputeColumnCount(width));
        }

        return new LayoutSignature(resolvedMode, GridColumns: 0);
    }

    private int ComputeColumnCount(double width)
    {
        if (width <= 0)
        {
            return 1;
        }

        var cardWidth = Math.Max(1, CardMinWidth);
        var spacing = Math.Max(0, Spacing);
        var maxColumns = Math.Max(1, MaxColumns);

        var denom = cardWidth + spacing;
        if (denom <= 0)
        {
            return 1;
        }

        var columns = (int)Math.Floor((width + spacing) / denom);
        if (columns < 1)
        {
            columns = 1;
        }

        if (columns > maxColumns)
        {
            columns = maxColumns;
        }

        return columns;
    }

    private static void CopySnapshots(Dictionary<int, Snapshot> source, Dictionary<int, Snapshot> destination)
    {
        destination.Clear();
        foreach (var pair in source)
        {
            destination[pair.Key] = pair.Value;
        }
    }

    private static void CopySnapshots(Dictionary<long, Snapshot> source, Dictionary<long, Snapshot> destination)
    {
        destination.Clear();
        foreach (var pair in source)
        {
            destination[pair.Key] = pair.Value;
        }
    }

    private void CaptureCurrentLayoutSnapshots(Dictionary<int, Snapshot> destination)
    {
        destination.Clear();

        foreach (var index in _realizedIndices)
        {
            var element = TryGetElement(index);
            if (element is null)
            {
                continue;
            }

            var bounds = GetBoundsRelativeToPresenter(element);
            var (rotation, scale) = GetLayoutTransform(element);
            destination[index] = new Snapshot(bounds, rotation, scale, element.Translation.Z);
        }
    }

    private void CaptureCurrentLayoutSnapshotsByCardId(Dictionary<long, Snapshot> destination)
    {
        destination.Clear();

        foreach (var (cardId, element) in _cardIdToElement)
        {
            if (element is null)
            {
                continue;
            }

            var bounds = GetBoundsRelativeToPresenter(element);
            var (rotation, scale) = GetLayoutTransform(element);
            destination[cardId] = new Snapshot(bounds, rotation, scale, element.Translation.Z);
        }
    }

    private void RunMorphAnimations()
    {
        if (!_pendingMorphUsesCardId && _snapshots.Count == 0)
        {
            _pendingMorph = false;
            return;
        }

        if (_pendingMorphUsesCardId && _snapshotsByCardId.Count == 0)
        {
            _pendingMorph = false;
            _pendingMorphUsesCardId = false;
            return;
        }

        var durationMs = Math.Max(0, MorphDurationMilliseconds);
        StartFrameTracking(durationMs);

        if (_pendingMorphUsesCardId)
        {
            foreach (var (cardId, snapshot) in _snapshotsByCardId)
            {
                if (!_cardIdToElement.TryGetValue(cardId, out var element) || element is null)
                {
                    continue;
                }

                if (_activeSwipeStoryboards.ContainsKey(element))
                {
                    continue;
                }

                var newBounds = GetBoundsRelativeToPresenter(element);
                var (newRotation, newScale) = GetLayoutTransform(element);
                var newBaseZ = CardDeckElevation.GetBaseZ(element);

                var deltaX = snapshot.Bounds.X - newBounds.X;
                var deltaY = snapshot.Bounds.Y - newBounds.Y;

                var deltaRotation = snapshot.RotationDegrees - newRotation;

                var scaleRatio = 1.0;
                if (newScale is > 0 and < double.PositiveInfinity)
                {
                    scaleRatio = snapshot.Scale / newScale;
                }

                TryAnimateMorph(element, deltaX, deltaY, deltaRotation, scaleRatio, snapshot.Z, newBaseZ, durationMs);
            }

            _snapshotsByCardId.Clear();
        }
        else
        {
            foreach (var (index, snapshot) in _snapshots)
            {
                if (_swipingIndices.Contains(index))
                {
                    continue;
                }

                var element = TryGetElement(index);
                if (element is null)
                {
                    continue;
                }

                var newBounds = GetBoundsRelativeToPresenter(element);
                var (newRotation, newScale) = GetLayoutTransform(element);
                var newBaseZ = CardDeckElevation.GetBaseZ(element);

                var deltaX = snapshot.Bounds.X - newBounds.X;
                var deltaY = snapshot.Bounds.Y - newBounds.Y;

                var deltaRotation = snapshot.RotationDegrees - newRotation;

                var scaleRatio = 1.0;
                if (newScale is > 0 and < double.PositiveInfinity)
                {
                    scaleRatio = snapshot.Scale / newScale;
                }

                TryAnimateMorph(element, deltaX, deltaY, deltaRotation, scaleRatio, snapshot.Z, newBaseZ, durationMs);
            }

            _snapshots.Clear();
        }

        _pendingMorph = false;
        _pendingMorphUsesCardId = false;
    }

    private void StartFrameTracking(int durationMs)
    {
        // During rapid resizing, the layout can change again while a morph is still in-flight.
        // Keep refreshing "last pass" snapshots from the actual on-screen (animated) positions,
        // so subsequent morphs start from the current state and remain continuous.
        var now = DateTimeOffset.UtcNow;
        var until = now + TimeSpan.FromMilliseconds(Math.Max(1, durationMs) + 120);
        if (until > _frameTrackingUntilUtc)
        {
            _frameTrackingUntilUtc = until;
        }

        if (_isFrameTrackingEnabled)
        {
            return;
        }

        _isFrameTrackingEnabled = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopFrameTracking()
    {
        if (!_isFrameTrackingEnabled)
        {
            return;
        }

        _isFrameTrackingEnabled = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        if (_isUnloaded || !_isLoaded)
        {
            StopFrameTracking();
            return;
        }

        if (DateTimeOffset.UtcNow > _frameTrackingUntilUtc)
        {
            StopFrameTracking();
            return;
        }

        TickZAnimations();

        CaptureCurrentLayoutSnapshots(_lastPassSnapshots);
        CaptureCurrentLayoutSnapshotsByCardId(_lastPassSnapshotsByCardId);
    }

    private void TickZAnimations()
    {
        if (_activeZAnimations.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elements = _activeZAnimations.Keys.ToArray();

        foreach (var element in elements)
        {
            if (element is null)
            {
                _activeZAnimations.Remove(element!);
                continue;
            }

            if (!_activeZAnimations.TryGetValue(element, out var state))
            {
                continue;
            }

            var durationMs = Math.Max(1, state.DurationMs);
            var t = (now - state.StartedUtc).TotalMilliseconds / durationMs;
            if (!double.IsFinite(t))
            {
                t = 1;
            }

            if (t <= 0)
            {
                CardDeckElevation.SetAnimationOffsetZ(element, state.FromOffsetZ);
                continue;
            }

            if (t >= 1)
            {
                CardDeckElevation.SetAnimationOffsetZ(element, 0);
                _activeZAnimations.Remove(element);
                continue;
            }

            // Cubic ease-out: 1 - (1 - t)^3
            var eased = 1 - Math.Pow(1 - t, 3);
            var currentOffset = state.FromOffsetZ * (1 - eased);
            CardDeckElevation.SetAnimationOffsetZ(element, currentOffset);
        }
    }

    private void TryAnimateMorph(UIElement element, double deltaX, double deltaY, double deltaRotation, double scaleRatio, double snapshotZ, double newBaseZ, int durationMs)
    {
        if (element is null)
        {
            return;
        }

        // Skip tiny/no-op changes.
        var shouldAnimateZ = double.IsFinite(newBaseZ) && Math.Abs(snapshotZ - newBaseZ) >= 0.5;

        if (Math.Abs(deltaX) < 0.5
            && Math.Abs(deltaY) < 0.5
            && Math.Abs(deltaRotation) < 0.2
            && (!double.IsFinite(scaleRatio) || Math.Abs(scaleRatio - 1.0) < 0.01)
            && !shouldAnimateZ)
        {
            return;
        }

        StopMorphStoryboard(element);

        // Ensure we have a dedicated animation CompositeTransform that the layout won't overwrite.
        var (_, animationTransform) = CardDeckLayout.EnsureTransforms(element);

        // Seed the additive transform to represent “old → new” delta, then animate back to neutral.
        animationTransform.TranslateX = deltaX;
        animationTransform.TranslateY = deltaY;
        animationTransform.Rotation = deltaRotation;

        if (double.IsFinite(scaleRatio) && scaleRatio > 0)
        {
            animationTransform.ScaleX = scaleRatio;
            animationTransform.ScaleY = scaleRatio;
        }
        else
        {
            animationTransform.ScaleX = 1;
            animationTransform.ScaleY = 1;
        }

        var duration = TimeSpan.FromMilliseconds(Math.Max(1, durationMs));
        var moveEase = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        var rotateEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        var storyboard = new Storyboard();

        var xAnim = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = moveEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(xAnim, animationTransform);
        Storyboard.SetTargetProperty(xAnim, nameof(CompositeTransform.TranslateX));
        storyboard.Children.Add(xAnim);

        var yAnim = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = moveEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(yAnim, animationTransform);
        Storyboard.SetTargetProperty(yAnim, nameof(CompositeTransform.TranslateY));
        storyboard.Children.Add(yAnim);

        var rAnim = new DoubleAnimation { To = 0, Duration = duration, EasingFunction = rotateEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(rAnim, animationTransform);
        Storyboard.SetTargetProperty(rAnim, nameof(CompositeTransform.Rotation));
        storyboard.Children.Add(rAnim);

        var sxAnim = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = moveEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(sxAnim, animationTransform);
        Storyboard.SetTargetProperty(sxAnim, nameof(CompositeTransform.ScaleX));
        storyboard.Children.Add(sxAnim);

        var syAnim = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = moveEase, EnableDependentAnimation = true };
        Storyboard.SetTarget(syAnim, animationTransform);
        Storyboard.SetTargetProperty(syAnim, nameof(CompositeTransform.ScaleY));
        storyboard.Children.Add(syAnim);

        if (shouldAnimateZ)
        {
            // Layout has already applied the new base Z. Animate an additive offset back to 0.
            var fromOffsetZ = snapshotZ - newBaseZ;
            CardDeckElevation.SetAnimationOffsetZ(element, fromOffsetZ);
            _activeZAnimations[element] = new ZAnimationState(fromOffsetZ, DateTimeOffset.UtcNow, Math.Max(1, durationMs));
        }

        _activeMorphStoryboards[element] = storyboard;
        storyboard.Completed += (_, _) => OnMorphStoryboardCompleted(element, storyboard);
        storyboard.Begin();
    }

    private void OnMorphStoryboardCompleted(UIElement element, Storyboard completed)
    {
        if (element is null)
        {
            return;
        }

        // IMPORTANT (WinAppSDK): calling Stop() on completion can revert the animated
        // properties back to their pre-animation values, which looks like a snap-back.
        // Only detach tracking when the *current* storyboard completes.
        if (_activeMorphStoryboards.TryGetValue(element, out var current) && ReferenceEquals(current, completed))
        {
            _activeMorphStoryboards.Remove(element);
        }
    }

    private void StopMorphStoryboard(UIElement element)
    {
        if (element is null)
        {
            return;
        }

        if (_activeMorphStoryboards.TryGetValue(element, out var active))
        {
            _activeMorphStoryboards.Remove(element);
            try
            {
                active.Stop();
            }
            catch
            {
                // Ignore stop failures during teardown/resizing.
            }

            // Ensure we don't leave a cancelled animation transform in a non-neutral state.
            var (_, animationTransform) = CardDeckLayout.EnsureTransforms(element);
            animationTransform.TranslateX = 0;
            animationTransform.TranslateY = 0;
            animationTransform.Rotation = 0;
            animationTransform.ScaleX = 1;
            animationTransform.ScaleY = 1;

            CardDeckElevation.SetAnimationOffsetZ(element, 0);
            _activeZAnimations.Remove(element);
        }
    }

    private void StopAllMorphStoryboards()
    {
        foreach (var element in _activeZAnimations.Keys)
        {
            if (element is not null)
            {
                CardDeckElevation.SetAnimationOffsetZ(element, 0);
            }
        }

        _activeZAnimations.Clear();

        if (_activeMorphStoryboards.Count == 0)
        {
            return;
        }

        foreach (var storyboard in _activeMorphStoryboards.Values)
        {
            try
            {
                storyboard.Stop();
            }
            catch
            {
                // Ignore.
            }
        }

        _activeMorphStoryboards.Clear();
    }

    private void StopSwipeStoryboard(UIElement element, bool resetToNeutral)
    {
        if (element is null)
        {
            return;
        }

        if (_activeSwipeStoryboards.TryGetValue(element, out var active))
        {
            _activeSwipeStoryboards.Remove(element);
            try
            {
                active.Stop();
            }
            catch
            {
                // Ignore.
            }
        }

        if (resetToNeutral)
        {
            var (_, animationTransform) = CardDeckLayout.EnsureTransforms(element);
            animationTransform.TranslateX = 0;
            animationTransform.TranslateY = 0;
            animationTransform.Rotation = 0;
            animationTransform.ScaleX = 1;
            animationTransform.ScaleY = 1;

            element.Opacity = 1;
            element.IsHitTestVisible = true;
        }
    }

    private void StopAllSwipeStoryboards()
    {
        if (_activeSwipeStoryboards.Count == 0)
        {
            return;
        }

        foreach (var storyboard in _activeSwipeStoryboards.Values)
        {
            try
            {
                storyboard.Stop();
            }
            catch
            {
                // Ignore.
            }
        }

        _activeSwipeStoryboards.Clear();
    }

    private int? TryFindIndexByCardId(long cardId)
    {
        var view = ItemsSourceView;
        if (view is null)
        {
            return null;
        }

        for (var i = 0; i < view.Count; i++)
        {
            var item = view.GetAt(i);
            if (TryExtractCardId(item) is long id && id == cardId)
            {
                return i;
            }
        }

        return null;
    }

    private static long? TryExtractCardId(object? item)
    {
        if (item is null)
        {
            return null;
        }

        // Keep the presenter generic: treat "CardId" as a conventional key.
        var type = item.GetType();
        var prop = type.GetProperty("CardId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is null)
        {
            return null;
        }

        try
        {
            var value = prop.GetValue(item);
            return value switch
            {
                long l => l,
                int i => i,
                short s => s,
                byte b => b,
                string str when long.TryParse(str, out var parsed) => parsed,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private void TryAnimateAppear(UIElement element)
    {
        if (element is null)
        {
            return;
        }

        element.Opacity = 0;
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var storyboard = new Storyboard();
        var opacityAnim = new DoubleAnimation { To = 1, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(opacityAnim, element);
        Storyboard.SetTargetProperty(opacityAnim, nameof(UIElement.Opacity));
        storyboard.Children.Add(opacityAnim);
        storyboard.Begin();
    }

    private Rect GetBoundsRelativeToPresenter(UIElement element)
    {
        try
        {
            var transform = element.TransformToVisual(this);
            return transform.TransformBounds(new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height));
        }
        catch
        {
            return new Rect(0, 0, 0, 0);
        }
    }

    private static (double RotationDegrees, double Scale) GetLayoutTransform(UIElement element)
    {
        if (element.RenderTransform is TransformGroup group
            && group.Children.Count >= 1
            && group.Children[0] is CompositeTransform layout)
        {
            // Layout uses uniform scale.
            return (layout.Rotation, layout.ScaleX);
        }

        if (element.RenderTransform is CompositeTransform ct)
        {
            // Layout uses uniform scale.
            return (ct.Rotation, ct.ScaleX);
        }

        return (0d, 1d);
    }

    private readonly record struct LayoutSignature(CardDeckLayoutMode Mode, int GridColumns);

    private readonly record struct Snapshot(Rect Bounds, double RotationDegrees, double Scale, double Z);
}
