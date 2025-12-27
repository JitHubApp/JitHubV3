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
            var targetBounds = new RectF(0, y, width, cached.Bounds.Height);
            var rebased = Translate(cached, dx: targetBounds.X - cached.Bounds.X, dy: targetBounds.Y - cached.Bounds.Y);
            y += cached.Bounds.Height + spacingAfter;
            return rebased;
        }

        BlockLayout created = block switch
        {
            ParagraphBlockNode p => LayoutParagraph(p, width, contentWidth, theme, scale, textMeasurer, style, padding, spacingAfter, ref y),
            HeadingBlockNode h => LayoutHeading(h, width, contentWidth, theme, scale, textMeasurer, style, padding, spacingAfter, ref y),
            CodeBlockNode c => LayoutCodeBlock(c, width, contentWidth, theme, scale, textMeasurer, style, padding, spacingAfter, ref y),
            BlockQuoteBlockNode q => LayoutBlockQuote(q, width, contentWidth, theme, themeHash, scale, textMeasurer, style, padding, spacingAfter, ref y),
            ListBlockNode l => LayoutList(l, width, theme, themeHash, scale, textMeasurer, style, padding, spacingAfter, ref y),
            TableBlockNode t => LayoutTable(t, width, theme, themeHash, scale, textMeasurer, style, padding, spacingAfter, ref y),
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

            var run = new InlineRunLayout(
                codeBlock.Id,
                NodeKind.InlineCode,
                codeBlock.Span,
                runBounds,
                textStyle,
                text,
                Url: null,
                IsStrikethrough: false,
                IsCodeBlockLine: true,
                GlyphX: BuildGlyphX(text, textStyle, scale, textMeasurer, runBounds.X, extraLeft: 0));
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

            // Offset child layout inside the quote container.
            childLayout = Translate(childLayout, dx: padding, dy: 0);

            innerBlocks.Add(childLayout);
            localY = childY;
        }

        var contentHeight = Math.Max(0, (localY - (y + padding)));
        var height = (padding * 2) + contentHeight;

        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;

        return new BlockQuoteLayout(quote.Id, quote.Span, bounds, blockStyle, innerBlocks.ToImmutable());
    }

    private ListLayout LayoutList(
        ListBlockNode list,
        float width,
        MarkdownTheme theme,
        int themeHash,
        float scale,
        ITextMeasurer textMeasurer,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        // Phase 4.2.8: lists are a container that lays out items with a marker gutter.
        // Spacing is handled by child blocks; list itself only adds a trailing spacingAfter.
        var markerStyle = theme.Typography.Paragraph;
        var markerGap = Math.Max(4f, theme.Metrics.BlockPadding / 2f) * scale;

        // Compute a uniform marker gutter width so item content aligns.
        var markerTexts = new string[list.Items.Length];
        var markerWidths = new float[list.Items.Length];
        var maxMarkerWidth = 0f;
        for (var i = 0; i < list.Items.Length; i++)
        {
            var item = list.Items[i];

            var markerText = item.IsTask
                ? (item.IsChecked == true ? "☑" : "☐")
                : (list.IsOrdered ? $"{i + 1}." : "•");

            markerTexts[i] = markerText;

            var m = textMeasurer.Measure(markerText, markerStyle, scale);
            var w = Math.Max(0, m.Width);
            markerWidths[i] = w;
            maxMarkerWidth = Math.Max(maxMarkerWidth, w);
        }

        var markerGutterWidth = maxMarkerWidth + markerGap;
        var itemContentWidth = Math.Max(0, width - markerGutterWidth);

        var items = ImmutableArray.CreateBuilder<ListItemLayout>(list.Items.Length);

        var listTop = y;
        var localY = y;

        var fallbackLineHeight = Math.Max(textMeasurer.GetLineHeight(markerStyle, scale), 0);

        for (var i = 0; i < list.Items.Length; i++)
        {
            var item = list.Items[i];
            var itemTop = localY;

            // Lay out item blocks into the content area.
            var childBlocks = ImmutableArray.CreateBuilder<BlockLayout>(item.Blocks.Length);
            var childY = itemTop;
            foreach (var child in item.Blocks)
            {
                var childLayout = LayoutBlock(child, itemContentWidth, theme, themeHash, scale, textMeasurer, ref childY);
                childBlocks.Add(Translate(childLayout, dx: markerGutterWidth, dy: 0));
            }

            localY = childY;

            // Marker aligns to the first available line in the first child block.
            var markerY = itemTop;
            var markerHeight = fallbackLineHeight;

            if (childBlocks.Count > 0)
            {
                var first = childBlocks[0];
                (markerY, markerHeight) = GetFirstLineYAndHeight(first, fallbackLineHeight);
            }

            var markerBounds = new RectF(0, markerY, markerWidths[i], markerHeight);

            var itemBounds = new RectF(0, itemTop, width, Math.Max(0, localY - itemTop));
            items.Add(new ListItemLayout(
                item.Id,
                item.Span,
                itemBounds,
                _styleResolver.ResolveBlockStyle(item, theme),
                MarkerText: markerTexts[i],
                MarkerBounds: markerBounds,
                Blocks: childBlocks.ToImmutable()));
        }

        var bounds = new RectF(0, listTop, width, Math.Max(0, localY - listTop));
        y = localY + spacingAfter;

        return new ListLayout(list.Id, list.Span, bounds, blockStyle, list.IsOrdered, items.ToImmutable());
    }

    private TableLayout LayoutTable(
        TableBlockNode table,
        float width,
        MarkdownTheme theme,
        int themeHash,
        float scale,
        ITextMeasurer textMeasurer,
        MarkdownBlockStyle blockStyle,
        float padding,
        float spacingAfter,
        ref float y)
    {
        // Phase 4.2.10: baseline table layout with equal columns.
        // The table is a container; cell contents are regular blocks laid out within each cell width.
        var tableTop = y;
        var contentWidth = Math.Max(0, width - (padding * 2));

        var colCount = 0;
        for (var i = 0; i < table.Rows.Length; i++)
        {
            colCount = Math.Max(colCount, table.Rows[i].Cells.Length);
        }
        colCount = Math.Max(1, colCount);

        var cellPadding = Math.Max(4f, theme.Metrics.BlockPadding / 2f) * scale;
        var cellWidth = colCount == 0 ? 0 : Math.Max(0, contentWidth / colCount);
        var cellContentWidth = Math.Max(0, cellWidth - (cellPadding * 2));

        var rows = ImmutableArray.CreateBuilder<TableRowLayout>(table.Rows.Length);
        var localY = y + padding;

        for (var r = 0; r < table.Rows.Length; r++)
        {
            var row = table.Rows[r];
            var rowTop = localY;

            var cells = ImmutableArray.CreateBuilder<TableCellLayout>(colCount);
            var rowHeight = 0f;

            for (var c = 0; c < colCount; c++)
            {
                var cellX = padding + (c * cellWidth);
                var cellY = rowTop;

                ImmutableArray<BlockNode> cellBlocks = ImmutableArray<BlockNode>.Empty;
                if (c < row.Cells.Length)
                {
                    cellBlocks = row.Cells[c].Blocks;
                }

                var childLayouts = ImmutableArray.CreateBuilder<BlockLayout>(cellBlocks.Length);
                var childY = cellY + cellPadding;

                for (var bi = 0; bi < cellBlocks.Length; bi++)
                {
                    var child = cellBlocks[bi];
                    var childLayout = LayoutBlock(child, cellContentWidth, theme, themeHash, scale, textMeasurer, ref childY);
                    childLayouts.Add(Translate(childLayout, dx: cellX + cellPadding, dy: 0));
                }

                var cellHeight = Math.Max(0, (childY - cellY)) + cellPadding;
                rowHeight = Math.Max(rowHeight, cellHeight);

                var cellId = c < row.Cells.Length ? row.Cells[c].Id : row.Id;
                var cellSpan = c < row.Cells.Length ? row.Cells[c].Span : row.Span;

                var cellBounds = new RectF(cellX, cellY, cellWidth, cellHeight);
                cells.Add(new TableCellLayout(cellId, cellSpan, cellBounds, childLayouts.ToImmutable()));
            }

            // Normalize all cell heights to the max for the row.
            var normalizedCells = cells.ToImmutable();
            if (rowHeight > 0)
            {
                normalizedCells = normalizedCells
                    .Select(cell => cell with { Bounds = cell.Bounds with { Height = rowHeight } })
                    .ToImmutableArray();
            }

            var rowBounds = new RectF(0, rowTop, width, rowHeight);
            rows.Add(new TableRowLayout(row.Id, row.Span, rowBounds, normalizedCells));
            localY += rowHeight;
        }

        var tableHeight = (localY - (y + padding)) + (padding * 2);
        var bounds = new RectF(0, y, width, Math.Max(0, tableHeight));
        y += bounds.Height + spacingAfter;

        return new TableLayout(table.Id, table.Span, bounds, blockStyle, colCount, rows.ToImmutable());
    }

    private static (float Y, float Height) GetFirstLineYAndHeight(BlockLayout block, float fallbackLineHeight)
        => block switch
        {
            ParagraphLayout p when p.Lines.Length > 0 => (p.Lines[0].Y, p.Lines[0].Height),
            HeadingLayout h when h.Lines.Length > 0 => (h.Lines[0].Y, h.Lines[0].Height),
            CodeBlockLayout c when c.Lines.Length > 0 => (c.Lines[0].Y, c.Lines[0].Height),
            _ => (block.Bounds.Y, fallbackLineHeight),
        };

    private static int ComputeThemeHash(MarkdownTheme theme)
    {
        // Deterministic enough for caching in Phase 3; can evolve to a formal theme hash later.
        var h = new HashCode();

        h.Add(theme.Metrics.CornerRadius);
        h.Add(theme.Metrics.InlineCodeCornerRadius);
        h.Add(theme.Metrics.InlineCodePadding);
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
        var normalized = Translate(layout, dx: -layout.Bounds.X, dy: -layout.Bounds.Y);
        _cache[new BlockCacheKey(block.Id, width, scale, themeHash)] = normalized;
    }

    private static BlockLayout Rebase(BlockLayout layout, RectF bounds)
        => Translate(layout, dx: bounds.X - layout.Bounds.X, dy: bounds.Y - layout.Bounds.Y);

    private static BlockLayout Translate(BlockLayout layout, float dx, float dy)
    {
        RectF Shift(RectF r) => new(r.X + dx, r.Y + dy, r.Width, r.Height);
        LineLayout ShiftLine(LineLayout l)
            => new(l.Y + dy, l.Height, l.Runs.Select(ShiftRun).ToImmutableArray());
        InlineRunLayout ShiftRun(InlineRunLayout r)
            => r with { Bounds = Shift(r.Bounds) };

        return layout switch
        {
            ParagraphLayout p => p with
            {
                Bounds = Shift(p.Bounds),
                Lines = p.Lines.Select(ShiftLine).ToImmutableArray(),
            },

            HeadingLayout h => h with
            {
                Bounds = Shift(h.Bounds),
                Lines = h.Lines.Select(ShiftLine).ToImmutableArray(),
            },

            CodeBlockLayout c => c with
            {
                Bounds = Shift(c.Bounds),
                Lines = c.Lines.Select(ShiftLine).ToImmutableArray(),
            },

            ThematicBreakLayout hr => hr with { Bounds = Shift(hr.Bounds) },
            UnknownBlockLayout u => u with { Bounds = Shift(u.Bounds) },

            BlockQuoteLayout q => q with
            {
                Bounds = Shift(q.Bounds),
                Blocks = q.Blocks.Select(b => Translate(b, dx, dy)).ToImmutableArray(),
            },

            ListLayout l => l with
            {
                Bounds = Shift(l.Bounds),
                Items = l.Items.Select(it => (ListItemLayout)Translate(it, dx, dy)).ToImmutableArray(),
            },

            ListItemLayout li => li with
            {
                Bounds = Shift(li.Bounds),
                MarkerBounds = Shift(li.MarkerBounds),
                Blocks = li.Blocks.Select(b => Translate(b, dx, dy)).ToImmutableArray(),
            },

            TableLayout t => t with
            {
                Bounds = Shift(t.Bounds),
                Rows = t.Rows.Select(r => r with
                {
                    Bounds = Shift(r.Bounds),
                    Cells = r.Cells.Select(c => c with
                    {
                        Bounds = Shift(c.Bounds),
                        Blocks = c.Blocks.Select(b => Translate(b, dx, dy)).ToImmutableArray(),
                    }).ToImmutableArray(),
                }).ToImmutableArray(),
            },

            _ => layout,
        };
    }

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

                // Phase 4.2.11: images reserve a deterministic placeholder surface.
                // They are laid out as a full-width run on their own line.
                if (token.Kind == NodeKind.Image)
                {
                    if (currentRuns.Count > 0)
                    {
                        FlushLine();
                    }

                    var imageHeight = Math.Max(0, theme.Metrics.ImagePlaceholderHeight) * scale;
                    var imageRunBounds = new RectF(paddingLeft, lineY, contentWidth, imageHeight);
                    currentRuns.Add(new InlineRunLayout(
                        token.Id,
                        token.Kind,
                        token.Span,
                        imageRunBounds,
                        token.Style,
                        token.Text,
                        token.Url,
                        token.IsStrikethrough,
                        token.IsCodeBlockLine,
                        GlyphX: default));

                    lineHeight = Math.Max(lineHeight, imageHeight);
                    FlushLine();
                    continue;
                }

                var m = textMeasurer.Measure(token.Text, token.Style, scale);
                var tokenWidth = Math.Max(0, m.Width);
                var tokenHeight = Math.Max(0, m.Height);

                // Phase 4.2.5: inline-code gets a background surface with padding.
                if (token.Kind == NodeKind.InlineCode && !token.IsCodeBlockLine)
                {
                    var inlinePad = Math.Max(0, theme.Metrics.InlineCodePadding) * scale;
                    tokenWidth += inlinePad * 2;
                    tokenHeight += inlinePad * 2;
                }

                var isWhitespace = token.IsWhitespace;

                // Wrap only on non-leading, non-whitespace tokens.
                if (!isWhitespace && currentRuns.Count > 0 && (x - paddingLeft) + tokenWidth > contentWidth)
                {
                    FlushLine();
                }

                var runBounds = new RectF(x, lineY, tokenWidth, tokenHeight);

                var extraLeft = 0f;
                if (token.Kind == NodeKind.InlineCode && !token.IsCodeBlockLine)
                {
                    extraLeft = Math.Max(0, theme.Metrics.InlineCodePadding) * scale;
                }

                currentRuns.Add(new InlineRunLayout(
                    token.Id,
                    token.Kind,
                    token.Span,
                    runBounds,
                    token.Style,
                    token.Text,
                    token.Url,
                    token.IsStrikethrough,
                    token.IsCodeBlockLine,
                    GlyphX: BuildGlyphX(token.Text, token.Style, scale, textMeasurer, runBounds.X, extraLeft)));

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

    private static ImmutableArray<float> BuildGlyphX(string text, MarkdownTextStyle style, float scale, ITextMeasurer measurer, float startX, float extraLeft)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ImmutableArray<float>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<float>(text.Length + 1);
        var x = startX + Math.Max(0, extraLeft);
        builder.Add(x);

        for (var i = 0; i < text.Length; i++)
        {
            // Deterministic, fast-enough approximation: per-char measurement.
            // Kerning/shaping will be handled in a later shaping phase.
            var w = measurer.Measure(text[i].ToString(), style, scale).Width;
            x += Math.Max(0, w);
            builder.Add(x);
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<InlineSegment> FlattenInlineSegments(ImmutableArray<InlineNode> inlines, MarkdownTextStyle baseStyle, MarkdownTheme theme)
    {
        var builder = ImmutableArray.CreateBuilder<InlineSegment>();
        FlattenInto(inlines, baseStyle, url: null, isStrikethrough: false);
        return builder.ToImmutable();

        void FlattenInto(ImmutableArray<InlineNode> nodes, MarkdownTextStyle current, string? url, bool isStrikethrough)
        {
            foreach (var n in nodes)
            {
                switch (n)
                {
                    case TextInlineNode t:
                        builder.Add(new InlineSegment(
                            Id: t.Id,
                            Kind: url is null ? t.Kind : NodeKind.Link,
                            Span: t.Span,
                            Style: current,
                            Text: t.Text,
                            Url: url,
                            IsStrikethrough: isStrikethrough,
                            IsCodeBlockLine: false));
                        break;

                    case InlineCodeNode code:
                        builder.Add(new InlineSegment(code.Id, code.Kind, code.Span, theme.Typography.InlineCode, code.Code, Url: null, IsStrikethrough: isStrikethrough, IsCodeBlockLine: false));
                        break;

                    case LineBreakInlineNode br:
                        builder.Add(new InlineSegment(br.Id, br.Kind, br.Span, current, "\n", Url: null, IsStrikethrough: isStrikethrough, IsCodeBlockLine: false));
                        break;

                    case EmphasisInlineNode e:
                        FlattenInto(e.Inlines, current.With(italic: true), url, isStrikethrough);
                        break;

                    case StrongInlineNode s:
                        FlattenInto(s.Inlines, current.With(weight: FontWeight.Bold), url, isStrikethrough);
                        break;

                    case StrikethroughInlineNode st:
                        FlattenInto(st.Inlines, current, url, isStrikethrough: true);
                        break;

                    case LinkInlineNode link:
                        var linkStyle = current.With(
                            foreground: theme.Typography.Link.Foreground,
                            underline: theme.Typography.Link.Underline);
                        FlattenInto(link.Inlines, linkStyle, url: link.Url, isStrikethrough);
                        break;

                    case ImageInlineNode img:
                        // Phase 4.2.11: represent images as a dedicated run with URL metadata.
                        // Layout will allocate placeholder bounds; renderer draws placeholder or resolved image.
                        builder.Add(new InlineSegment(
                            Id: img.Id,
                            Kind: NodeKind.Image,
                            Span: img.Span,
                            Style: current,
                            Text: "\uFFFC",
                            Url: img.Url,
                            IsStrikethrough: false,
                            IsCodeBlockLine: false));
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
            yield return new Token(segment.Id, segment.Kind, segment.Span, segment.Style, string.Empty, segment.Url, segment.IsStrikethrough, segment.IsCodeBlockLine, IsWhitespace: true);
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

            yield return new Token(segment.Id, segment.Kind, span, segment.Style, text, segment.Url, segment.IsStrikethrough, segment.IsCodeBlockLine, IsWhitespace: isSpace);
            start = end;
        }
    }

    private readonly record struct InlineSegment(NodeId Id, NodeKind Kind, SourceSpan Span, MarkdownTextStyle Style, string Text, string? Url, bool IsStrikethrough, bool IsCodeBlockLine);

    private readonly record struct Token(NodeId Id, NodeKind Kind, SourceSpan Span, MarkdownTextStyle Style, string Text, string? Url, bool IsStrikethrough, bool IsCodeBlockLine, bool IsWhitespace);
}
