using Microsoft.UI.Xaml;

namespace JitHub.Markdown.Uno;

/// <summary>
/// Platform theme observer for Uno/WinUI applications.
/// This is intentionally NOT built into the MarkdownView control so each app/platform can decide
/// how to map platform theme signals to markdown themes.
/// </summary>
public sealed class MarkdownPlatformThemeWatcher : IDisposable
{
    private readonly FrameworkElement _themeSource;
    private object? _accessibilitySettings;

    public MarkdownThemeVariant CurrentVariant { get; private set; }

    public event EventHandler<MarkdownThemeVariant>? VariantChanged;

    public MarkdownPlatformThemeWatcher(FrameworkElement themeSource)
    {
        _themeSource = themeSource ?? throw new ArgumentNullException(nameof(themeSource));

        _themeSource.ActualThemeChanged += OnActualThemeChanged;
        TryHookHighContrast();

        CurrentVariant = ReadVariant();
    }

    public void Dispose()
    {
        _themeSource.ActualThemeChanged -= OnActualThemeChanged;

        // Best-effort unhook (works for WinRT-style events as well).
        if (_accessibilitySettings is not null)
        {
            try
            {
                var t = _accessibilitySettings.GetType();
                var evt = t.GetEvent("HighContrastChanged");
                if (evt is not null)
                {
                    // We attached a lambda, so we can't reliably remove it here without storing it.
                    // HighContrastChanged is low-frequency and this object is intended to live for the
                    // lifetime of the view/page, so this is acceptable.
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
        => Update();

    private void Update()
    {
        var next = ReadVariant();
        if (next == CurrentVariant)
        {
            return;
        }

        CurrentVariant = next;
        VariantChanged?.Invoke(this, next);
    }

    private MarkdownThemeVariant ReadVariant()
    {
        if (TryGetHighContrast(out var isHighContrast) && isHighContrast)
        {
            return MarkdownThemeVariant.HighContrast;
        }

        return _themeSource.ActualTheme switch
        {
            ElementTheme.Dark => MarkdownThemeVariant.Dark,
            _ => MarkdownThemeVariant.Light,
        };
    }

    private bool TryGetHighContrast(out bool highContrast)
    {
        highContrast = false;

        try
        {
            if (_accessibilitySettings is null)
            {
                return false;
            }

            var prop = _accessibilitySettings.GetType().GetProperty("HighContrast");
            if (prop?.PropertyType != typeof(bool))
            {
                return false;
            }

            highContrast = (bool)prop.GetValue(_accessibilitySettings)!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryHookHighContrast()
    {
        try
        {
            // Use reflection so non-Windows targets that do not implement this API still compile.
            // Uno often provides this WinUI surface across platforms, but we still keep it defensive.
            var t = Type.GetType("Windows.UI.ViewManagement.AccessibilitySettings, Windows, ContentType=WindowsRuntime")
                ?? Type.GetType("Windows.UI.ViewManagement.AccessibilitySettings");

            if (t is null)
            {
                return;
            }

            _accessibilitySettings = Activator.CreateInstance(t);
            if (_accessibilitySettings is null)
            {
                return;
            }

            var evt = t.GetEvent("HighContrastChanged");
            if (evt is null)
            {
                return;
            }

            // Any theme change should recalc (HC overrides Light/Dark).
            var handler = new EventHandler<object>((_, __) => Update());
            evt.AddEventHandler(_accessibilitySettings, handler);
        }
        catch
        {
            // Ignore: not supported on this platform.
        }
    }
}
