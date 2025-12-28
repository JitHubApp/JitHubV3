using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;

namespace JitHub.Markdown.Uno;

internal static class MarkdownLogCategories
{
    // High-level segments (categories) so appsettings can control them independently.
    public const string UnoInput = "JitHub.Markdown.Uno.Input";
    public const string UnoSelection = "JitHub.Markdown.Uno.Selection";
    public const string UnoRender = "JitHub.Markdown.Uno.Render";

    public const string SkiaSyntaxHighlighting = "JitHub.Markdown.Skia.SyntaxHighlighting";
}

internal static class MarkdownLog
{
    public static ILogger CreateLogger(DependencyObject owner, string category)
    {
        if (TryGetLoggerFactory() is { } factory)
        {
            return factory.CreateLogger(category);
        }

        // Fallback: no access to app services / logger factory.
        return NullLogger.Instance;
    }

    private static ILoggerFactory? TryGetLoggerFactory()
    {
        try
        {
            var app = Application.Current;
            if (app is null)
            {
                return null;
            }

            // We intentionally avoid a compile-time dependency on the app type.
            // The app exposes a public instance property: IServiceProvider? Services { get; }
            var servicesProp = app.GetType().GetProperty(
                "Services",
                BindingFlags.Instance | BindingFlags.Public);

            if (servicesProp?.GetValue(app) is not IServiceProvider services)
            {
                return null;
            }

            return services.GetService<ILoggerFactory>();
        }
        catch
        {
            return null;
        }
    }
}
