using System.Numerics;
using Microsoft.UI.Xaml;

namespace JitHubV3.Presentation.Infrastructure;

public static class DashboardElevation
{
    public static double GetZ(DependencyObject obj)
        => (double)obj.GetValue(ZProperty);

    public static void SetZ(DependencyObject obj, double value)
        => obj.SetValue(ZProperty, value);

    public static readonly DependencyProperty ZProperty =
        DependencyProperty.RegisterAttached(
            "Z",
            typeof(double),
            typeof(DashboardElevation),
            new PropertyMetadata(0d, OnZChanged));

    private static void OnZChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        var z = e.NewValue is double value && double.IsFinite(value) ? value : 0d;

        // Use Translation.Z so ThemeShadow depth is consistent cross-platform.
        element.Translation = new Vector3(0, 0, (float)z);
    }
}
