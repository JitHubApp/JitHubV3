namespace JitHub.Markdown;

public static class MarkdownThemeEngine
{
    public static MarkdownTheme Resolve(
        MarkdownThemeVariant variant,
        MarkdownThemeOverrides? overrides = null,
        Uri? imageBaseUri = null)
    {
        var baseTheme = variant switch
        {
            MarkdownThemeVariant.Light => MarkdownTheme.Light,
            MarkdownThemeVariant.Dark => MarkdownTheme.Dark,
            MarkdownThemeVariant.HighContrast => MarkdownTheme.HighContrast,
            _ => MarkdownTheme.Light,
        };

        var theme = MarkdownThemeOverrides.Apply(baseTheme, overrides);
        if (imageBaseUri is not null)
        {
            theme = theme.With(imageBaseUri: imageBaseUri);
        }

        // Phase 7.4: keep selection overlays readable even after customization.
        return EnsureReadableSelectionOverlay(theme);
    }

    private static MarkdownTheme EnsureReadableSelectionOverlay(MarkdownTheme theme)
    {
        // SelectionFill is an overlay over the page background. Validate readability against the blended result.
        var blendedFill = theme.Selection.SelectionFill.BlendOver(theme.Colors.PageBackground);
        var contrast = MarkdownContrast.ContrastRatio(theme.Selection.SelectionText, blendedFill);

        // 3.0 is the WCAG minimum for large text; selection labels tend to be larger and bold in HC.
        // If a consumer customizes things into an unreadable state, we repair by choosing the best of black/white.
        if (contrast >= 3.0)
        {
            return theme;
        }

        var black = ColorRgba.Black;
        var white = ColorRgba.White;
        var blackContrast = MarkdownContrast.ContrastRatio(black, blendedFill);
        var whiteContrast = MarkdownContrast.ContrastRatio(white, blendedFill);
        var best = blackContrast >= whiteContrast ? black : white;

        if (best.Equals(theme.Selection.SelectionText))
        {
            return theme;
        }

        return theme.With(selection: new MarkdownSelectionTheme
        {
            SelectionFill = theme.Selection.SelectionFill,
            SelectionText = best,
        });
    }
}
