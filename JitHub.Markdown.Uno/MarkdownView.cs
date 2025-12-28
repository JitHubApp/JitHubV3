using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
namespace JitHub.Markdown.Uno;

/// <summary>
/// Code-only wrapper used for Core MSBuild builds (dotnet build).
///
/// When building with MSBuild.exe (Full), the XAML-backed implementation in Xaml/ is used instead.
/// </summary>
public sealed partial class MarkdownView : UserControl
{
	private readonly Grid _root;
	private readonly SkiaMarkdownView _host;

	public MarkdownView()
	{
		_root = new Grid();
		Content = _root;

		_host = new SkiaMarkdownView
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Top,
		};

		_root.Children.Add(_host);

		// Move focus away from any editor above when user interacts with markdown.
		// Use handledEventsToo so this still runs when a parent ScrollViewer handles panning/manipulation.
		AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, __) => _host.Focus(FocusState.Pointer)), true);

		SyncAll();
	}
}
