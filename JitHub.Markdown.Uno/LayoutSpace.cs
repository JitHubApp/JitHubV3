using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace JitHub.Markdown.Uno;

internal static class LayoutSpace
{
    /// <summary>
    /// Converts a pointer position into markdown layout coordinates (DIPs).
    ///
    /// The rendered bitmap is displayed in an Image whose Y is offset by the current viewport top.
    /// `e.GetCurrentPoint(image).Position` is viewport-local; hit-testing must use layout coordinates.
    /// </summary>
    public static Windows.Foundation.Point GetLayoutPoint(PointerRoutedEventArgs e, Image image, UIElement fallback, double viewportTop, out Windows.Foundation.Point? pointerPositionInImage)
    {
        try
        {
            var p = e.GetCurrentPoint(image).Position;
            pointerPositionInImage = p;
            return new Windows.Foundation.Point(p.X, p.Y + viewportTop);
        }
        catch
        {
            pointerPositionInImage = null;
            return e.GetCurrentPoint(fallback).Position;
        }
    }
}
