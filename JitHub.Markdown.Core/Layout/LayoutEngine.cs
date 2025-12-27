using System.Collections.Immutable;

namespace JitHub.Markdown;

public sealed class MarkdownLayoutEngine
{
    private readonly IMarkdownStyleResolver _styleResolver;
    private readonly Dictionary<BlockCacheKey, BlockLayout> _cache = new();

    public MarkdownLayoutEngine(IMarkdownStyleResolver? styleResolver = null)
    {
        _styleResolver = styleResolver ?? new MarkdownStyleResolver();
    }

    public MarkdownLayout Layout(MarkdownDocumentModel document, float width, MarkdownTheme theme, float scale, ITextMeasurer textMeasurer)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));

        var themeHash = ComputeThemeHash(theme);

        var blocks = ImmutableArray.CreateBuilder<BlockLayout>(document.Blocks.Length);
        var y = 0f;

        foreach (var block in document.Blocks)
        {
            var layout = LayoutBlock(block, width, theme, themeHash, scale, textMeasurer, ref y);
            blocks.Add(layout);
        }

        return new MarkdownLayout
        {
            Width = width,
            Height = Math.Max(0, y),
            Blocks = blocks.ToImmutable(),
        };
    }

    public MarkdownLayout LayoutViewport(
        MarkdownDocumentModel document,
        float width,
        MarkdownTheme theme,
        float scale,
        ITextMeasurer textMeasurer,
        float viewportTop,
        float viewportHeight,
        float overscan = 0)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (viewportHeight < 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        if (overscan < 0) throw new ArgumentOutOfRangeException(nameof(overscan));

        var themeHash = ComputeThemeHash(theme);
        var view = new RectF(0, viewportTop - overscan, width, viewportHeight + (overscan * 2));
        var viewBottom = view.Bottom;

        var blocks = ImmutableArray.CreateBuilder<BlockLayout>();
        var y = 0f;

        foreach (var block in document.Blocks)
        {
            var layout = LayoutBlock(block, width, theme, themeHash, scale, textMeasurer, ref y);

            if (layout.Bounds.IntersectsWith(view))
            {
                blocks.Add(layout);
            }

            // Basic virtualization: once we pass the viewport bottom, stop.
            if (layout.Bounds.Y > viewBottom)
            {
                break;
            }
        }

        return new MarkdownLayout
        {
            Width = width,
            Height = Math.Max(0, y),
            Blocks = blocks.ToImmutable(),
        };
    }

    private BlockLayout LayoutBlock(BlockNode block, float width, MarkdownTheme theme, int themeHash, float scale, ITextMeasurer textMeasurer, ref float y)
    {
        var style = _styleResolver.ResolveBlockStyle(block, theme);
        var padding = Math.Max(0, style.Padding) * scale;
        var spacingAfter = Math.Max(0, style.SpacingAfter) * scale;

        var contentWidth = Math.Max(0, width - (padding * 2));

        // Simple caching for the common, non-nested blocks.
        if (TryGetCached(block, width, scale, themeHash, out var cached))
        {
            var bounds = new RectF(0, y, width, cached.Bounds.Height);
            var rebased = Rebase(cached, bounds);
            y += cached.Bounds.Height + spacingAfter;
            return rebased;
        }

        BlockLayout created = block switch
        {
            ParagraphBlockNode p => LayoutParagraph(p, width, contentWidth, theme, scale, textMeasurer, style, padding, spacingAfter, ref y),
            HeadingBlockNode h => LayoutHeading(h, width, contentWidth, theme, scale, textMeasurer, style, padding, spacingAfter, ref y),
            CodeBlockNode c => LayoutCodeBlock(c, width, contentWidth, theme, scale, textMeasurer, style, padding, spacingAfter, ref y),
            BlockQuoteBlockNode q => LayoutBlockQuote(q, width, contentWidth, theme, themeHash, scale, textMeasurer, style, padding, spacingAfter, ref y),
            ThematicBreakBlockNode hr => LayoutThematicBreak(hr, width, theme, scale, style, padding, spacingAfter, ref y),
            _ => LayoutUnknown(block, width, theme, scale, style, padding, spacingAfter, ref y),
        };

        CacheIfSupported(block, created, width, scale, themeHash);
        return created;
    }

    private ParagraphLayout LayoutParagraph(
        ParagraphBlockNode paragraph,
        float width,
        float contentWidth,
        MarkdownTheme theme,
        float scale,
        ITextMeasurer textMeasurer,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        var baseTextStyle = theme.Typography.Paragraph;
        var lines = LayoutInlines(paragraph.Inlines, baseTextStyle, contentWidth, theme, scale, textMeasurer, paddingLeft: padding, yTop: y);

        var contentHeight = lines.Sum(l => l.Height);
        var height = (padding * 2) + contentHeight;

        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;

        return new ParagraphLayout(paragraph.Id, paragraph.Span, bounds, blockStyle, lines);
    }

    private HeadingLayout LayoutHeading(
        HeadingBlockNode heading,
        float width,
        float contentWidth,
        MarkdownTheme theme,
        float scale,
        ITextMeasurer textMeasurer,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        var baseTextStyle = heading.Level switch
        {
            1 => theme.Typography.Heading1,
            2 => theme.Typography.Heading2,
            3 => theme.Typography.Heading3,
            4 => theme.Typography.Heading4,
            5 => theme.Typography.Heading5,
            _ => theme.Typography.Heading6,
        };

        var lines = LayoutInlines(heading.Inlines, baseTextStyle, contentWidth, theme, scale, textMeasurer, paddingLeft: padding, yTop: y);

        var contentHeight = lines.Sum(l => l.Height);
        var height = (padding * 2) + contentHeight;

        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;

        return new HeadingLayout(heading.Id, heading.Span, bounds, blockStyle, heading.Level, lines);
    }

    private CodeBlockLayout LayoutCodeBlock(
        CodeBlockNode codeBlock,
        float width,
        float contentWidth,
        MarkdownTheme theme,
        float scale,
        ITextMeasurer textMeasurer,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        var textStyle = theme.Typography.InlineCode;
        var lineHeight = Math.Max(textMeasurer.GetLineHeight(textStyle, scale), 0);

        var linesText = (codeBlock.Code ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n');

        var runsBuilder = ImmutableArray.CreateBuilder<LineLayout>(linesText.Length);

        var localY = y + padding;
        for (var i = 0; i < linesText.Length; i++)
        {
            var text = linesText[i];
            var m = textMeasurer.Measure(text, textStyle, scale);
            var runBounds = new RectF(padding, localY, Math.Min(m.Width, contentWidth), lineHeight);

            var run = new InlineRunLayout(codeBlock.Id, NodeKind.InlineCode, codeBlock.Span, runBounds, textStyle, text);
            runsBuilder.Add(new LineLayout(localY, lineHeight, ImmutableArray.Create(run)));

            localY += lineHeight;
        }

        var contentHeight = Math.Max(0, runsBuilder.Count * lineHeight);
        var height = (padding * 2) + contentHeight;

        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;

        return new CodeBlockLayout(codeBlock.Id, codeBlock.Span, bounds, blockStyle, runsBuilder.ToImmutable());
    }

    private BlockQuoteLayout LayoutBlockQuote(
        BlockQuoteBlockNode quote,
        float width,
        float contentWidth,
        MarkdownTheme theme,
        int themeHash,
        float scale,
        ITextMeasurer textMeasurer,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        // For Phase 3, blockquotes are a styled container that lays out its child blocks.
        var innerBlocks = ImmutableArray.CreateBuilder<BlockLayout>(quote.Blocks.Length);

        var localY = y + padding;
        foreach (var child in quote.Blocks)
        {
            var childY = localY;
            var childLayout = LayoutBlock(child, contentWidth, theme, themeHash, scale, textMeasurer, ref childY);

            // Rebase child bounds inside the quote container.
            childLayout = childLayout with
            {
                Bounds = new RectF(padding + childLayout.Bounds.X, childLayout.Bounds.Y, childLayout.Bounds.Width, childLayout.Bounds.Height)
            };

            innerBlocks.Add(childLayout);
            localY = childY;
        }

        var contentHeight = Math.Max(0, (localY - (y + padding)));
        var height = (padding * 2) + contentHeight;

        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;

        return new BlockQuoteLayout(quote.Id, quote.Span, bounds, blockStyle, innerBlocks.ToImmutable());
    }

    private static int ComputeThemeHash(MarkdownTheme theme)
    {
        // Deterministic enough for caching in Phase 3; can evolve to a formal theme hash later.
        var h = new HashCode();

        h.Add(theme.Metrics.CornerRadius);
        h.Add(theme.Metrics.BlockPadding);
        h.Add(theme.Metrics.BlockSpacing);

        AddStyle(h, theme.Typography.Paragraph);
        AddStyle(h, theme.Typography.InlineCode);
        AddStyle(h, theme.Typography.Link);
        AddStyle(h, theme.Typography.Heading1);
        AddStyle(h, theme.Typography.Heading2);
        AddStyle(h, theme.Typography.Heading3);
        AddStyle(h, theme.Typography.Heading4);
        AddStyle(h, theme.Typography.Heading5);
        AddStyle(h, theme.Typography.Heading6);

        return h.ToHashCode();

        static void AddStyle(HashCode hc, MarkdownTextStyle s)
        {
            hc.Add(s.FontFamily);
            hc.Add(s.FontSize);
            hc.Add((int)s.Weight);
            hc.Add(s.Italic);
            hc.Add(s.Underline);
            hc.Add(s.Foreground);
        }
    }

    private bool TryGetCached(BlockNode block, float width, float scale, int themeHash, out BlockLayout layout)
    {
        // Do not cache nested blocks yet (BlockQuote) to avoid complex child rebasing.
        if (block.Kind is NodeKind.BlockQuote or NodeKind.List or NodeKind.ListItem or NodeKind.Table)
        {
            layout = default!;
            return false;
        }

        var key = new BlockCacheKey(block.Id, width, scale, themeHash);
        return _cache.TryGetValue(key, out layout!);
    }

    private void CacheIfSupported(BlockNode block, BlockLayout layout, float width, float scale, int themeHash)
    {
        if (block.Kind is NodeKind.BlockQuote or NodeKind.List or NodeKind.ListItem or NodeKind.Table)
        {
            return;
        }

        // Store cached version normalized to Y=0.
        var normalized = Rebase(layout, layout.Bounds with { Y = 0 });
        _cache[new BlockCacheKey(block.Id, width, scale, themeHash)] = normalized;
    }

    private static BlockLayout Rebase(BlockLayout layout, RectF bounds)
        => layout switch
        {
            ParagraphLayout p => p with { Bounds = bounds },
            HeadingLayout h => h with { Bounds = bounds },
            CodeBlockLayout c => c with { Bounds = bounds },
            ThematicBreakLayout hr => hr with { Bounds = bounds },
            UnknownBlockLayout u => u with { Bounds = bounds },
            BlockQuoteLayout q => q with { Bounds = bounds },
            _ => layout,
        };

    private readonly record struct BlockCacheKey(NodeId Id, float Width, float Scale, int ThemeHash);

    private ThematicBreakLayout LayoutThematicBreak(
        ThematicBreakBlockNode hr,
        float width,
        MarkdownTheme theme,
        float scale,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        var height = Math.Max(1f * scale, 0);
        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;
        return new ThematicBreakLayout(hr.Id, hr.Span, bounds, blockStyle);
    }

    private UnknownBlockLayout LayoutUnknown(
        BlockNode block,
        float width,
        MarkdownTheme theme,
        float scale,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        var height = Math.Max(theme.Metrics.BlockSpacing * scale, 0);
        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;
        return new UnknownBlockLayout(block.Id, block.Kind, block.Span, bounds, blockStyle);
    }

    private ImmutableArray<LineLayout> LayoutInlines(
        ImmutableArray<InlineNode> inlines,
        MarkdownTextStyle baseTextStyle,
        float contentWidth,
        MarkdownTheme theme,
        float scale,
        ITextMeasurer textMeasurer,
        float paddingLeft,
        float yTop)
    {
        var segments = FlattenInlineSegments(inlines, baseTextStyle, theme);

        var lines = ImmutableArray.CreateBuilder<LineLayout>();
        var currentRuns = ImmutableArray.CreateBuilder<InlineRunLayout>();

        var x = paddingLeft;
        var lineY = yTop + (paddingLeft); // reuse padding scale in Y direction for baseline simplicity
        var lineHeight = Math.Max(textMeasurer.GetLineHeight(baseTextStyle, scale), 0);

        void FlushLine()
        {
            lines.Add(new LineLayout(lineY, lineHeight, currentRuns.ToImmutable()));
            currentRuns.Clear();
            x = paddingLeft;
            lineY += lineHeight;
            lineHeight = Math.Max(textMeasurer.GetLineHeight(baseTextStyle, scale), 0);
        }

        foreach (var segment in segments)
        {
            foreach (var token in Tokenize(segment))
            {
                if (token.Text.Length == 0)
                {
                    continue;
                }

                var m = textMeasurer.Measure(token.Text, token.Style, scale);
                var tokenWidth = Math.Max(0, m.Width);
                var tokenHeight = Math.Max(0, m.Height);

                var isWhitespace = token.IsWhitespace;

                // Wrap only on non-leading, non-whitespace tokens.
                if (!isWhitespace && currentRuns.Count > 0 && (x - paddingLeft) + tokenWidth > contentWidth)
                {
                    FlushLine();
                }

                var runBounds = new RectF(x, lineY, tokenWidth, tokenHeight);
                currentRuns.Add(new InlineRunLayout(token.Id, token.Kind, token.Span, runBounds, token.Style, token.Text));

                x += tokenWidth;
                lineHeight = Math.Max(lineHeight, tokenHeight);
            }
        }

        if (currentRuns.Count == 0)
        {
            // Always produce at least one line for consistency.
            lines.Add(new LineLayout(lineY, lineHeight, ImmutableArray<InlineRunLayout>.Empty));
        }
        else
        {
            FlushLine();
        }

        return lines.ToImmutable();
    }

    private ImmutableArray<InlineSegment> FlattenInlineSegments(ImmutableArray<InlineNode> inlines, MarkdownTextStyle baseStyle, MarkdownTheme theme)
    {
        var builder = ImmutableArray.CreateBuilder<InlineSegment>();
        FlattenInto(inlines, baseStyle);
        return builder.ToImmutable();

        void FlattenInto(ImmutableArray<InlineNode> nodes, MarkdownTextStyle current)
        {
            foreach (var n in nodes)
            {
                switch (n)
                {
                    case TextInlineNode t:
                        builder.Add(new InlineSegment(t.Id, t.Kind, t.Span, current, t.Text));
                        break;

                    case InlineCodeNode code:
                        builder.Add(new InlineSegment(code.Id, code.Kind, code.Span, theme.Typography.InlineCode, code.Code));
                        break;

                    case LineBreakInlineNode br:
                        builder.Add(new InlineSegment(br.Id, br.Kind, br.Span, current, "\n"));
                        break;

                    case EmphasisInlineNode e:
                        FlattenInto(e.Inlines, current.With(italic: true));
                        break;

                    case StrongInlineNode s:
                        FlattenInto(s.Inlines, current.With(weight: FontWeight.Bold));
                        break;

                    case StrikethroughInlineNode st:
                        FlattenInto(st.Inlines, current);
                        break;

                    case LinkInlineNode link:
                        var linkStyle = current.With(
                            foreground: theme.Typography.Link.Foreground,
                            underline: theme.Typography.Link.Underline);
                        FlattenInto(link.Inlines, linkStyle);
                        break;

                    case ImageInlineNode img:
                        // Phase 3: treat image as its alt text for layout measurement.
                        FlattenInto(img.AltText, current);
                        break;

                    default:
                        break;
                }
            }
        }
    }

    private static IEnumerable<Token> Tokenize(InlineSegment segment)
    {
        var s = segment.Text ?? string.Empty;

        if (s == "\n")
        {
            // Explicit hard break.
            yield return new Token(segment.Id, segment.Kind, segment.Span, segment.Style, string.Empty, IsWhitespace: true);
            yield break;
        }

        var start = 0;
        while (start < s.Length)
        {
            var isSpace = char.IsWhiteSpace(s[start]);
            var end = start + 1;
            while (end < s.Length && char.IsWhiteSpace(s[end]) == isSpace)
            {
                end++;
            }

            var text = s.Substring(start, end - start);
            var span = new SourceSpan(segment.Span.Start + start, segment.Span.Start + end);

            yield return new Token(segment.Id, segment.Kind, span, segment.Style, text, IsWhitespace: isSpace);
            start = end;
        }
    }

    private readonly record struct InlineSegment(NodeId Id, NodeKind Kind, SourceSpan Span, MarkdownTextStyle Style, string Text);

    private readonly record struct Token(NodeId Id, NodeKind Kind, SourceSpan Span, MarkdownTextStyle Style, string Text, bool IsWhitespace);
}
