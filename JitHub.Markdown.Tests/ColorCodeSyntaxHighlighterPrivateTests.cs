using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using ColorCode.Styling;
using SkiaSharp;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class ColorCodeSyntaxHighlighterPrivateTests
{
    [Test]
    public void NormalizeLanguageId_handles_common_fence_formats()
    {
        InvokeNormalize("").Should().BeNull();
        InvokeNormalize("   ").Should().BeNull();

        InvokeNormalize("text").Should().BeNull();
        InvokeNormalize("plaintext").Should().BeNull();

        InvokeNormalize("csharp").Should().Be("c#");
        InvokeNormalize("CSharp linenums").Should().Be("c#");
        InvokeNormalize("{.language-csharp linenums}").Should().Be("c#");

        InvokeNormalize(".lang-js").Should().Be("javascript");
        InvokeNormalize("language-ts").Should().Be("typescript");
        InvokeNormalize("pwsh").Should().Be("powershell");

        InvokeNormalize("axml").Should().Be("xml");
        InvokeNormalize("xaml").Should().Be("xaml");
        InvokeNormalize("c++").Should().Be("cpp");
    }

    [Test]
    public void TryParseArgb_supports_multiple_formats_and_logs_failures_once_when_diagnostics_enabled()
    {
        var messages = new List<string>();
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: true, sink: messages.Add);

        InvokeTryParseArgb("FF112233").Should().Be(new SKColor(0x11, 0x22, 0x33, 0xFF));
        InvokeTryParseArgb("112233").Should().Be(new SKColor(0x11, 0x22, 0x33, 0xFF));
        InvokeTryParseArgb("0xFF112233").Should().Be(new SKColor(0x11, 0x22, 0x33, 0xFF));
        InvokeTryParseArgb("#FF#E6E6E6").Should().Be(new SKColor(0xE6, 0xE6, 0xE6, 0xFF));

        InvokeTryParseArgb("F123").Should().Be(new SKColor(0x11, 0x22, 0x33, 0xFF));
        InvokeTryParseArgb("123").Should().Be(new SKColor(0x11, 0x22, 0x33, 0xFF));

        var before = messages.Count;
        InvokeTryParseArgbInvalid("not-a-color").Should().BeFalse();
        (messages.Count - before).Should().Be(1);
        messages.Last().Should().Contain("TryParseColor: failed value='not-a-color'");

        before = messages.Count;
        InvokeTryParseArgbInvalid("not-a-color").Should().BeFalse();
        messages.Count.Should().Be(before, "same invalid value should only log once");

        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);
    }

    [Test]
    public void Private_tokenizers_can_be_invoked_via_reflection_and_return_scopes()
    {
        var styles = StyleDictionary.DefaultLight;

        var xml = "<Grid><TextBlock Text=\"Hello\" /></Grid>";
        var xmlScopes = InvokeTryBuildXmlLikeFlatScopes(xml, isXaml: true, styles);
        xmlScopes.Should().NotBeNull();
        xmlScopes!.Count.Should().BeGreaterThan(0);

        var json = "{\n  \"a\": 1,\n  \"b\": true,\n  \"c\": null\n}";
        var jsonScopes = InvokeTryBuildJsonFlatScopes(json, styles);
        jsonScopes.Should().NotBeNull();
        jsonScopes!.Count.Should().BeGreaterThan(0);

        // Also cover the rule-based fallback builder.
        var lang = ColorCode.Languages.FindById("c#");
        lang.Should().NotBeNull();
        var fallback = InvokeTryBuildFlatScopesFromRules("var x = 1;", lang!, styles);
        fallback.Should().NotBeNull();
    }

    [Test]
    public void TryGetStyle_supports_exact_and_case_insensitive_lookup_and_rejects_whitespace()
    {
        var styles = new StyleDictionary();
        styles.Add(new Style("keyword") { Foreground = "FF112233" });

        InvokeTryGetStyle(styles, "keyword").Should().BeTrue();
        InvokeTryGetStyle(styles, "KEYWORD").Should().BeTrue("case-insensitive scan should match dictionary keys");
        InvokeTryGetStyle(styles, "   ").Should().BeFalse();
    }

    [Test]
    public void BuildSpansFromFlatScopes_coalesces_adjacent_spans_with_same_color()
    {
        var messages = new List<string>();
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: true, sink: messages.Add);

        var code = "abcd";
        var styles = new StyleDictionary();
        styles.Add(new Style("keyword") { Foreground = "FF112233" });
        var scopeName = "keyword";

        var flat = CreateFlatScopeList(new (string name, int start, int end, int depth)[]
        {
            (scopeName, 0, 4, 0),
            (scopeName, 0, 2, 1),
        });

        var spans = InvokeBuildSpansFromFlatScopes(code, flat, styles);

        spans.Length.Should().Be(1, "adjacent spans with same color should be merged");
        spans[0].Start.Should().Be(0);
        spans[0].Length.Should().Be(4);

        messages.Any(m => m.Contains("BuildSpans: spansRaw=", StringComparison.Ordinal)).Should().BeTrue();

        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);
    }

    [Test]
    public void BuildSpansFromFlatScopes_returns_empty_when_no_valid_segments_exist()
    {
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);

        var code = "abcd";
        var styles = new StyleDictionary();
        styles.Add(new Style("keyword") { Foreground = "FF112233" });
        var scopeName = "keyword";

        // End <= start means no boundaries beyond {0, len}.
        var flat = CreateFlatScopeList(new (string name, int start, int end, int depth)[]
        {
            (scopeName, 2, 2, 0),
        });

        InvokeBuildSpansFromFlatScopes(code, flat, styles).Should().BeEmpty();
    }

    [Test]
    public void BuildSpansFromFlatScopes_skips_scopes_missing_from_style_dictionary()
    {
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);

        var code = "abcd";
        var styles = new StyleDictionary();
        styles.Add(new Style("Keyword") { Foreground = "FF112233" });

        var flat = CreateFlatScopeList(new (string name, int start, int end, int depth)[]
        {
            ("NotAStyle", 0, 4, 0),
        });

        InvokeBuildSpansFromFlatScopes(code, flat, styles).Should().BeEmpty();
    }

    [Test]
    public void XmlLike_tokenizer_covers_comments_cdata_processing_instructions_and_punctuation()
    {
        var styles = CreateStyles("Keyword", "String", "Comment", "Variable", "Number", "Punctuation", "PreprocessorKeyword");

        var code = "<?xml version=\"1.0\"?>\n<!--c-->\n<![CDATA[x]]>\n<Grid x:Name=\"Root\"><TextBlock Text=\"Hello\" /></Grid>";
        var xmlScopes = InvokeTryBuildXmlLikeFlatScopes(code, isXaml: true, styles);

        xmlScopes.Should().NotBeNull();
        ((ICollection)xmlScopes!).Count.Should().BeGreaterThan(0);

        var names = GetFlatScopeNames(xmlScopes!);
        names.Should().Contain("Punctuation");
        names.Should().Contain("Comment");
        names.Should().Contain("String");
    }

    [Test]
    public void Json_tokenizer_covers_punctuation_numbers_strings_and_keywords()
    {
        var styles = CreateStyles("Keyword", "String", "Number", "Punctuation");

        var code = "{\"a\":1,\"b\":true,\"c\":null,\"arr\":[1,2]}";
        var jsonScopes = InvokeTryBuildJsonFlatScopes(code, styles);

        jsonScopes.Should().NotBeNull();
        ((ICollection)jsonScopes!).Count.Should().BeGreaterThan(0);

        var names = GetFlatScopeNames(jsonScopes!);
        names.Should().Contain("Punctuation");
        names.Should().Contain("String");
        names.Should().Contain("Number");
        names.Should().Contain("Keyword");
    }

    [Test]
    public void ScopeCapturer_exercises_parse_success_and_parse_exception_paths()
    {
        var messages = new List<string>();
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: true, sink: messages.Add);

        var styles = StyleDictionary.DefaultLight;
        var capturer = CreateScopeCapturer(styles);
        var lang = ColorCode.Languages.FindById("c#");
        lang.Should().NotBeNull();

        messages.Any(m => m.Contains("ScopeCapturer: parserType=", StringComparison.Ordinal)).Should().BeTrue();

        // Success path: should emit Parse done when diagnostics enabled.
        var scopes = InvokeScopeCapturerCapture(capturer, "var x = 1;", lang!);
        scopes.Should().NotBeNull();
        messages.Any(m => m.Contains("Parse done", StringComparison.Ordinal)).Should().BeTrue();

        // Exception path: set internal languageParser to null so Capture throws and is caught.
        SetScopeCapturerLanguageParser(capturer, null);
        var before = messages.Count;
        _ = InvokeScopeCapturerCapture(capturer, "var y = 2;", lang!);
        messages.Skip(before).Any(m => m.Contains("Parse threw", StringComparison.Ordinal)).Should().BeTrue();

        // Cover the protected Write override.
        InvokeScopeCapturerWrite(capturer, parsedSourceCode: "", scopes: Array.Empty<ColorCode.Parsing.Scope>());

        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);
    }

    [Test]
    public void GuessScopeName_heuristics_cover_common_categories()
    {
        InvokeGuessScopeName("c#", 0, "// comment").Should().Be("Comment");
        InvokeGuessScopeName("powershell", 0, "\\$env:PATH").Should().Be("Variable");
        InvokeGuessScopeName("c#", 0, "\\b(foo|bar)").Should().Be("Keyword");
        InvokeGuessScopeName("c#", 0, "\"str\"").Should().Be("String");
        InvokeGuessScopeName("c#", 0, "\\d+").Should().Be("Number");
        InvokeGuessScopeName("c#", 0, "nope").Should().BeNull();
    }

    [Test]
    public void NormalizeScopeName_trims_leaf_and_braces()
    {
        InvokeNormalizeScopeName(null).Should().BeNull();
        InvokeNormalizeScopeName("   ").Should().BeNull();
        InvokeNormalizeScopeName("Html.Tag").Should().Be("Tag");
        InvokeNormalizeScopeName("{ Keyword }\r\n").Should().Be("Keyword");
    }

    [Test]
    public void Flatten_collects_self_and_children_and_skips_zero_length_scopes()
    {
        var parent = new ColorCode.Parsing.Scope("Parent", index: 0, length: 4)
        {
            Children = new List<ColorCode.Parsing.Scope>
            {
                new("Child", index: 1, length: 2),
                new("Empty", index: 2, length: 0),
            },
        };

        var list = CreateFlatScopeList([]);
        InvokeFlatten(parent, depth: 0, list);

        ((ICollection)list).Count.Should().Be(2);
    }

    [Test]
    public void TryLogRegexProbe_emits_diagnostics_for_some_rules()
    {
        var messages = new List<string>();
        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: true, sink: messages.Add);

        var lang = ColorCode.Languages.FindById("c#");
        lang.Should().NotBeNull();

        InvokeTryLogRegexProbe("public class C { }", lang!);

        messages.Any(m => m.Contains("RegexProbe:", StringComparison.Ordinal)).Should().BeTrue();

        ColorCodeSyntaxHighlighter.SetDiagnostics(enabled: false, sink: null);
    }

    private static string? InvokeNormalize(string fenceInfo)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "NormalizeLanguageId",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, [fenceInfo]);
    }

    private static SKColor InvokeTryParseArgb(string value)
    {
        var (ok, color) = InvokeTryParseArgbCore(value);
        ok.Should().BeTrue();
        return color;
    }

    private static bool InvokeTryParseArgbInvalid(string value)
    {
        var (ok, _) = InvokeTryParseArgbCore(value);
        return ok;
    }

    private static (bool ok, SKColor color) InvokeTryParseArgbCore(string value)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "TryParseArgb",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        object?[] args = [value, null!];
        var ok = (bool)method!.Invoke(null, args)!;
        var color = (SKColor)args[1]!;
        return (ok, color);
    }

    private static IList? InvokeTryBuildXmlLikeFlatScopes(string code, bool isXaml, StyleDictionary styles)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "TryBuildXmlLikeFlatScopes",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (IList?)method!.Invoke(null, [code, isXaml, styles]);
    }

    private static IList? InvokeTryBuildJsonFlatScopes(string code, StyleDictionary styles)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "TryBuildJsonFlatScopes",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (IList?)method!.Invoke(null, [code, styles]);
    }

    private static IList? InvokeTryBuildFlatScopesFromRules(string code, ColorCode.ILanguage language, StyleDictionary styles)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "TryBuildFlatScopesFromRules",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (IList?)method!.Invoke(null, [code, language, styles]);
    }

    private static bool InvokeTryGetStyle(StyleDictionary styles, string scopeName)
    {
        return InvokeTryGetStyleWithStyle(styles, scopeName, out _);
    }

    private static bool InvokeTryGetStyleWithStyle(StyleDictionary styles, string scopeName, out Style style)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "TryGetStyle",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        object?[] args = [styles, scopeName, null!];
        var ok = (bool)method!.Invoke(null, args)!;
        style = ok ? (Style)args[2]! : default!;
        return ok;
    }

    // Intentionally no dependency on StyleDictionary.DefaultLight contents.

    private static object CreateFlatScopeList((string name, int start, int end, int depth)[] items)
    {
        var flatScopeType = typeof(ColorCodeSyntaxHighlighter).GetNestedType("FlatScope", BindingFlags.NonPublic);
        flatScopeType.Should().NotBeNull();

        var listType = typeof(List<>).MakeGenericType(flatScopeType!);
        var list = Activator.CreateInstance(listType)!
            ?? throw new InvalidOperationException("Failed to create FlatScope list");

        var add = listType.GetMethod("Add")!;

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var flat = Activator.CreateInstance(flatScopeType!, [item.name, item.start, item.end, item.depth]);
            flat.Should().NotBeNull();
            add.Invoke(list, [flat]);
        }

        return list;
    }

    private static StyleDictionary CreateStyles(params string[] scopeNames)
    {
        var dict = new StyleDictionary();
        for (var i = 0; i < scopeNames.Length; i++)
        {
            dict.Add(new Style(scopeNames[i]) { Foreground = "FF112233" });
        }

        return dict;
    }

    private static HashSet<string> GetFlatScopeNames(IList flatScopes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < flatScopes.Count; i++)
        {
            var item = flatScopes[i]!;
            var name = item.GetType().GetProperty("Name")!.GetValue(item) as string;
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static void InvokeFlatten(ColorCode.Parsing.Scope scope, int depth, object flatScopeList)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "Flatten",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [scope, depth, flatScopeList]);
    }

    private static string? InvokeGuessScopeName(string languageId, int ruleIndex, string regex)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "GuessScopeName",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, [languageId, ruleIndex, regex]);
    }

    private static string? InvokeNormalizeScopeName(string? scope)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "NormalizeScopeName",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, [scope]);
    }

    private static void InvokeTryLogRegexProbe(string code, ColorCode.ILanguage language)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "TryLogRegexProbe",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [code, language]);
    }

    private static ColorCodeSyntaxHighlighter.CodeSpan[] InvokeBuildSpansFromFlatScopes(string code, object flatScopeList, StyleDictionary styles)
    {
        var method = typeof(ColorCodeSyntaxHighlighter).GetMethod(
            "BuildSpansFromFlatScopes",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        return (ColorCodeSyntaxHighlighter.CodeSpan[])method!.Invoke(null, [code, flatScopeList, styles])!;
    }

    private static object CreateScopeCapturer(StyleDictionary styles)
    {
        var t = typeof(ColorCodeSyntaxHighlighter).GetNestedType("ScopeCapturer", BindingFlags.NonPublic);
        t.Should().NotBeNull();
        return Activator.CreateInstance(t!, [styles])!;
    }

    private static IList InvokeScopeCapturerCapture(object capturer, string code, ColorCode.ILanguage language)
    {
        var method = capturer.GetType().GetMethod("Capture", BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull();
        return (IList)method!.Invoke(capturer, [code, language])!;
    }

    private static void SetScopeCapturerLanguageParser(object capturer, object? value)
    {
        // languageParser is a protected field on CodeColorizerBase (base type)
        var field = capturer.GetType().BaseType!.GetField("languageParser", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.SetValue(capturer, value);
    }

    private static void InvokeScopeCapturerWrite(object capturer, string parsedSourceCode, IList<ColorCode.Parsing.Scope> scopes)
    {
        var method = capturer.GetType().GetMethod(
            "Write",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Should().NotBeNull();
        method!.Invoke(capturer, [parsedSourceCode, scopes]);
    }
}
