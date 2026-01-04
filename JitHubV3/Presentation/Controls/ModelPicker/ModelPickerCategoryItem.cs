namespace JitHubV3.Presentation.Controls.ModelPicker;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

public sealed partial record ModelPickerCategoryItem(
    string Id,
    string DisplayName,
    Symbol IconSymbol,
    Uri? IconUri)
{
    private SvgImageSource? _iconSvg;

    public bool HasIconUri => IconUri is not null;

    public bool HasNoIconUri => IconUri is null;

    public Visibility IconUriVisibility => IconUri is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility IconSymbolVisibility => IconUri is null ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? IconImageSource =>
        IconUri is null
            ? null
            : _iconSvg ??= new SvgImageSource { UriSource = IconUri };
}
