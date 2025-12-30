using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace JitHubV3.Presentation.Infrastructure;

public sealed class CardTintVariantToBrushConverter : IValueConverter
{
    private static readonly string[] _paletteKeys =
    [
        "DashboardCardTintBrushNeutral",
        "DashboardCardTintBrushAttention",
        "DashboardCardTintBrushSuccess",
        "DashboardCardTintBrushCaution",
        "DashboardCardTintBrushCritical",
    ];

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var variant = value as int?;
        if (value is int i)
        {
            variant = i;
        }

        if (variant is null)
        {
            return null;
        }

        var normalized = variant.Value;
        if (normalized < 0)
        {
            normalized = -normalized;
        }

        var index = normalized % _paletteKeys.Length;
        var key = _paletteKeys[index];

        if (Application.Current?.Resources is { } resources && resources.TryGetValue(key, out var resource))
        {
            return resource as Brush;
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
