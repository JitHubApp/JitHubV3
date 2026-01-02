using System.Linq;

namespace JitHubV3.Presentation;

public sealed class StatusBarComposer
{
    private readonly IReadOnlyList<IStatusBarExtension> _extensions;
    private readonly StatusBarViewModel _statusBar;

    public StatusBarComposer(IEnumerable<IStatusBarExtension> extensions, StatusBarViewModel statusBar)
    {
        _extensions = extensions?.ToArray() ?? throw new ArgumentNullException(nameof(extensions));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));

        foreach (var ext in _extensions)
        {
            ext.Changed += OnExtensionChanged;
        }

        Recompose();
    }

    private void OnExtensionChanged(object? sender, EventArgs e) => Recompose();

    private void Recompose()
    {
        var segments = _extensions
            .SelectMany(x => x.Segments)
            .Where(x => x.IsVisible)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        _statusBar.SetSegments(segments);
    }
}
