using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JitHub.Markdown.Uno;

/// <summary>
/// Phase 0 placeholder control.
/// The Skia-backed renderer and selection system are implemented in later phases.
/// </summary>
public sealed partial class MarkdownView : ContentControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownView),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownView()
    {
        var title = new TextBlock
        {
            Text = "MarkdownView (Phase 0 placeholder)",
            TextWrapping = TextWrapping.Wrap
        };

        _markdownText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = string.Empty
        };

        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                title,
                _markdownText
            }
        };

        UpdateText();
    }

    private readonly TextBlock _markdownText;

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).UpdateText();
    }

    private void UpdateText()
    {
        _markdownText.Text = Markdown;
    }
}
