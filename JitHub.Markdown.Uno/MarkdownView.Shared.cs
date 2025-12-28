using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace JitHub.Markdown.Uno;

public sealed partial class MarkdownView : UserControl
{
	public static readonly DependencyProperty GitHubBaseUrlProperty = DependencyProperty.Register(
		nameof(GitHubBaseUrl),
		typeof(string),
		typeof(MarkdownView),
		new PropertyMetadata(string.Empty, OnGitHubEnrichmentsChanged));

	public static readonly DependencyProperty GitHubRepositorySlugProperty = DependencyProperty.Register(
		nameof(GitHubRepositorySlug),
		typeof(string),
		typeof(MarkdownView),
		new PropertyMetadata(string.Empty, OnGitHubEnrichmentsChanged));

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

	public string GitHubBaseUrl
	{
		get => (string)GetValue(GitHubBaseUrlProperty);
		set => SetValue(GitHubBaseUrlProperty, value);
	}

	public string GitHubRepositorySlug
	{
		get => (string)GetValue(GitHubRepositorySlugProperty);
		set => SetValue(GitHubRepositorySlugProperty, value);
	}

	public SelectionRange? Selection
	{
		get => _host.Selection;
		set => _host.Selection = value;
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

	private static void OnGitHubEnrichmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		((MarkdownView)d).SyncGitHubEnrichments();
	}

	private void SyncAll()
	{
		SyncMarkdown();
		SyncTheme();
		SyncSelectionEnabled();
		SyncIsRightToLeft();
		SyncGitHubEnrichments();
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

	private void SyncGitHubEnrichments()
	{
		_host.GitHubBaseUrl = GitHubBaseUrl;
		_host.GitHubRepositorySlug = GitHubRepositorySlug;
	}

	public Task CopySelectionToClipboardAsync(bool includePlainText = true)
		=> _host.CopySelectionToClipboardAsync(includePlainText);
}
