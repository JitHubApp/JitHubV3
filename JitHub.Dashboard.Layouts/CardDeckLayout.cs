using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace JitHub.Dashboard.Layouts;

public sealed class CardDeckLayout : VirtualizingLayout
{
    public CardDeckLayoutMode Mode
    {
        get => (CardDeckLayoutMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode),
            typeof(CardDeckLayoutMode),
            typeof(CardDeckLayout),
            new PropertyMetadata(CardDeckLayoutMode.Auto, OnLayoutPropertyChanged));

    public double ModeWidthThreshold
    {
        get => (double)GetValue(ModeWidthThresholdProperty);
        set => SetValue(ModeWidthThresholdProperty, value);
    }

    public static readonly DependencyProperty ModeWidthThresholdProperty =
        DependencyProperty.Register(
            nameof(ModeWidthThreshold),
            typeof(double),
            typeof(CardDeckLayout),
            new PropertyMetadata(900d, OnLayoutPropertyChanged));

    public double CardMinWidth
    {
        get => (double)GetValue(CardMinWidthProperty);
        set => SetValue(CardMinWidthProperty, value);
    }

    public static readonly DependencyProperty CardMinWidthProperty =
        DependencyProperty.Register(
            nameof(CardMinWidth),
            typeof(double),
            typeof(CardDeckLayout),
            new PropertyMetadata(320d, OnLayoutPropertyChanged));

    public double CardHeight
    {
        get => (double)GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public static readonly DependencyProperty CardHeightProperty =
        DependencyProperty.Register(
            nameof(CardHeight),
            typeof(double),
            typeof(CardDeckLayout),
            new PropertyMetadata(180d, OnLayoutPropertyChanged));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(CardDeckLayout),
            new PropertyMetadata(16d, OnLayoutPropertyChanged));

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(
            nameof(MaxColumns),
            typeof(int),
            typeof(CardDeckLayout),
            new PropertyMetadata(3, OnLayoutPropertyChanged));

    public int DeckMaxVisibleCount
    {
        get => (int)GetValue(DeckMaxVisibleCountProperty);
        set => SetValue(DeckMaxVisibleCountProperty, value);
    }

    public static readonly DependencyProperty DeckMaxVisibleCountProperty =
        DependencyProperty.Register(
            nameof(DeckMaxVisibleCount),
            typeof(int),
            typeof(CardDeckLayout),
            new PropertyMetadata(5, OnLayoutPropertyChanged));

    public double DeckOffsetY
    {
        get => (double)GetValue(DeckOffsetYProperty);
        set => SetValue(DeckOffsetYProperty, value);
    }

    public static readonly DependencyProperty DeckOffsetYProperty =
        DependencyProperty.Register(
            nameof(DeckOffsetY),
            typeof(double),
            typeof(CardDeckLayout),
            new PropertyMetadata(8d, OnLayoutPropertyChanged));

    public double DeckAngleStepDegrees
    {
        get => (double)GetValue(DeckAngleStepDegreesProperty);
        set => SetValue(DeckAngleStepDegreesProperty, value);
    }

    public static readonly DependencyProperty DeckAngleStepDegreesProperty =
        DependencyProperty.Register(
            nameof(DeckAngleStepDegrees),
            typeof(double),
            typeof(CardDeckLayout),
            new PropertyMetadata(1.25d, OnLayoutPropertyChanged));

    public double DeckScaleStep
    {
        get => (double)GetValue(DeckScaleStepProperty);
        set => SetValue(DeckScaleStepProperty, value);
    }

    public static readonly DependencyProperty DeckScaleStepProperty =
        DependencyProperty.Register(
            nameof(DeckScaleStep),
            typeof(double),
            typeof(CardDeckLayout),
            new PropertyMetadata(0d, OnLayoutPropertyChanged));

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var items = CardDeckLayoutMath.Compute(
            new LayoutSize(availableSize.Width, availableSize.Height),
            context.ItemCount,
            GetSettings());

        var desiredWidth = availableSize.Width;
        var desiredHeight = 0d;

        var cardHeight = Math.Max(1, CardHeight);
        var spacing = Math.Max(0, Spacing);

        if (items.Count > 0)
        {
            if (ResolveMode(availableSize.Width) == CardDeckLayoutMode.Grid)
            {
                var columns = Math.Max(1, Math.Min(MaxColumns, items.Count));
                var rows = (int)Math.Ceiling(items.Count / (double)columns);
                desiredHeight = rows * cardHeight + Math.Max(0, rows - 1) * spacing;
            }
            else
            {
                var visibleDepth = Math.Min(items.Count, Math.Max(1, DeckMaxVisibleCount));
                desiredHeight = cardHeight + Math.Max(0, visibleDepth - 1) * Math.Max(0, DeckOffsetY);
            }
        }

        for (var i = 0; i < context.ItemCount; i++)
        {
            var element = context.GetOrCreateElementAt(i);
            element.Measure(new Size(Math.Max(0, CardMinWidth), Math.Max(0, CardHeight)));
        }

        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        var items = CardDeckLayoutMath.Compute(
            new LayoutSize(finalSize.Width, finalSize.Height),
            context.ItemCount,
            GetSettings());

        for (var i = 0; i < items.Count; i++)
        {
            var element = context.GetOrCreateElementAt(i);
            var item = items[i];
            element.Arrange(new Rect(item.Rect.X, item.Rect.Y, item.Rect.Width, item.Rect.Height));

            ApplyTransform(element, item.Transform);
        }

        return finalSize;
    }

    private CardDeckLayoutSettings GetSettings() => new(
        Mode: Mode,
        ModeWidthThreshold: ModeWidthThreshold,
        CardMinWidth: CardMinWidth,
        CardHeight: CardHeight,
        Spacing: Spacing,
        MaxColumns: MaxColumns,
        DeckMaxVisibleCount: DeckMaxVisibleCount,
        DeckOffsetY: DeckOffsetY,
        DeckAngleStepDegrees: DeckAngleStepDegrees,
        DeckScaleStep: DeckScaleStep);

    private CardDeckLayoutMode ResolveMode(double width)
    {
        if (Mode != CardDeckLayoutMode.Auto)
        {
            return Mode;
        }

        return width >= ModeWidthThreshold ? CardDeckLayoutMode.Grid : CardDeckLayoutMode.Deck;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CardDeckLayout layout)
        {
            layout.InvalidateMeasure();
        }
    }

    private static void ApplyTransform(UIElement element, LayoutTransform transform)
    {
        if (element is null)
        {
            return;
        }

        if (element.RenderTransform is not CompositeTransform ct)
        {
            ct = new CompositeTransform();
            element.RenderTransform = ct;
            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }

        ct.TranslateX = transform.TranslateX;
        ct.TranslateY = transform.TranslateY;
        ct.Rotation = transform.RotationDegrees;
        ct.ScaleX = transform.Scale;
        ct.ScaleY = transform.Scale;
    }
}
