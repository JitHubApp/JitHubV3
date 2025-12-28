using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;
using SkiaSharp;

namespace JitHub.Markdown;

internal static class ColorCodeSyntaxHighlighter
{
    private readonly record struct CacheKey(string LanguageId, string Code, bool IsDark);

    private static readonly ConcurrentDictionary<CacheKey, CodeSpan[]> Cache = new();

    private static volatile bool DiagnosticsEnabled;
    private static Action<string>? DiagnosticsSink;
    private static readonly ConcurrentDictionary<string, byte> LoggedParseFailures = new(StringComparer.Ordinal);
    private const int MaxLoggedParseFailures = 32;

    internal static void SetDiagnostics(bool enabled, Action<string>? sink)
    {
        DiagnosticsEnabled = enabled;
        DiagnosticsSink = sink;
    }

    private static void Diag(string message)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        try
        {
            var sink = DiagnosticsSink;
            var line = $"[SyntaxHL] {DateTimeOffset.Now:HH:mm:ss.fff} {message}";
            if (sink is not null)
            {
                sink(line);
            }
            else
            {
                Debug.WriteLine(line);
            }
        }
        catch
        {
            // Never let diagnostics break rendering.
        }
    }

    internal readonly record struct CodeSpan(int Start, int Length, SKColor Foreground);

    public static CodeSpan[] GetSpans(string code, string? fenceInfo, bool isDark)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(fenceInfo))
        {
            return [];
        }

        var languageId = NormalizeLanguageId(fenceInfo);
        if (string.IsNullOrEmpty(languageId))
        {
            Diag($"GetSpans: fence='{fenceInfo}' normalized=<null> (no highlighting) codeLen={code.Length} isDark={isDark}");
            return [];
        }

        Diag($"GetSpans: fence='{fenceInfo}' normalized='{languageId}' codeLen={code.Length} isDark={isDark}");

        var key = new CacheKey(languageId, code, isDark);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ILanguage? language;
        try
        {
            // Treat some fences as aliases for another ColorCode language, while keeping the original
            // ID around so we can apply specialized tokenizers.
            var lookupId = languageId == "xaml" ? "xml" : languageId;
            language = Languages.FindById(lookupId);
        }
        catch
        {
            language = null;
        }

        if (language is null)
        {
            Diag($"GetSpans: language not found id='{languageId}'");
            Cache.TryAdd(key, []);
            return [];
        }

        int? ruleCount = null;
        try
        {
            ruleCount = language.Rules?.Count;
        }
        catch
        {
            ruleCount = null;
        }

        Diag($"GetSpans: language resolved id='{languageId}' name='{language.Name}' type='{language.GetType().FullName}' rules={ruleCount?.ToString() ?? "?"}");

        var styles = isDark ? StyleDictionary.DefaultDark : StyleDictionary.DefaultLight;
        var spans = BuildSpans(code, language, styles, languageId);

        Diag($"GetSpans: spans={spans.Length} (cachedKey={languageId}, dark={isDark})");

        Cache.TryAdd(key, spans);
        return spans;
    }

    private static CodeSpan[] BuildSpans(string code, ILanguage language, StyleDictionary styles, string requestedLanguageId)
    {
        // Specialized tokenizers for fences where ColorCode is too sparse or inconsistent.
        // We keep these small and conservative.
        if (requestedLanguageId is "xaml" or "xml")
        {
            var xmlFlat = TryBuildXmlLikeFlatScopes(code, isXaml: requestedLanguageId == "xaml", styles);
            if (xmlFlat is not null && xmlFlat.Count > 0)
            {
                Diag($"BuildSpans: xmlLikeFlatScopes={xmlFlat.Count} (requested='{requestedLanguageId}')");
                return BuildSpansFromFlatScopes(code, xmlFlat, styles);
            }
        }

        if (requestedLanguageId is "json")
        {
            var jsonFlat = TryBuildJsonFlatScopes(code, styles);
            if (jsonFlat is not null && jsonFlat.Count > 0)
            {
                Diag($"BuildSpans: jsonFlatScopes={jsonFlat.Count}");
                return BuildSpansFromFlatScopes(code, jsonFlat, styles);
            }
        }

        // Use ColorCode's own default parser setup (via CodeColorizerBase) so we don't
        // depend on internal LanguageParser construction details.
        var capturer = new ScopeCapturer(styles);
        var captured = capturer.Capture(code, language);

        if (captured.Count == 0)
        {
            int? ruleCount = null;
            try
            {
                ruleCount = language.Rules?.Count;
            }
            catch
            {
                ruleCount = null;
            }

            Diag($"BuildSpans: capturedScopes=0 (language='{language.Name}' id='{language.Id}' rules={ruleCount?.ToString() ?? "?"})");

            // Fallback: Some runtimes (notably Android/Mono/AOT) can return zero scopes from ColorCode's
            // LanguageParser for languages with more complex regexes. When that happens, we can still
            // produce useful highlighting by applying the language rules directly.
            var fallbackFlat = TryBuildFlatScopesFromRules(code, language, styles);

            if (fallbackFlat is not null && fallbackFlat.Count > 0)
            {
                Diag($"BuildSpans: fallbackFlatScopes={fallbackFlat.Count}");
                return BuildSpansFromFlatScopes(code, fallbackFlat, styles);
            }

            if (DiagnosticsEnabled)
            {
                TryLogRegexProbe(code, language);

                // If we have rules but still no scopes, dump a little rule summary.
                try
                {
                    var rules = language.Rules;
                    if (rules is not null)
                    {
                        var preview = Math.Min(rules.Count, 6);
                        for (var i = 0; i < preview; i++)
                        {
                            var r = rules[i];
                            var regex = r.Regex ?? string.Empty;
                            var caps = r.Captures?.Count ?? 0;
                            var head = regex.Length > 40 ? regex[..40] + "…" : regex;
                            Diag($"BuildSpans: rule[{i}] regexLen={regex.Length} captures={caps} regexHead='{head.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Diag($"BuildSpans: rule dump failed: {ex.GetType().Name}: {ex.Message}");
                }

                return [];
            }
        }

        Diag($"BuildSpans: capturedScopes={captured.Count}");

        var flat = new List<FlatScope>(capacity: captured.Count * 2);
        for (var i = 0; i < captured.Count; i++)
        {
            Flatten(captured[i], depth: 0, flat);
        }

        if (flat.Count == 0)
        {
            Diag("BuildSpans: flatScopes=0");
            return [];
        }

        Diag($"BuildSpans: flatScopes={flat.Count}");

        return BuildSpansFromFlatScopes(code, flat, styles);
    }

    private static CodeSpan[] BuildSpansFromFlatScopes(string code, List<FlatScope> flat, StyleDictionary styles)
    {
        // Collect boundaries.
        var boundaries = new SortedSet<int> { 0, code.Length };
        for (var i = 0; i < flat.Count; i++)
        {
            var s = Clamp(flat[i].Start, 0, code.Length);
            var e = Clamp(flat[i].End, 0, code.Length);
            if (e <= s)
            {
                continue;
            }

            boundaries.Add(s);
            boundaries.Add(e);
        }

        if (boundaries.Count <= 2)
        {
            Diag($"BuildSpans: boundaries={boundaries.Count} (no segments)");
            return [];
        }

        Diag($"BuildSpans: boundaries={boundaries.Count}");

        var boundaryArray = boundaries.ToArray();
        var results = new List<CodeSpan>();

        var scopeColorCache = new Dictionary<string, SKColor?>(StringComparer.OrdinalIgnoreCase);

        SKColor? GetScopeColor(string scopeName)
        {
            if (scopeColorCache.TryGetValue(scopeName, out var cached))
            {
                return cached;
            }

            if (!TryGetStyle(styles, scopeName, out var style))
            {
                scopeColorCache[scopeName] = null;
                return null;
            }

            if (string.IsNullOrWhiteSpace(style.Foreground))
            {
                scopeColorCache[scopeName] = null;
                return null;
            }

            if (!TryParseArgb(style.Foreground, out var color))
            {
                scopeColorCache[scopeName] = null;
                return null;
            }

            scopeColorCache[scopeName] = color;
            return color;
        }

        for (var bi = 0; bi < boundaryArray.Length - 1; bi++)
        {
            var start = boundaryArray[bi];
            var end = boundaryArray[bi + 1];
            if (end <= start)
            {
                continue;
            }

            // Choose the deepest scope that actually has a known, parseable foreground color.
            SKColor? bestColor = null;
            var bestDepth = int.MinValue;

            for (var i = 0; i < flat.Count; i++)
            {
                var f = flat[i];
                if (f.Start > start || f.End < end)
                {
                    continue;
                }

                if (f.Depth < bestDepth)
                {
                    continue;
                }

                var candidateColor = GetScopeColor(f.Name);
                if (candidateColor is null)
                {
                    continue;
                }

                bestColor = candidateColor;
                bestDepth = f.Depth;
            }

            if (bestColor is null)
            {
                continue;
            }

            results.Add(new CodeSpan(start, end - start, bestColor.Value));
        }

        // Coalesce adjacent spans with same color.
        results.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<CodeSpan>(results.Count);
        for (var i = 0; i < results.Count; i++)
        {
            var s = results[i];
            if (merged.Count == 0)
            {
                merged.Add(s);
                continue;
            }

            var last = merged[^1];
            if (last.Start + last.Length == s.Start && last.Foreground == s.Foreground)
            {
                merged[^1] = last with { Length = last.Length + s.Length };
            }
            else
            {
                merged.Add(s);
            }
        }

        var final = merged.ToArray();

        if (final.Length > 0)
        {
            var preview = Math.Min(final.Length, 8);
            for (var i = 0; i < preview; i++)
            {
                var s = final[i];
                Diag($"BuildSpans: span[{i}] start={s.Start} len={s.Length} color=#{s.Foreground.Alpha:X2}{s.Foreground.Red:X2}{s.Foreground.Green:X2}{s.Foreground.Blue:X2}");
            }
        }

        Diag($"BuildSpans: spansRaw={results.Count} spansMerged={final.Length}");
        return final;
    }

    private static bool TryGetStyle(StyleDictionary styles, string scopeName, out Style style)
    {
        style = default!;
        if (string.IsNullOrWhiteSpace(scopeName))
        {
            return false;
        }

        // Fast-path: exact match.
        try
        {
            if (styles.Contains(scopeName))
            {
                style = styles[scopeName];
                return true;
            }
        }
        catch
        {
            // ignore
        }

        // Slow-path: attempt case-insensitive lookup by scanning keys.
        try
        {
            if (styles is System.Collections.IDictionary dict)
            {
                foreach (var keyObj in dict.Keys)
                {
                    if (keyObj is not string key)
                    {
                        continue;
                    }

                    if (!string.Equals(key, scopeName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (dict[keyObj] is Style s)
                    {
                        style = s;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        // Some StyleDictionary implementations are not IDictionary but are enumerable.
        // Scan contained styles by ScopeName as another case-insensitive fallback.
        try
        {
            foreach (var s in styles)
            {
                if (string.Equals(s.ScopeName, scopeName, StringComparison.OrdinalIgnoreCase))
                {
                    style = s;
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        // Last resort: reflection for an indexer-like "Item" taking string.
        try
        {
            var indexer = styles.GetType().GetProperty("Item", new[] { typeof(string) });
            if (indexer is null)
            {
                return false;
            }

            // If we can't enumerate keys, we can't find alternate casing reliably.
            // Still, some implementations might already do case-insensitive lookup.
            var val = indexer.GetValue(styles, new object[] { scopeName });
            if (val is Style s2)
            {
                style = s2;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static List<FlatScope>? TryBuildFlatScopesFromRules(string code, ILanguage language, StyleDictionary styles)
    {
        IList<LanguageRule>? rules;
        try
        {
            rules = language.Rules;
        }
        catch
        {
            rules = null;
        }

        if (rules is null || rules.Count == 0)
        {
            return null;
        }

        var flat = new List<FlatScope>();
        var timeout = TimeSpan.FromMilliseconds(60);

        var heuristicAdded = 0;
        var captureMapAdded = 0;

        for (var ri = 0; ri < rules.Count; ri++)
        {
            var rule = rules[ri];
            var pattern = rule.Regex;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var captureMap = GetRuleCaptureMap(rule);
            var useHeuristic = captureMap.Count == 0;

            string? heuristicScope = null;
            if (useHeuristic)
            {
                heuristicScope = GuessScopeName(language.Id, ri, pattern);
                if (string.IsNullOrWhiteSpace(heuristicScope))
                {
                    continue;
                }

                // Don't spend time capturing scopes that we can't style anyway.
                if (!TryGetStyle(styles, heuristicScope, out _))
                {
                    continue;
                }
            }

            Regex regex;
            try
            {
                // Avoid RegexOptions.Compiled to keep this compatible with AOT/Mobile.
                regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline, timeout);
            }
            catch
            {
                continue;
            }

            try
            {
                for (var m = regex.Match(code); m.Success; m = m.NextMatch())
                {
                    if (useHeuristic)
                    {
                        var g0 = m.Groups.Count > 0 ? m.Groups[0] : null;
                        if (g0 is null || !g0.Success || g0.Length <= 0)
                        {
                            continue;
                        }

                        var depth = (ri * 16);
                        flat.Add(new FlatScope(heuristicScope!, g0.Index, g0.Index + g0.Length, depth));
                        heuristicAdded++;
                        continue;
                    }

                    for (var ci = 0; ci < captureMap.Count; ci++)
                    {
                        var (groupIndex, scopeNameRaw) = captureMap[ci];
                        if (groupIndex < 0 || groupIndex >= m.Groups.Count)
                        {
                            continue;
                        }

                        var g = m.Groups[groupIndex];
                        if (!g.Success || g.Length <= 0)
                        {
                            continue;
                        }

                        var scopeName = NormalizeScopeName(scopeNameRaw);
                        if (string.IsNullOrWhiteSpace(scopeName))
                        {
                            continue;
                        }

                        // If the scope isn't stylable, skip it.
                        if (!TryGetStyle(styles, scopeName, out _))
                        {
                            continue;
                        }

                        // Depth heuristic: later rules override earlier ones.
                        var depth = (ri * 16) + ci;
                        flat.Add(new FlatScope(scopeName, g.Index, g.Index + g.Length, depth));
                        captureMapAdded++;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip pathological patterns for this input.
            }
            catch
            {
                // Ignore rule failures and continue.
            }
        }

        if (DiagnosticsEnabled)
        {
            Diag($"BuildSpans: fallbackCollect heuristics={heuristicAdded} captureMap={captureMapAdded} flat={flat.Count}");
        }

        return flat;
    }

    private static string? NormalizeScopeName(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        var s = scope.Trim();

        // Some libraries prefix scope names (e.g. "Html.Tag"), keep the leaf.
        var lastDot = s.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < s.Length - 1)
        {
            s = s[(lastDot + 1)..];
        }

        // Trim braces or separators that sometimes appear.
        s = s.Trim('{', '}', ' ', '\t', '\r', '\n');
        if (s.Length == 0)
        {
            return null;
        }

        return s;
    }

    private static string? GuessScopeName(string languageId, int ruleIndex, string regex)
    {
        // Keep this intentionally simple: we just want *some* useful coloring on runtimes
        // where ColorCode's parser yields no scopes.
        // Use PascalCase keys that match ColorCode default StyleDictionary.

        // Comments first.
        if (regex.Contains("///", StringComparison.Ordinal)
            || regex.Contains("//", StringComparison.Ordinal)
            || regex.Contains("/*", StringComparison.Ordinal)
            || regex.Contains("--", StringComparison.Ordinal)
            || regex.Contains("<#", StringComparison.Ordinal)
            || regex.Contains("#>", StringComparison.Ordinal)
            || (languageId.Equals("powershell", StringComparison.OrdinalIgnoreCase) && regex.Contains("#", StringComparison.Ordinal)))
        {
            return "Comment";
        }

        // PowerShell variables.
        if (languageId.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            && regex.Contains("\\$", StringComparison.Ordinal))
        {
            return "Variable";
        }

        // Keyword lists are typically huge and contain "\b(" (often also contain quotes in lookbehinds).
        if (regex.Contains("\\b(", StringComparison.Ordinal) || regex.Contains("(?i)\\b", StringComparison.Ordinal))
        {
            return "Keyword";
        }

        // Strings next.
        if (regex.Contains("@\"", StringComparison.Ordinal)
            || regex.Contains("\"", StringComparison.Ordinal)
            || regex.Contains("'", StringComparison.Ordinal))
        {
            return "String";
        }

        // Some languages have a dedicated numeric rule; fall back to Number.
        if (regex.Contains("\\d", StringComparison.Ordinal))
        {
            return "Number";
        }

        // Last-resort: in C# the first few rules are generally comments/strings already handled.
        // Avoid applying unknown categories.
        return null;
    }

    private static List<FlatScope>? TryBuildXmlLikeFlatScopes(string code, bool isXaml, StyleDictionary styles)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        // Use existing ColorCode style keys when available.
        var hasKeyword = TryGetStyle(styles, "Keyword", out _);
        var hasString = TryGetStyle(styles, "String", out _);
        var hasComment = TryGetStyle(styles, "Comment", out _);
        var hasVariable = TryGetStyle(styles, "Variable", out _);
        var hasNumber = TryGetStyle(styles, "Number", out _);
        var hasPunctuation = TryGetStyle(styles, "Punctuation", out _);
        var hasPreprocessor = TryGetStyle(styles, "PreprocessorKeyword", out _);

        if (!hasKeyword && !hasString && !hasComment && !hasVariable)
        {
            return null;
        }

        static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is ':' or '_' or '-' or '.';

        var flat = new List<FlatScope>();
        var i = 0;
        while (i < code.Length)
        {
            // XML comments.
            if (hasComment && i + 4 <= code.Length && code.AsSpan(i, 4).SequenceEqual("<!--"))
            {
                var end = code.IndexOf("-->", i + 4, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + 3;
                flat.Add(new FlatScope("Comment", i, end, 5000));
                i = end;
                continue;
            }

            // CDATA (treat as string-ish).
            if (hasString && i + 9 <= code.Length && code.AsSpan(i, 9).SequenceEqual("<![CDATA["))
            {
                var end = code.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + 3;
                flat.Add(new FlatScope("String", i, end, 4500));
                i = end;
                continue;
            }

            // Tags.
            if (code[i] == '<')
            {
                var tagStart = i;

                // Punctuation for '<' / '</' if style exists.
                if (hasPunctuation)
                {
                    var punctEnd = (i + 1 < code.Length && code[i + 1] == '/') ? i + 2 : i + 1;
                    flat.Add(new FlatScope("Punctuation", i, punctEnd, 1000));
                }

                i++;
                if (i < code.Length && code[i] == '/')
                {
                    i++;
                }

                // Processing instruction / directive.
                if (i < code.Length && code[i] == '?')
                {
                    var endPi = code.IndexOf("?>", i + 1, StringComparison.Ordinal);
                    endPi = endPi < 0 ? code.Length : endPi + 2;
                    if (hasPreprocessor)
                    {
                        flat.Add(new FlatScope("PreprocessorKeyword", tagStart, endPi, 4000));
                    }
                    else if (hasKeyword)
                    {
                        flat.Add(new FlatScope("Keyword", tagStart, endPi, 4000));
                    }
                    i = endPi;
                    continue;
                }

                // Tag name.
                while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
                var nameStart = i;
                while (i < code.Length && IsNameChar(code[i])) i++;
                var nameEnd = i;
                if (hasKeyword && nameEnd > nameStart)
                {
                    flat.Add(new FlatScope("Keyword", nameStart, nameEnd, 3000));
                }

                // Attributes.
                while (i < code.Length)
                {
                    // Skip whitespace.
                    while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
                    if (i >= code.Length) break;

                    // End tag.
                    if (code[i] == '>')
                    {
                        if (hasPunctuation)
                        {
                            flat.Add(new FlatScope("Punctuation", i, i + 1, 1000));
                        }
                        i++;
                        break;
                    }

                    if (code[i] == '/' && i + 1 < code.Length && code[i + 1] == '>')
                    {
                        if (hasPunctuation)
                        {
                            flat.Add(new FlatScope("Punctuation", i, i + 2, 1000));
                        }
                        i += 2;
                        break;
                    }

                    // Attribute name.
                    var attrStart = i;
                    while (i < code.Length && IsNameChar(code[i])) i++;
                    var attrEnd = i;
                    if (hasVariable && attrEnd > attrStart)
                    {
                        flat.Add(new FlatScope("Variable", attrStart, attrEnd, 2500));
                    }

                    // Skip whitespace.
                    while (i < code.Length && char.IsWhiteSpace(code[i])) i++;

                    // Optional '='
                    if (i < code.Length && code[i] == '=')
                    {
                        if (hasPunctuation)
                        {
                            flat.Add(new FlatScope("Punctuation", i, i + 1, 1000));
                        }
                        i++;
                        while (i < code.Length && char.IsWhiteSpace(code[i])) i++;

                        // Attribute value.
                        if (i < code.Length && (code[i] == '\'' || code[i] == '"'))
                        {
                            var quote = code[i];
                            var valueStart = i;
                            i++;
                            while (i < code.Length)
                            {
                                if (code[i] == quote)
                                {
                                    i++;
                                    break;
                                }
                                // Allow escaping.
                                if (code[i] == '\\' && i + 1 < code.Length)
                                {
                                    i += 2;
                                    continue;
                                }
                                i++;
                            }

                            var valueEnd = i;
                            if (hasString && valueEnd > valueStart)
                            {
                                flat.Add(new FlatScope("String", valueStart, valueEnd, 2000));
                            }

                            // XAML markup extension inside attribute values: {Binding ...}
                            if (isXaml && hasPreprocessor)
                            {
                                var span = code.AsSpan(valueStart, Math.Max(0, valueEnd - valueStart));
                                var open = span.IndexOf('{');
                                if (open >= 0)
                                {
                                    var close = span.LastIndexOf('}');
                                    if (close > open)
                                    {
                                        var absOpen = valueStart + open;
                                        var absClose = valueStart + close + 1;
                                        flat.Add(new FlatScope("PreprocessorKeyword", absOpen, absClose, 3500));
                                    }
                                }
                            }

                            continue;
                        }

                        // Unquoted value.
                        var unqStart = i;
                        while (i < code.Length && !char.IsWhiteSpace(code[i]) && code[i] != '>' && !(code[i] == '/' && i + 1 < code.Length && code[i + 1] == '>'))
                        {
                            i++;
                        }
                        var unqEnd = i;
                        if (unqEnd > unqStart)
                        {
                            if (hasNumber && LooksLikeNumber(code.AsSpan(unqStart, unqEnd - unqStart)))
                            {
                                flat.Add(new FlatScope("Number", unqStart, unqEnd, 2000));
                            }
                            else if (hasString)
                            {
                                flat.Add(new FlatScope("String", unqStart, unqEnd, 2000));
                            }
                        }
                        continue;
                    }

                    // Attribute without value.
                }

                continue;
            }

            i++;
        }

        return flat.Count == 0 ? null : flat;
    }

    private static List<FlatScope>? TryBuildJsonFlatScopes(string code, StyleDictionary styles)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        var hasString = TryGetStyle(styles, "String", out _);
        var hasNumber = TryGetStyle(styles, "Number", out _);
        var hasKeyword = TryGetStyle(styles, "Keyword", out _);
        var hasVariable = TryGetStyle(styles, "Variable", out _);
        var hasPunctuation = TryGetStyle(styles, "Punctuation", out _);

        if (!hasString && !hasNumber && !hasKeyword)
        {
            return null;
        }

        var flat = new List<FlatScope>();

        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];

            // Whitespace.
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Punctuation.
            if (hasPunctuation && c is '{' or '}' or '[' or ']' or ':' or ',')
            {
                flat.Add(new FlatScope("Punctuation", i, i + 1, 500));
                i++;
                continue;
            }

            // String.
            if (c == '"')
            {
                var start = i;
                i++;
                while (i < code.Length)
                {
                    if (code[i] == '"')
                    {
                        i++;
                        break;
                    }
                    if (code[i] == '\\' && i + 1 < code.Length)
                    {
                        i += 2;
                        continue;
                    }
                    i++;
                }
                var end = i;

                // Detect "key":
                var j = end;
                while (j < code.Length && char.IsWhiteSpace(code[j])) j++;
                var isKey = j < code.Length && code[j] == ':';

                if (isKey && hasVariable)
                {
                    flat.Add(new FlatScope("Variable", start, end, 2500));
                }
                else if (hasString)
                {
                    flat.Add(new FlatScope("String", start, end, 2000));
                }

                continue;
            }

            // Numbers.
            if (hasNumber && (c == '-' || char.IsDigit(c)))
            {
                var start = i;
                i++;

                while (i < code.Length)
                {
                    var ch = code[i];
                    if (char.IsDigit(ch) || ch is '.' or 'e' or 'E' or '+' or '-')
                    {
                        i++;
                        continue;
                    }
                    break;
                }

                flat.Add(new FlatScope("Number", start, i, 2000));
                continue;
            }

            // Keywords: true/false/null
            if (hasKeyword)
            {
                if (i + 4 <= code.Length && code.AsSpan(i, 4).Equals("true".AsSpan(), StringComparison.Ordinal))
                {
                    flat.Add(new FlatScope("Keyword", i, i + 4, 2200));
                    i += 4;
                    continue;
                }
                if (i + 5 <= code.Length && code.AsSpan(i, 5).Equals("false".AsSpan(), StringComparison.Ordinal))
                {
                    flat.Add(new FlatScope("Keyword", i, i + 5, 2200));
                    i += 5;
                    continue;
                }
                if (i + 4 <= code.Length && code.AsSpan(i, 4).Equals("null".AsSpan(), StringComparison.Ordinal))
                {
                    flat.Add(new FlatScope("Keyword", i, i + 4, 2200));
                    i += 4;
                    continue;
                }
            }

            i++;
        }

        return flat.Count == 0 ? null : flat;
    }

    private static bool LooksLikeNumber(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var i = 0;
        if (value[0] == '-')
        {
            i++;
        }

        var sawDigit = false;
        for (; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsDigit(c))
            {
                sawDigit = true;
                continue;
            }
            if (c is '.' or 'e' or 'E' or '+' or '-')
            {
                continue;
            }

            return false;
        }

        return sawDigit;
    }

    private static List<(int GroupIndex, string ScopeName)> GetRuleCaptureMap(LanguageRule rule)
    {
        var result = new List<(int, string)>();

        object? capturesObj;
        try
        {
            capturesObj = rule.Captures;
        }
        catch
        {
            capturesObj = null;
        }

        if (capturesObj is null)
        {
            return result;
        }

        if (capturesObj is not System.Collections.IEnumerable enumerable)
        {
            return result;
        }

        foreach (var cap in enumerable)
        {
            if (cap is null)
            {
                continue;
            }

            var t = cap.GetType();

            string? scope = null;
            try
            {
                var scopeObj = t.GetProperty("Name")?.GetValue(cap)
                    ?? t.GetProperty("ScopeName")?.GetValue(cap)
                    ?? t.GetProperty("Scope")?.GetValue(cap)
                    ?? t.GetProperty("Classification")?.GetValue(cap);
                scope = scopeObj switch
                {
                    null => null,
                    string s => s,
                    _ => Convert.ToString(scopeObj, System.Globalization.CultureInfo.InvariantCulture),
                };
            }
            catch
            {
                scope = null;
            }

            if (string.IsNullOrWhiteSpace(scope))
            {
                continue;
            }

            int groupIndex;
            try
            {
                var val = t.GetProperty("CaptureGroup")?.GetValue(cap)
                    ?? t.GetProperty("Group")?.GetValue(cap)
                    ?? t.GetProperty("GroupIndex")?.GetValue(cap)
                    ?? t.GetProperty("Index")?.GetValue(cap);
                groupIndex = val is int i ? i : Convert.ToInt32(val, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                continue;
            }

            result.Add((groupIndex, scope));
        }

        return result;
    }

    private static void TryLogRegexProbe(string code, ILanguage language)
    {
        try
        {
            var rules = language.Rules;
            if (rules is null || rules.Count == 0)
            {
                return;
            }

            // Probe a few representative patterns (prefer keyword-like patterns).
            var indices = new List<int>();
            for (var i = 0; i < rules.Count && indices.Count < 3; i++)
            {
                var p = rules[i].Regex ?? string.Empty;
                if (p.Contains("\\b(", StringComparison.Ordinal) || p.Contains("public", StringComparison.OrdinalIgnoreCase) || p.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    indices.Add(i);
                }
            }

            if (indices.Count == 0)
            {
                indices.Add(0);
            }

            var timeout = TimeSpan.FromMilliseconds(50);
            foreach (var i in indices)
            {
                var pattern = rules[i].Regex ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                try
                {
                    var r = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline, timeout);
                    var m = r.Match(code);
                    Diag($"RegexProbe: rule[{i}] compile=ok match={(m.Success ? "yes" : "no")} firstIndex={(m.Success ? m.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}");
                }
                catch (Exception ex)
                {
                    Diag($"RegexProbe: rule[{i}] compile=fail {ex.GetType().Name}: {ex.Message}");
                }

                try
                {
                    // Also try Compiled so we can detect AOT limitations.
                    var r2 = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled, timeout);
                    var m2 = r2.Match(code);
                    Diag($"RegexProbe: rule[{i}] compiled=ok match={(m2.Success ? "yes" : "no")} firstIndex={(m2.Success ? m2.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}");
                }
                catch (Exception ex)
                {
                    Diag($"RegexProbe: rule[{i}] compiled=fail {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private sealed class ScopeCapturer : CodeColorizerBase
    {
        public ScopeCapturer(StyleDictionary styles)
            : base(styles, languageParser: null)
        {
            if (DiagnosticsEnabled)
            {
                Diag($"ScopeCapturer: parserType='{languageParser?.GetType().FullName ?? "<null>"}'");
            }
        }

        public IList<Scope> Capture(string code, ILanguage language)
        {
            IList<Scope> captured = Array.Empty<Scope>();
            try
            {
                var sw = DiagnosticsEnabled ? Stopwatch.StartNew() : null;
                languageParser.Parse(code, language, (_, scopes) => captured = scopes);
                if (DiagnosticsEnabled && sw is not null)
                {
                    sw.Stop();
                    Diag($"ScopeCapturer: Parse done in {sw.ElapsedMilliseconds}ms scopes={captured.Count} codeLen={code.Length} lang='{language.Id}'");
                }
            }
            catch (Exception ex)
            {
                Diag($"ScopeCapturer: Parse threw {ex.GetType().Name}: {ex.Message}");
                captured = Array.Empty<Scope>();
            }
            return captured;
        }

        protected override void Write(string parsedSourceCode, IList<Scope> scopes)
        {
            // Not used; Capture uses the parser directly.
        }
    }

    private readonly record struct FlatScope(string Name, int Start, int End, int Depth);

    private static void Flatten(Scope scope, int depth, List<FlatScope> into)
    {
        if (scope.Length > 0)
        {
            into.Add(new FlatScope(scope.Name, scope.Index, scope.Index + scope.Length, depth));
        }

        if (scope.Children is null)
        {
            return;
        }

        for (var i = 0; i < scope.Children.Count; i++)
        {
            Flatten(scope.Children[i], depth + 1, into);
        }
    }

    private static string? NormalizeLanguageId(string fenceInfo)
    {
        // Markdig fenced info is typically: "csharp", "csharp linenums", etc.
        // Other producers may emit things like: "{.language-csharp}" or "language-csharp".
        var firstToken = fenceInfo.Trim();
        if (firstToken.Length == 0)
        {
            return null;
        }

        // If it looks like a Pandoc attribute block, unwrap it first.
        if (firstToken.Length >= 2 && firstToken[0] == '{' && firstToken[^1] == '}')
        {
            firstToken = firstToken[1..^1].Trim();
        }

        // Only keep the first whitespace-separated token.
        var space = firstToken.IndexOfAny([' ', '\t', '\r', '\n']);
        if (space >= 0)
        {
            firstToken = firstToken[..space];
        }

        // Common prefixes/syntax.
        firstToken = firstToken.Trim();
        while (firstToken.StartsWith(".", StringComparison.Ordinal))
        {
            firstToken = firstToken[1..];
        }

        if (firstToken.Length == 0)
        {
            return null;
        }

        if (firstToken.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
        {
            firstToken = firstToken["language-".Length..];
        }
        else if (firstToken.StartsWith("lang-", StringComparison.OrdinalIgnoreCase))
        {
            firstToken = firstToken["lang-".Length..];
        }

        var id = firstToken.Trim().ToLowerInvariant();

        // Treat explicit plain-text fences as "no highlighting".
        if (id is "" or "text" or "txt" or "plain" or "plaintext")
        {
            return null;
        }

        // Prefer ColorCode canonical IDs where known; otherwise return the normalized token.
        return id switch
        {
            "c#" => "c#",
            "cs" => "c#",
            "csharp" => "c#",

            "f#" => "f#",
            "fs" => "f#",
            "fsharp" => "f#",

            "vb" => "vb.net",
            "vbnet" => "vb.net",
            "vb.net" => "vb.net",
            "visualbasic" => "vb.net",
            "visual-basic" => "vb.net",

            "js" => "javascript",
            "javascript" => "javascript",

            "ts" => "typescript",
            "typescript" => "typescript",

            "ps" => "powershell",
            "pwsh" => "powershell",
            "powershell" => "powershell",

            "xaml" => "xaml",
            "axml" => "xml",

            "c++" => "cpp",

            _ => id,
        };
    }

    private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

    private static bool TryParseArgb(string? value, out SKColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var s = value.Trim();
        // Some style sources can contain accidental extra '#', e.g. "#FF#E6E6E6".
        if (s.Contains('#', StringComparison.Ordinal))
        {
            s = s.Replace("#", string.Empty, StringComparison.Ordinal);
        }
        if (s.StartsWith("#", StringComparison.Ordinal))
        {
            s = s[1..];
        }

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        // Support common ColorCode formats:
        // - AARRGGBB
        // - RRGGBB (assume opaque)
        // - ARGB (nibbles)
        // - RGB (nibbles, assume opaque)
        var ok = s.Length switch
        {
            8 => TryParseAarrggbb(s, out color),
            6 => TryParseRrggbb(s, out color),
            4 => TryParseArgbNibbles(s, out color),
            3 => TryParseRgbNibbles(s, out color),
            _ => false,
        };

        if (!ok && DiagnosticsEnabled && LoggedParseFailures.Count < MaxLoggedParseFailures)
        {
            if (LoggedParseFailures.TryAdd(value, 0))
            {
                Diag($"TryParseColor: failed value='{value}'");
            }
        }

        return ok;
    }

    private static bool TryParseAarrggbb(string s, out SKColor color)
    {
        color = default;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var argb))
        {
            return false;
        }

        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        color = new SKColor(r, g, b, a);
        return true;
    }

    private static bool TryParseRrggbb(string s, out SKColor color)
    {
        color = default;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        var r = (byte)((rgb >> 16) & 0xFF);
        var g = (byte)((rgb >> 8) & 0xFF);
        var b = (byte)(rgb & 0xFF);
        color = new SKColor(r, g, b, 0xFF);
        return true;
    }

    private static bool TryParseArgbNibbles(string s, out SKColor color)
    {
        color = default;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var argb))
        {
            return false;
        }

        var a = (byte)(((argb >> 12) & 0xF) * 17);
        var r = (byte)(((argb >> 8) & 0xF) * 17);
        var g = (byte)(((argb >> 4) & 0xF) * 17);
        var b = (byte)((argb & 0xF) * 17);
        color = new SKColor(r, g, b, a);
        return true;
    }

    private static bool TryParseRgbNibbles(string s, out SKColor color)
    {
        color = default;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        var r = (byte)(((rgb >> 8) & 0xF) * 17);
        var g = (byte)(((rgb >> 4) & 0xF) * 17);
        var b = (byte)((rgb & 0xF) * 17);
        color = new SKColor(r, g, b, 0xFF);
        return true;
    }
}
