using System;

namespace JitHub.Markdown;

public static class SyntaxHighlightDiagnostics
{
    /// <summary>
    /// Enables verbose syntax highlighting diagnostics. If <paramref name="sink"/> is null,
    /// logs go to <see cref="System.Diagnostics.Debug.WriteLine(string)"/>.
    /// </summary>
    public static void Enable(Action<string>? sink = null)
        => ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: true, sink);

    public static void Disable()
        => ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);
}
