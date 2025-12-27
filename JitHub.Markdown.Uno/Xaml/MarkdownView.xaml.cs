using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Globalization;

namespace JitHub.Markdown.Uno;

public sealed partial class MarkdownView : UserControl
{
    private readonly SkiaMarkdownView _host;

    public static readonly DependencyProperty IsRightToLeftProperty = DependencyProperty.Register(
        nameof(IsRightToLeft),
        typeof(bool),
        typeof(MarkdownView),
        new PropertyMetadata(GetPlatformIsRtl(), OnIsRightToLeftChanged));

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownView),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme),
        typeof(MarkdownTheme),
        typeof(MarkdownView),
        new PropertyMetadata(MarkdownTheme.Light, OnThemeChanged));

    public static readonly DependencyProperty ImageBaseUriProperty = DependencyProperty.Register(
        nameof(ImageBaseUri),
        typeof(Uri),
        typeof(MarkdownView),
        new PropertyMetadata(null, OnThemeChanged));

    public static readonly DependencyProperty SelectionEnabledProperty = DependencyProperty.Register(
        nameof(SelectionEnabled),
        typeof(bool),
        typeof(MarkdownView),
        new PropertyMetadata(true, OnSelectionEnabledChanged));

    public bool IsRightToLeft
    {
        get => (bool)GetValue(IsRightToLeftProperty);
        set => SetValue(IsRightToLeftProperty, value);
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownTheme Theme
    {
        get => (MarkdownTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public Uri? ImageBaseUri
    {
        get => (Uri?)GetValue(ImageBaseUriProperty);
        set => SetValue(ImageBaseUriProperty, value);
    }

    public bool SelectionEnabled
    {
        get => (bool)GetValue(SelectionEnabledProperty);
        set => SetValue(SelectionEnabledProperty, value);
    }

    public SelectionRange? Selection
    {
        get => _host.Selection;
        set => _host.Selection = value;
    }

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

        SyncAll();
    }

    private static bool GetPlatformIsRtl()
        => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).SyncMarkdown();
    }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).SyncTheme();
    }

    private static void OnSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).SyncSelectionEnabled();
    }

    private static void OnIsRightToLeftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).SyncIsRightToLeft();
    }

    private void SyncAll()
    {
        SyncMarkdown();
        SyncTheme();
        SyncSelectionEnabled();
        SyncIsRightToLeft();
    }

    private void SyncMarkdown()
    {
        _host.Markdown = Markdown;
    }

    private void SyncTheme()
    {
        _host.Theme = Theme;
        _host.ImageBaseUri = ImageBaseUri;
    }

    private void SyncSelectionEnabled()
    {
        _host.SelectionEnabled = SelectionEnabled;
    }

    private void SyncIsRightToLeft()
    {
        _host.IsRightToLeft = IsRightToLeft;
    }

    public Task CopySelectionToClipboardAsync(bool includePlainText = true)
        => _host.CopySelectionToClipboardAsync(includePlainText);
}
