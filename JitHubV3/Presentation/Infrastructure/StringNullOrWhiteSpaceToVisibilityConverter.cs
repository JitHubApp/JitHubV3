using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace JitHubV3.Presentation;

public sealed class StringNullOrWhiteSpaceToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasValue = value is string s && !string.IsNullOrWhiteSpace(s);
        if (Invert)
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
