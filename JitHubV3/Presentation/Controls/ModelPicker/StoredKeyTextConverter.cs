using System;
using Microsoft.UI.Xaml.Data;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed class StoredKeyTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return b ? "API key: stored" : "API key: not set";
        }

        return "API key: not set";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
