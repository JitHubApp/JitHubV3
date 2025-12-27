using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.Logging;
using Uno.Extensions;
using Microsoft.UI.Xaml.Media;
using System.Linq;

namespace JitHubV3.Presentation;

public sealed partial class MarkdownTestPage : Page
{
    public MarkdownTestPage()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Per Uno focus management guidance: explicitly set initial focus away from TextBox.
        _ = MarkdownPreview?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

#if DEBUG
        // Probe pointer routing at the page level. If this doesn't fire, something above the page
        // is consuming/never delivering pointer events.
        AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            var pt = args.GetCurrentPoint(this).Position;
            var original = args.OriginalSource as DependencyObject;
            var originalName = (original as FrameworkElement)?.Name;
            var originalType = original?.GetType().FullName ?? "<null>";
            var isInPreview = MarkdownPreview is not null && IsInSubtree(original, MarkdownPreview);

            if (MarkdownPreview is not null)
            {
                try
                {
                    var t = MarkdownPreview.TransformToVisual(this);
                    var tl = t.TransformPoint(new Windows.Foundation.Point(0, 0));
                    var w = MarkdownPreview.ActualWidth;
                    var h = MarkdownPreview.ActualHeight;
                    var inside = pt.X >= tl.X && pt.X <= (tl.X + w) && pt.Y >= tl.Y && pt.Y <= (tl.Y + h);

                    string? topHit = null;
                    try
                    {
                        var hits = VisualTreeHelper.FindElementsInHostCoordinates(pt, this);
                        var top = hits.FirstOrDefault();
                        if (top is not null)
                        {
                            topHit = $"{top.GetType().FullName}#{(top as FrameworkElement)?.Name}";
                        }
                    }
                    catch
                    {
                        // Ignore hit-test failures.
                    }

                    this.Log().LogWarning(
                        "[MarkdownTestPage] Page PointerPressed ({X},{Y}) handled={Handled} | Original={OriginalType}#{OriginalName} inPreviewRoute={InPreviewRoute} | TopHit={TopHit} | Preview bounds ({L},{T}) {W}x{H} inside={Inside}",
                        pt.X, pt.Y, args.Handled,
                        originalType, originalName ?? "",
                        isInPreview,
                        topHit ?? "<unknown>",
                        tl.X, tl.Y, w, h, inside);
                    return;
                }
                catch
                {
                    // ignore and fall back
                }
            }

            this.Log().LogWarning("[MarkdownTestPage] Page PointerPressed ({X},{Y}) handled={Handled} | Preview <null>", pt.X, pt.Y, args.Handled);
        }), true);
#endif

        // Pointer probe: confirms whether pointer events reach the markdown view subtree.
        if (MarkdownPreview is not null)
        {
            // Force managed bubbling for this subtree (Uno routed-events docs).
            TryEnableManagedPointerBubbling(MarkdownPreview);

            MarkdownPreview.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, args) =>
            {
                var pt = args.GetCurrentPoint(MarkdownPreview).Position;
                this.Log().LogWarning("[MarkdownTestPage] Preview PointerPressed ({X},{Y}) handled={Handled}", pt.X, pt.Y, args.Handled);
            }), true);
        }
    }

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

    private static bool IsInSubtree(DependencyObject? node, DependencyObject ancestor)
    {
        try
        {
            var cur = node;
            while (cur is not null)
            {
                if (ReferenceEquals(cur, ancestor))
                {
                    return true;
                }

                cur = VisualTreeHelper.GetParent(cur);
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }
}
