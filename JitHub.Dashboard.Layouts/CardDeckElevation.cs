using System.Numerics;
using Microsoft.UI.Xaml;

namespace JitHub.Dashboard.Layouts;

public static class CardDeckElevation
{
    public static double GetBaseZ(DependencyObject obj)
        => ReadDoubleOr(obj, BaseZProperty, fallback: double.NaN);

    public static void SetBaseZ(DependencyObject obj, double value)
        => obj.SetValue(BaseZProperty, value);

    public static void ClearBaseZ(DependencyObject obj)
        => obj.ClearValue(BaseZProperty);

    public static readonly DependencyProperty BaseZProperty =
        DependencyProperty.RegisterAttached(
            "BaseZ",
            typeof(double),
            typeof(CardDeckElevation),
            new PropertyMetadata(double.NaN, OnZChanged));

    public static double GetAnimationOffsetZ(DependencyObject obj)
        => ReadDoubleOr(obj, AnimationOffsetZProperty, fallback: 0d);

    public static void SetAnimationOffsetZ(DependencyObject obj, double value)
        => obj.SetValue(AnimationOffsetZProperty, value);

    public static void ClearAnimationOffsetZ(DependencyObject obj)
        => obj.ClearValue(AnimationOffsetZProperty);

    public static readonly DependencyProperty AnimationOffsetZProperty =
        DependencyProperty.RegisterAttached(
            "AnimationOffsetZ",
            typeof(double),
            typeof(CardDeckElevation),
            new PropertyMetadata(0d, OnZChanged));

    private static void OnZChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        var baseZ = ReadDoubleOr(element, BaseZProperty, fallback: double.NaN);
        if (!double.IsFinite(baseZ))
        {
            return;
        }

        var offsetZ = ReadDoubleOr(element, AnimationOffsetZProperty, fallback: 0d);
        if (!double.IsFinite(offsetZ))
        {
            offsetZ = 0;
        }

        var targetZ = baseZ + offsetZ;
        if (!double.IsFinite(targetZ))
        {
            targetZ = 0;
        }

        var t = element.Translation;
        element.Translation = new Vector3(t.X, t.Y, (float)targetZ);
    }

    private static double ReadDoubleOr(DependencyObject obj, DependencyProperty property, double fallback)
    {
        try
        {
            return obj.GetValue(property) switch
            {
                double d when double.IsFinite(d) => d,
                float f when float.IsFinite(f) => f,
                int i => i,
                _ => fallback,
            };
        }
        catch
        {
            return fallback;
        }
    }
}
