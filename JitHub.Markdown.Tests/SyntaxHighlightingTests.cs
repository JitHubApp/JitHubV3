using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class SyntaxHighlightingTests
{
    [Test]
    public void Diagnostics_sink_receives_messages_when_enabled_and_highlighter_runs()
    {
        var messages = new List<string>();
        SyntaxHighlightDiagnostics.Enable(messages.Add);

        // "text" is treated as explicit no-highlighting and should emit a diag line about normalized=<null>.
        var spans = ColorCodeSyntaxHighlighter.GetSpans("var x = 1;", fenceInfo: "text", isDark: false);
        spans.Should().BeEmpty();

        messages.Should().NotBeEmpty();
        messages.Any(m => m.Contains("normalized=<null>", StringComparison.Ordinal)).Should().BeTrue();

        SyntaxHighlightDiagnostics.Disable();
    }

    [Test]
    public void Diagnostics_disable_stops_emitting_messages()
    {
        var messages = new List<string>();
        SyntaxHighlightDiagnostics.Enable(messages.Add);

        _ = ColorCodeSyntaxHighlighter.GetSpans("var x = 1;", fenceInfo: "text", isDark: false);
        messages.Count.Should().BeGreaterThan(0);

        SyntaxHighlightDiagnostics.Disable();

        var before = messages.Count;
        _ = ColorCodeSyntaxHighlighter.GetSpans("var y = 2;", fenceInfo: "text", isDark: false);
        messages.Count.Should().Be(before);
    }

    [Test]
    public void GetSpans_returns_cached_instance_for_same_key_and_spans_are_in_range()
    {
        SyntaxHighlightDiagnostics.Disable();

        var code = "var x = 1;\nvar y = x + 2;";

        var spans1 = ColorCodeSyntaxHighlighter.GetSpans(code, fenceInfo: "csharp", isDark: false);
        var spans2 = ColorCodeSyntaxHighlighter.GetSpans(code, fenceInfo: "csharp", isDark: false);

        ReferenceEquals(spans1, spans2).Should().BeTrue("calls with the same key should hit the cache");

        foreach (var s in spans1)
        {
            s.Start.Should().BeGreaterThanOrEqualTo(0);
            s.Length.Should().BeGreaterThanOrEqualTo(0);
            (s.Start + s.Length).Should().BeLessThanOrEqualTo(code.Length);
        }
    }

    [Test]
    public void GetSpans_returns_empty_for_unknown_language_and_does_not_throw()
    {
        SyntaxHighlightDiagnostics.Disable();

        var spans = ColorCodeSyntaxHighlighter.GetSpans("hello", fenceInfo: "this-language-does-not-exist", isDark: false);
        spans.Should().BeEmpty();
    }

    [Test]
    public void GetSpans_returns_empty_when_code_is_empty_or_fence_is_whitespace()
    {
        SyntaxHighlightDiagnostics.Disable();

        ColorCodeSyntaxHighlighter.GetSpans("", fenceInfo: "csharp", isDark: false).Should().BeEmpty();
        ColorCodeSyntaxHighlighter.GetSpans("var x = 1;", fenceInfo: "   ", isDark: false).Should().BeEmpty();
    }

    [Test]
    public void Xaml_fence_uses_xml_alias_and_emits_xmlLikeFlatScopes_diagnostics_when_enabled()
    {
        var messages = new List<string>();
        SyntaxHighlightDiagnostics.Enable(messages.Add);

        var code = "<Grid><TextBlock Text=\"Hello\" /></Grid>";
        var spans = ColorCodeSyntaxHighlighter.GetSpans(code, fenceInfo: "xaml", isDark: false);

        // We don't assert specific tokenization output (implementation detail), but we do
        // assert it ran and didn't produce out-of-range spans.
        foreach (var s in spans)
        {
            s.Start.Should().BeGreaterThanOrEqualTo(0);
            s.Length.Should().BeGreaterThanOrEqualTo(0);
            (s.Start + s.Length).Should().BeLessThanOrEqualTo(code.Length);
        }

        messages.Any(m => m.Contains("xmlLikeFlatScopes=", StringComparison.Ordinal)).Should().BeTrue();

        SyntaxHighlightDiagnostics.Disable();
    }

    [Test]
    public void Json_fence_emits_jsonFlatScopes_diagnostics_when_enabled()
    {
        var messages = new List<string>();
        SyntaxHighlightDiagnostics.Enable(messages.Add);

        var code = "{\n  \"a\": 1,\n  \"b\": true,\n  \"c\": null\n}";
        _ = ColorCodeSyntaxHighlighter.GetSpans(code, fenceInfo: "json", isDark: true);

        messages.Any(m => m.Contains("jsonFlatScopes=", StringComparison.Ordinal)).Should().BeTrue();

        SyntaxHighlightDiagnostics.Disable();
    }

    [Test]
    public void Diagnostics_sink_exceptions_are_swallowed()
    {
        // Enable diagnostics directly so we can inject a throwing sink.
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: true, sink: _ => throw new InvalidOperationException("boom"));

        Action act = () => _ = ColorCodeSyntaxHighlighter.GetSpans("var x = 1;", fenceInfo: "text", isDark: false);
        act.Should().NotThrow();

        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);
    }

    [Test]
    public void Diagnostics_with_null_sink_uses_debug_write_line_path_and_does_not_throw()
    {
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: true, sink: null);

        Action act = () => _ = ColorCodeSyntaxHighlighter.GetSpans("var x = 1;", fenceInfo: "text", isDark: false);
        act.Should().NotThrow();

        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);
    }
}
