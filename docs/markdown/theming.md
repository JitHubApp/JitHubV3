# Theming

Theme data lives in Core so it can be shared across renderers and controls.

## Primary types

- `MarkdownTheme`: root theme object.
- `MarkdownThemePresets`: preset themes.
- `MarkdownThemeOverrides` (+ nested overrides): apply partial changes to an existing theme.

## Using a preset

```csharp
var theme = MarkdownThemePresets.DefaultLight;
```

## Applying overrides

```csharp
var theme = MarkdownThemePresets.DefaultLight;

var overridden = theme.WithOverrides(new MarkdownThemeOverrides
{
    // Keep overrides minimal; prefer modifying only what you need.
    // (Exact properties depend on MarkdownThemeOverrides implementation.)
});
```

## Platform binding (Uno)

The Uno layer includes helpers to bind the cross-platform theme to platform colors/metrics:

- `MarkdownThemePlatformBinding`
- `MarkdownPlatformThemeWatcher`

These are intended for app-level wiring so the view stays renderer-focused.
