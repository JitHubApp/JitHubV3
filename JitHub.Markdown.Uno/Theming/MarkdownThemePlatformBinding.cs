using Microsoft.UI.Xaml;
using JitHub.Markdown;

namespace JitHub.Markdown.Uno;

/// <summary>
/// Convenience binder that applies platform light/dark/high-contrast to a markdown control using
/// the core MarkdownThemeEngine. This stays outside the control itself.
/// </summary>
public static class MarkdownThemePlatformBinding
{
    public static IDisposable Bind(
        MarkdownView view,
        FrameworkElement themeSource,
        MarkdownThemeOverrides? lightOverrides = null,
        MarkdownThemeOverrides? darkOverrides = null,
        MarkdownThemeOverrides? highContrastOverrides = null)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (themeSource is null) throw new ArgumentNullException(nameof(themeSource));

        var watcher = new MarkdownPlatformThemeWatcher(themeSource);

        void Apply(MarkdownThemeVariant variant)
        {
            var overrides = variant switch
            {
                MarkdownThemeVariant.Dark => darkOverrides,
                MarkdownThemeVariant.HighContrast => highContrastOverrides,
                _ => lightOverrides,
            };

            view.Theme = MarkdownThemeEngine.Resolve(variant, overrides: overrides);
        }

        Apply(watcher.CurrentVariant);
        watcher.VariantChanged += (_, v) => Apply(v);

        return watcher;
    }
}
