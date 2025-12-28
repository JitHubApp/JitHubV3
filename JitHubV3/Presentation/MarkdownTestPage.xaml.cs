using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.Logging;
using Uno.Extensions;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using System.Collections.Generic;

namespace JitHubV3.Presentation;

public sealed partial class MarkdownTestPage : Page
{
    private readonly Dictionary<uint, int> _moveCounts = new();
    private long _seq;

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
            LogPointer("Page", "PointerPressed", args, root: this);

            var pt = args.GetCurrentPoint(this).Position;
            var original = args.OriginalSource as DependencyObject;
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
                        "[MarkdownTestPage] Page HitTestSummary ({X},{Y}) inPreviewRoute={InPreviewRoute} | TopHit={TopHit} | Preview bounds ({L},{T}) {W}x{H} inside={Inside}",
                        pt.X, pt.Y,
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
        }), true);

        AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) =>
        {
            LogPointer("Page", "PointerReleased", args, root: this);
        }), true);

        AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, args) =>
        {
            LogPointer("Page", "PointerCanceled", args, root: this);
        }), true);

        AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, args) =>
        {
            LogPointer("Page", "PointerCaptureLost", args, root: this);
        }), true);

        AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) =>
        {
            if (!ShouldLogMove(args.Pointer.PointerId))
            {
                return;
            }

            LogPointer("Page", "PointerMoved", args, root: this);
        }), true);
#endif

        // Pointer probe: confirms whether pointer events reach the markdown view subtree.
        if (MarkdownPreview is not null)
        {
            MarkdownPreview.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, args) =>
            {
                OnPointerSequenceStart(args.Pointer.PointerId);
                LogPointer("Preview", "PointerPressed", args, root: MarkdownPreview);
            }), true);

            MarkdownPreview.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, args) =>
            {
                LogPointer("Preview", "PointerReleased", args, root: MarkdownPreview);
                OnPointerSequenceEnd(args.Pointer.PointerId);
            }), true);

            MarkdownPreview.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((s, args) =>
            {
                LogPointer("Preview", "PointerCanceled", args, root: MarkdownPreview);
                OnPointerSequenceEnd(args.Pointer.PointerId);
            }), true);

            MarkdownPreview.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((s, args) =>
            {
                LogPointer("Preview", "PointerCaptureLost", args, root: MarkdownPreview);
            }), true);

            MarkdownPreview.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((s, args) =>
            {
                if (!ShouldLogMove(args.Pointer.PointerId))
                {
                    return;
                }

                LogPointer("Preview", "PointerMoved", args, root: MarkdownPreview);
            }), true);
        }

#if DEBUG
        if (RtlToggle is not null)
        {
            AttachVerbosePointerLogging(RtlToggle, "Toggle");
        }

        if (MarkdownEditor is not null)
        {
            AttachVerbosePointerLogging(MarkdownEditor, "Editor");
        }
#endif
    }

#if DEBUG
    private void AttachVerbosePointerLogging(UIElement element, string name)
    {
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, a) =>
        {
            OnPointerSequenceStart(a.Pointer.PointerId);
            LogPointer(name, "PointerPressed", a, root: element);
        }), true);

        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, a) =>
        {
            LogPointer(name, "PointerReleased", a, root: element);
            OnPointerSequenceEnd(a.Pointer.PointerId);
        }), true);

        element.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((s, a) =>
        {
            LogPointer(name, "PointerCanceled", a, root: element);
            OnPointerSequenceEnd(a.Pointer.PointerId);
        }), true);

        element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((s, a) =>
        {
            LogPointer(name, "PointerCaptureLost", a, root: element);
        }), true);

        element.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((s, a) =>
        {
            if (!ShouldLogMove(a.Pointer.PointerId))
            {
                return;
            }
            LogPointer(name, "PointerMoved", a, root: element);
        }), true);
    }
#endif

    private void OnPointerSequenceStart(uint pointerId)
    {
        _moveCounts[pointerId] = 0;
    }

    private void OnPointerSequenceEnd(uint pointerId)
    {
        _moveCounts.Remove(pointerId);
    }

    private bool ShouldLogMove(uint pointerId)
    {
        if (!_moveCounts.TryGetValue(pointerId, out var c))
        {
            // If we never saw a press for this pointer (e.g., move only), don't spam.
            return false;
        }

        c++;
        _moveCounts[pointerId] = c;

        // First 12 moves, then every 30th.
        return c <= 12 || (c % 30 == 0);
    }

    private void LogPointer(string scope, string evt, PointerRoutedEventArgs args, UIElement root)
    {
        try
        {
            var seq = System.Threading.Interlocked.Increment(ref _seq);

            var cp = args.GetCurrentPoint(root);
            var pt = cp.Position;
            var props = cp.Properties;

            var original = args.OriginalSource as DependencyObject;
            var originalFe = original as FrameworkElement;

            string? topHit = null;
            try
            {
                var hits = VisualTreeHelper.FindElementsInHostCoordinates(pt, root);
                var top = hits.FirstOrDefault();
                if (top is not null)
                {
                    topHit = $"{top.GetType().FullName}#{(top as FrameworkElement)?.Name}";
                }
            }
            catch
            {
                // ignore
            }

            var updateKind = "<n/a>";
            try
            {
                updateKind = props.PointerUpdateKind.ToString();
            }
            catch
            {
                // ignore
            }

            this.Log().LogWarning(
                "[MarkdownTestPage] #{Seq} {Scope} {Event} ({X},{Y}) id={Id} device={Device} inContact={InContact} updateKind={UpdateKind} left={Left} right={Right} middle={Middle} x1={X1} x2={X2} wheel={Wheel} handled={Handled} mods={Mods} | Original={OriginalType}#{OriginalName} | TopHit={TopHit}",
                seq,
                scope,
                evt,
                pt.X, pt.Y,
                args.Pointer.PointerId,
                args.Pointer.PointerDeviceType.ToString(),
                cp.IsInContact,
                updateKind,
                props.IsLeftButtonPressed,
                props.IsRightButtonPressed,
                props.IsMiddleButtonPressed,
                props.IsXButton1Pressed,
                props.IsXButton2Pressed,
                props.MouseWheelDelta,
                args.Handled,
                args.KeyModifiers.ToString(),
                original?.GetType().FullName ?? "<null>",
                originalFe?.Name ?? "",
                topHit ?? "<unknown>");
        }
        catch (Exception ex)
        {
            this.Log().LogWarning(ex, "[MarkdownTestPage] Pointer log failure ({Scope}/{Event})", scope, evt);
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
