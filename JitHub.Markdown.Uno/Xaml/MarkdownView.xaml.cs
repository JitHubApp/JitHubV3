using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace JitHub.Markdown.Uno;

public sealed partial class MarkdownView : UserControl
{
    private readonly SkiaMarkdownView _host;

    public MarkdownView()
    {
        _host = new SkiaMarkdownView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };

        InitializeComponent();

        if (Root is not null)
        {
            Root.Children.Add(_host);
        }

        // Move focus away from any editor above when user interacts with markdown.
        // Use handledEventsToo so this still runs when a parent ScrollViewer handles panning/manipulation.
        AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, __) => _host.Focus(FocusState.Pointer)), true);

        InitializeAutoThemeSupport();

        SyncAll();
    }
}
