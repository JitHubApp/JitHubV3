using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Diagnostics;

namespace JitHub.Markdown;

public sealed class MarkdownLayoutEngine
{
    private readonly IMarkdownStyleResolver _styleResolver;
    private readonly Dictionary<BlockCacheKey, BlockLayout> _cache = new();

    /// <summary>
    /// Base direction used when a block contains no strong directional characters.
    /// This is used as a fallback only; strong RTL/LTR characters in content take precedence.
    /// </summary>
    public bool DefaultIsRtl { get; set; }

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
        var nodeTextOffset = 0;
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
                GlyphX: BuildGlyphX(text, textStyle, scale, textMeasurer, runBounds.X, extraLeft: 0, isRightToLeft: false),
                NodeTextOffset: nodeTextOffset);
            runsBuilder.Add(new LineLayout(localY, lineHeight, ImmutableArray.Create(run)));

            localY += lineHeight;

            // Offset into the logical code content (not including fences).
            // Lines were split on '\n', so add 1 for the delimiter between lines.
            nodeTextOffset += text.Length + 1;
        }

        var contentHeight = Math.Max(0, runsBuilder.Count * lineHeight);
        var height = (padding * 2) + contentHeight;

        var bounds = new RectF(0, y, width, height);
        y += height + spacingAfter;

        return new CodeBlockLayout(codeBlock.Id, codeBlock.Span, bounds, blockStyle, runsBuilder.ToImmutable(), codeBlock.Info);
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

        // Phase 7.3 (RTL): when content is RTL (or base direction is RTL for neutral content), place marker gutter on the right.
        var isRtl = DetermineIsRtl(list.Items.SelectMany(static it => it.Blocks));

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
                childBlocks.Add(Translate(childLayout, dx: isRtl ? 0 : markerGutterWidth, dy: 0));
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

            var markerX = isRtl ? Math.Max(0, width - markerWidths[i]) : 0;
            var markerBounds = new RectF(markerX, markerY, markerWidths[i], markerHeight);

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

        var isRtl = DetermineIsRtl(table.Rows.SelectMany(static r => r.Cells).SelectMany(static c => c.Blocks));

        for (var r = 0; r < table.Rows.Length; r++)
        {
            var row = table.Rows[r];
            var rowTop = localY;

            var cells = ImmutableArray.CreateBuilder<TableCellLayout>(colCount);
            var rowHeight = 0f;

            for (var c = 0; c < colCount; c++)
            {
                var visualCol = isRtl ? (colCount - 1 - c) : c;
                var cellX = padding + (visualCol * cellWidth);
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

        // Colors affect rendered output even when typography stays identical.
        // Include them to avoid stale cached layouts (e.g., code block / quote backgrounds).
        h.Add(theme.Colors.PageBackground);
        h.Add(theme.Colors.InlineCodeBackground);
        h.Add(theme.Colors.CodeBlockBackground);
        h.Add(theme.Colors.QuoteBackground);
        h.Add(theme.Colors.ThematicBreak);

        // Selection impacts readability and some render overlays.
        h.Add(theme.Selection.SelectionFill);
        h.Add(theme.Selection.SelectionText);

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
        {
            var gx = r.GlyphX;
            if (dx != 0 && !gx.IsDefault && gx.Length > 0)
            {
                var builder = ImmutableArray.CreateBuilder<float>(gx.Length);
                for (var i = 0; i < gx.Length; i++)
                {
                    builder.Add(gx[i] + dx);
                }
                gx = builder.ToImmutable();
            }

            var shifted = r with { Bounds = Shift(r.Bounds), GlyphX = gx };
#if DEBUG
            ValidateRunGeometry(shifted);
#endif
            return shifted;
        }

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

#if DEBUG
    private static void ValidateRunGeometry(InlineRunLayout run)
    {
        var gx = run.GlyphX;
        if (gx.IsDefault || gx.Length == 0)
        {
            return;
        }

        for (var i = 0; i < gx.Length - 1; i++)
        {
            Debug.Assert(gx[i] <= gx[i + 1], "GlyphX must be monotonic in visual X order.");
        }

        // GlyphX values are absolute (layout-space) caret boundaries. They should overlap the run bounds.
        // Note: Inline code can start after Bounds.X due to internal padding; allow that.
        var left = run.Bounds.X;
        var right = run.Bounds.Right;
        var min = gx[0];
        var max = gx[gx.Length - 1];
        const float slop = 8f;

        Debug.Assert(max >= left - slop, "GlyphX end must not be far left of the run bounds.");
        Debug.Assert(min <= right + slop, "GlyphX start must not be far right of the run bounds.");
    }
#endif

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

        // Phase 7.3 (RTL): paragraph-level direction detection.
        // This is a minimal heuristic (first strong directional character). It aligns the laid-out
        // lines to the right edge for RTL content, but does not implement full Unicode BiDi shaping.
        var isRtl = DetermineIsRtl(segments.Select(static s => s.Text));

        var lines = ImmutableArray.CreateBuilder<LineLayout>();
        var pending = new List<PendingRun>(64);

        var x = paddingLeft;
        var lineTop = yTop;

        var metricsMeasurer = textMeasurer as ITextMeasurerWithFontMetrics;
        var minBaseHeight = Math.Max(0, textMeasurer.GetLineHeight(baseTextStyle, scale));

        var baseMetrics = metricsMeasurer is not null
            ? metricsMeasurer.GetFontMetrics(baseTextStyle, scale)
            : default;

        var baseAscentFallback = Math.Max(0, minBaseHeight * 0.8f);
        var baseDescentFallback = Math.Max(0, minBaseHeight - baseAscentFallback);

        var lineAscent = metricsMeasurer is not null ? Math.Max(0, baseMetrics.Ascent) : baseAscentFallback;
        var lineDescent = metricsMeasurer is not null ? Math.Max(0, baseMetrics.Descent) : baseDescentFallback;

        bool GetTokenIsRtl(Token token)
        {
            if (TryGetFirstStrongDirection(token.Text, out var rtl))
            {
                return rtl;
            }

            return isRtl;
        }

        void FlushLine()
        {
            var lineHeight = Math.Max(0, lineAscent + lineDescent);
            if (lineHeight < minBaseHeight)
            {
                // Preserve the base line height as a lower bound.
                lineDescent += (minBaseHeight - lineHeight);
                lineHeight = minBaseHeight;
            }

            var baselineY = lineTop + lineAscent;

            var runBuilder = ImmutableArray.CreateBuilder<InlineRunLayout>(pending.Count);
            for (var i = 0; i < pending.Count; i++)
            {
                var r = pending[i];
                var runHeight = Math.Max(0, r.Ascent + r.Descent);
                var y = baselineY - r.Ascent;
                var bounds = new RectF(r.X, y, r.Width, runHeight);

                runBuilder.Add(new InlineRunLayout(
                    r.Id,
                    r.Kind,
                    r.Span,
                    bounds,
                    r.Style,
                    r.Text,
                    r.Url,
                    r.IsStrikethrough,
                    r.IsCodeBlockLine,
                    GlyphX: BuildGlyphX(r.Text, r.Style, scale, textMeasurer, startX: bounds.X, extraLeft: r.ExtraLeft, isRightToLeft: r.IsRightToLeft),
                    IsRightToLeft: r.IsRightToLeft));
            }

            var runs = runBuilder.ToImmutable();
            if (runs.Length > 0)
            {
                runs = RelayoutBidiRuns(runs, isRtl, paddingLeft, contentWidth, theme, scale, textMeasurer);
            }

            lines.Add(new LineLayout(lineTop, lineHeight, runs));

            pending.Clear();
            x = paddingLeft;
            lineTop += lineHeight;

            // Reset line metrics.
            lineAscent = metricsMeasurer is not null ? Math.Max(0, baseMetrics.Ascent) : baseAscentFallback;
            lineDescent = metricsMeasurer is not null ? Math.Max(0, baseMetrics.Descent) : baseDescentFallback;
        }

        static (float ascent, float descent) MeasureAscentDescent(
            ITextMeasurer measurer,
            ITextMeasurerWithFontMetrics? metricsMeasurer,
            MarkdownTextStyle style,
            float scale,
            float measuredHeight)
        {
            if (metricsMeasurer is not null)
            {
                var fm = metricsMeasurer.GetFontMetrics(style, scale);
                var a = Math.Max(0, fm.Ascent);
                var d = Math.Max(0, fm.Descent);
                if (a + d > 0)
                {
                    return (a, d);
                }
            }

            var h = Math.Max(0, measuredHeight);
            var ascent = Math.Max(0, h * 0.8f);
            var descent = Math.Max(0, h - ascent);
            return (ascent, descent);
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
                    if (pending.Count > 0)
                    {
                        FlushLine();
                    }

                    var imageHeight = Math.Max(0, theme.Metrics.ImagePlaceholderHeight) * scale;
                    var imageRunBounds = new RectF(paddingLeft, lineTop, contentWidth, imageHeight);
                    var imageRun = new InlineRunLayout(
                        token.Id,
                        token.Kind,
                        token.Span,
                        imageRunBounds,
                        token.Style,
                        token.Text,
                        token.Url,
                        token.IsStrikethrough,
                        token.IsCodeBlockLine,
                        GlyphX: default,
                        IsRightToLeft: isRtl);

                    lines.Add(new LineLayout(lineTop, imageHeight, ImmutableArray.Create(imageRun)));
                    lineTop += imageHeight;

                    // Reset line metrics after the image line.
                    x = paddingLeft;
                    lineAscent = metricsMeasurer is not null ? Math.Max(0, baseMetrics.Ascent) : baseAscentFallback;
                    lineDescent = metricsMeasurer is not null ? Math.Max(0, baseMetrics.Descent) : baseDescentFallback;
                    continue;
                }

                var m = textMeasurer.Measure(token.Text, token.Style, scale);
                var tokenWidth = Math.Max(0, m.Width);

                var (runAscent, runDescent) = MeasureAscentDescent(textMeasurer, metricsMeasurer, token.Style, scale, m.Height);

                var extraLeft = 0f;
                if (token.Kind == NodeKind.InlineCode && !token.IsCodeBlockLine)
                {
                    extraLeft = Math.Max(0, theme.Metrics.InlineCodePadding) * scale;
                    tokenWidth += extraLeft * 2;
                    runAscent += extraLeft;
                    runDescent += extraLeft;
                }

                var isWhitespace = token.IsWhitespace;

                // Wrap only on non-leading, non-whitespace tokens.
                if (!isWhitespace && pending.Count > 0 && (x - paddingLeft) + tokenWidth > contentWidth)
                {
                    FlushLine();
                }

                var runIsRtl = GetTokenIsRtl(token);

                pending.Add(new PendingRun(
                    Id: token.Id,
                    Kind: token.Kind,
                    Span: token.Span,
                    Style: token.Style,
                    Text: token.Text,
                    Url: token.Url,
                    IsStrikethrough: token.IsStrikethrough,
                    IsCodeBlockLine: token.IsCodeBlockLine,
                    X: x,
                    Width: tokenWidth,
                    Ascent: runAscent,
                    Descent: runDescent,
                    ExtraLeft: extraLeft,
                    IsRightToLeft: runIsRtl));

                x += tokenWidth;

                lineAscent = Math.Max(lineAscent, runAscent);
                lineDescent = Math.Max(lineDescent, runDescent);
            }
        }

        if (pending.Count == 0)
        {
            // Always produce at least one line for consistency.
            var lineHeight = Math.Max(0, lineAscent + lineDescent);
            if (lineHeight < minBaseHeight)
            {
                lineHeight = minBaseHeight;
            }
            lines.Add(new LineLayout(lineTop, lineHeight, ImmutableArray<InlineRunLayout>.Empty));
        }
        else
        {
            FlushLine();
        }

        return lines.ToImmutable();
    }

    private readonly record struct PendingRun(
        NodeId Id,
        NodeKind Kind,
        SourceSpan Span,
        MarkdownTextStyle Style,
        string Text,
        string? Url,
        bool IsStrikethrough,
        bool IsCodeBlockLine,
        float X,
        float Width,
        float Ascent,
        float Descent,
        float ExtraLeft,
        bool IsRightToLeft);

    private static ImmutableArray<InlineRunLayout> RelayoutBidiRuns(
        ImmutableArray<InlineRunLayout> runs,
        bool baseIsRtl,
        float paddingLeft,
        float contentWidth,
        MarkdownTheme theme,
        float scale,
        ITextMeasurer textMeasurer)
    {
        if (runs.Length == 0)
        {
            return runs;
        }

        // Images are already laid out as full-width runs and should not be bidi-reflowed.
        if (runs.All(static r => r.Kind == NodeKind.Image))
        {
            return runs;
        }

        // If a line contains an image alongside text, do not attempt BiDi splitting/reordering.
        // Images are laid out as dedicated full-width placeholder runs and are expected to be isolated.
        if (runs.Any(static r => r.Kind == NodeKind.Image))
        {
            return runs;
        }

        var contentLeft = paddingLeft;
        var contentRight = paddingLeft + Math.Max(0, contentWidth);

        var lineText = string.Concat(runs.Select(static r => r.Text ?? string.Empty));
        if (lineText.Length == 0)
        {
            return runs;
        }

        // Map each UTF-16 index in the concatenated line text back to a source run + offset.
        var runAt = new int[lineText.Length];
        var offsetAt = new int[lineText.Length];
        var inlineCodeRun = new bool[runs.Length];

        var pos = 0;
        for (var r = 0; r < runs.Length; r++)
        {
            inlineCodeRun[r] = runs[r].Kind == NodeKind.InlineCode && !runs[r].IsCodeBlockLine;

            var t = runs[r].Text ?? string.Empty;
            for (var i = 0; i < t.Length; i++)
            {
                if (pos >= runAt.Length)
                {
                    break;
                }

                runAt[pos] = r;
                offsetAt[pos] = i;
                pos++;
            }
        }

        // Compute BiDi classes.
        var baseLevel = baseIsRtl ? 1 : 0;
        var classes = new BidiClass[lineText.Length];
        for (var i = 0; i < lineText.Length; i++)
        {
            var ch = lineText[i];

            // Handle surrogate pairs (basic classification).
            if (char.IsHighSurrogate(ch) && i + 1 < lineText.Length && char.IsLowSurrogate(lineText[i + 1]))
            {
                var rune = new Rune(ch, lineText[i + 1]);
                classes[i] = Classify(rune.Value, ch);
                classes[i + 1] = BidiClass.BN;
                i++;
            }
            else
            {
                classes[i] = Classify(ch);
            }

            // Inline code spans include delimiters in source; keep them unbroken and treat their content as a single strong direction.
            var r = runAt[i];
            if (r >= 0 && r < runs.Length && inlineCodeRun[r])
            {
                classes[i] = runs[r].IsRightToLeft ? BidiClass.R : BidiClass.L;
            }
        }

        // Resolve types + levels (UBA subset sufficient for mixed-direction text).
        ResolveWeakAndNeutralTypes(classes, baseLevel);
        var levels = ComputeImplicitLevels(classes, baseLevel);

        // Reorder by levels (UAX#9 L2).
        var order = ComputeVisualOrder(levels);

        // Build visual runs by grouping contiguous characters that belong to the same source run.
        var builder = ImmutableArray.CreateBuilder<InlineRunLayout>();

        var cursorX = contentLeft;

        var consumedInlineCode = new bool[runs.Length];

        var pIndex = 0;
        while (pIndex < order.Length)
        {
            var logicalIndex = order[pIndex];
            if ((uint)logicalIndex >= (uint)lineText.Length)
            {
                pIndex++;
                continue;
            }

            var srcRunIndex = runAt[logicalIndex];
            if ((uint)srcRunIndex >= (uint)runs.Length)
            {
                pIndex++;
                continue;
            }

            var srcRun = runs[srcRunIndex];
            var isInlineCode = inlineCodeRun[srcRunIndex];
            if (isInlineCode)
            {
                // Keep inline code unbroken.
                if (!consumedInlineCode[srcRunIndex])
                {
                    consumedInlineCode[srcRunIndex] = true;

                    AddSegment(srcRunIndex, startOffset: 0, endOffsetInclusive: Math.Max(0, (srcRun.Text ?? string.Empty).Length - 1), isRightToLeft: (levels[logicalIndex] & 1) == 1);
                }

                // Skip all positions that map to this inline-code run.
                while (pIndex < order.Length)
                {
                    var idx = order[pIndex];
                    if ((uint)idx >= (uint)runAt.Length || runAt[idx] != srcRunIndex)
                    {
                        break;
                    }
                    pIndex++;
                }

                continue;
            }

            var start = offsetAt[logicalIndex];
            var end = start;
            var step = 0;

            // Grow a contiguous (in-source) substring while we stay within the same source run.
            var q = pIndex + 1;
            while (q < order.Length)
            {
                var nextLogical = order[q];
                if ((uint)nextLogical >= (uint)lineText.Length)
                {
                    break;
                }

                if (runAt[nextLogical] != srcRunIndex)
                {
                    break;
                }

                var nextOffset = offsetAt[nextLogical];
                var delta = nextOffset - end;
                if (step == 0)
                {
                    if (delta is 1 or -1)
                    {
                        step = delta;
                        end = nextOffset;
                        q++;
                        continue;
                    }

                    break;
                }

                if (delta == step)
                {
                    end = nextOffset;
                    q++;
                    continue;
                }

                break;
            }

            var segStart = Math.Min(start, end);
            var segEnd = Math.Max(start, end);
            AddSegment(srcRunIndex, segStart, segEnd, isRightToLeft: (levels[logicalIndex] & 1) == 1);

            pIndex = q;
        }

        var visualRuns = builder.ToImmutable();

        // Align RTL paragraphs/headings to the right edge (paragraph-level base direction).
        if (baseIsRtl)
        {
            var maxRight = float.NegativeInfinity;
            for (var i = 0; i < visualRuns.Length; i++)
            {
                maxRight = Math.Max(maxRight, visualRuns[i].Bounds.Right);
            }

            if (float.IsFinite(maxRight))
            {
                var dx = contentRight - maxRight;
                if (dx != 0)
                {
                    var shifted = ImmutableArray.CreateBuilder<InlineRunLayout>(visualRuns.Length);
                    for (var i = 0; i < visualRuns.Length; i++)
                    {
                        shifted.Add(ShiftRunX(visualRuns[i], dx));
                    }
                    visualRuns = shifted.ToImmutable();
                }
            }
        }

        return visualRuns;

        void AddSegment(int srcRunIndex, int startOffset, int endOffsetInclusive, bool isRightToLeft)
        {
            var src = runs[srcRunIndex];
            var srcText = src.Text ?? string.Empty;
            if (srcText.Length == 0)
            {
                return;
            }

            startOffset = Math.Clamp(startOffset, 0, Math.Max(0, srcText.Length - 1));
            endOffsetInclusive = Math.Clamp(endOffsetInclusive, 0, Math.Max(0, srcText.Length - 1));
            if (endOffsetInclusive < startOffset)
            {
                (startOffset, endOffsetInclusive) = (endOffsetInclusive, startOffset);
            }

            var length = (endOffsetInclusive - startOffset) + 1;
            var text = srcText.Substring(startOffset, length);

            var span = src.Kind == NodeKind.InlineCode && !src.IsCodeBlockLine
                ? src.Span
                : new SourceSpan(src.Span.Start + startOffset, src.Span.Start + startOffset + length);

            var m = textMeasurer.Measure(text, src.Style, scale);
            var width = Math.Max(0, m.Width);
            var height = Math.Max(0, src.Bounds.Height);

            var extraLeft = 0f;
            if (src.Kind == NodeKind.InlineCode && !src.IsCodeBlockLine)
            {
                var inlinePad = Math.Max(0, theme.Metrics.InlineCodePadding) * scale;
                extraLeft = inlinePad;
                width += inlinePad * 2;
                height = Math.Max(height, Math.Max(0, m.Height) + (inlinePad * 2));
            }

            var bounds = new RectF(cursorX, src.Bounds.Y, width, height);
            var glyphX = BuildGlyphX(text, src.Style, scale, textMeasurer, startX: bounds.X, extraLeft, isRightToLeft);

            builder.Add(new InlineRunLayout(
                Id: src.Id,
                Kind: src.Kind,
                Span: span,
                Bounds: bounds,
                Style: src.Style,
                Text: text,
                Url: src.Url,
                IsStrikethrough: src.IsStrikethrough,
                IsCodeBlockLine: src.IsCodeBlockLine,
                GlyphX: glyphX,
                NodeTextOffset: src.NodeTextOffset + startOffset,
                IsRightToLeft: isRightToLeft));

            cursorX += width;
        }
    }

    private enum BidiClass
    {
        L,
        R,
        AL,
        EN,
        AN,
        ES,
        ET,
        CS,
        NSM,
        BN,
        B,
        S,
        WS,
        ON,
    }

    private static BidiClass Classify(char ch)
    {
        if (char.IsWhiteSpace(ch))
        {
            return BidiClass.WS;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(ch);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
        {
            return BidiClass.NSM;
        }

        if (char.IsDigit(ch))
        {
            // Distinguish Arabic-Indic digits.
            return ch is >= '\u0660' and <= '\u0669' or >= '\u06F0' and <= '\u06F9'
                ? BidiClass.AN
                : BidiClass.EN;
        }

        if (IsRtlCodePoint(ch))
        {
            // We do not distinguish AL vs R here; the downstream resolver normalizes AL to R anyway.
            return BidiClass.R;
        }

        if (char.IsLetter(ch))
        {
            return BidiClass.L;
        }

        // Basic punctuation / symbol handling.
        return BidiClass.ON;
    }

    private static BidiClass Classify(int codePoint, char fallback)
    {
        if (codePoint >= 0)
        {
            if (IsRtlCodePoint(codePoint))
            {
                return BidiClass.R;
            }
        }

        return Classify(fallback);
    }

    private static bool IsRtlCodePoint(char ch) => IsRtlCodePoint((int)ch);

    private static bool IsRtlCodePoint(int codePoint)
    {
        // Common RTL ranges and marks.
        // This is an approximation used to support robust RTL behavior without a full bidi-category table.
        return codePoint switch
        {
            // Explicit RTL marks
            0x061C or 0x200F or 0x202B or 0x202E => true,
            _ =>
                // Hebrew
                (codePoint is >= 0x0590 and <= 0x05FF) ||
                // Arabic + supplements
                (codePoint is >= 0x0600 and <= 0x06FF) ||
                (codePoint is >= 0x0750 and <= 0x077F) ||
                (codePoint is >= 0x08A0 and <= 0x08FF) ||
                // Syriac
                (codePoint is >= 0x0700 and <= 0x074F) ||
                // Thaana
                (codePoint is >= 0x0780 and <= 0x07BF) ||
                // NKo
                (codePoint is >= 0x07C0 and <= 0x07FF) ||
                // Arabic presentation forms
                (codePoint is >= 0xFB50 and <= 0xFDFF) ||
                (codePoint is >= 0xFE70 and <= 0xFEFF)
        };
    }

    private static void ResolveWeakAndNeutralTypes(BidiClass[] types, int baseLevel)
    {
        if (types.Length == 0)
        {
            return;
        }

        var baseDir = (baseLevel & 1) == 1 ? BidiClass.R : BidiClass.L;

        // W1: NSM -> previous type (or base).
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] == BidiClass.NSM)
            {
                types[i] = i > 0 ? types[i - 1] : baseDir;
            }
        }

        // W2: EN -> AN if previous strong is AL.
        var prevStrong = baseDir;
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            if (t is BidiClass.L or BidiClass.R or BidiClass.AL)
            {
                prevStrong = t;
            }

            if (t == BidiClass.EN && prevStrong == BidiClass.AL)
            {
                types[i] = BidiClass.AN;
            }
        }

        // W3: AL -> R.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] == BidiClass.AL)
            {
                types[i] = BidiClass.R;
            }
        }

        // W4: ES/CS between numbers.
        for (var i = 1; i < types.Length - 1; i++)
        {
            var t = types[i];
            if (t == BidiClass.ES && types[i - 1] == BidiClass.EN && types[i + 1] == BidiClass.EN)
            {
                types[i] = BidiClass.EN;
            }
            else if (t == BidiClass.CS)
            {
                var left = types[i - 1];
                var right = types[i + 1];
                if (left == BidiClass.EN && right == BidiClass.EN)
                {
                    types[i] = BidiClass.EN;
                }
                else if (left == BidiClass.AN && right == BidiClass.AN)
                {
                    types[i] = BidiClass.AN;
                }
            }
        }

        // W5: ET adjacent to EN -> EN.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] != BidiClass.ET)
            {
                continue;
            }

            var start = i;
            var end = i;
            while (end + 1 < types.Length && types[end + 1] == BidiClass.ET)
            {
                end++;
            }

            var leftIsEN = start - 1 >= 0 && types[start - 1] == BidiClass.EN;
            var rightIsEN = end + 1 < types.Length && types[end + 1] == BidiClass.EN;
            if (leftIsEN || rightIsEN)
            {
                for (var k = start; k <= end; k++)
                {
                    types[k] = BidiClass.EN;
                }
            }

            i = end;
        }

        // W6: ES/ET/CS -> ON.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is BidiClass.ES or BidiClass.ET or BidiClass.CS)
            {
                types[i] = BidiClass.ON;
            }
        }

        // W7: EN -> L if previous strong is L.
        prevStrong = baseDir;
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            if (t is BidiClass.L or BidiClass.R)
            {
                prevStrong = t;
            }

            if (t == BidiClass.EN && prevStrong == BidiClass.L)
            {
                types[i] = BidiClass.L;
            }
        }

        // N1/N2: neutrals (WS/ON) resolution.
        var iNeutral = 0;
        while (iNeutral < types.Length)
        {
            if (!IsNeutral(types[iNeutral]))
            {
                iNeutral++;
                continue;
            }

            var startNeutral = iNeutral;
            var endNeutral = iNeutral;
            while (endNeutral + 1 < types.Length && IsNeutral(types[endNeutral + 1]))
            {
                endNeutral++;
            }

            var leftStrong = FindStrong(types, startNeutral - 1, direction: -1, baseDir);
            var rightStrong = FindStrong(types, endNeutral + 1, direction: +1, baseDir);

            var resolved = leftStrong == rightStrong ? leftStrong : baseDir;
            for (var k = startNeutral; k <= endNeutral; k++)
            {
                types[k] = resolved;
            }

            iNeutral = endNeutral + 1;
        }

        static bool IsNeutral(BidiClass t) => t is BidiClass.WS or BidiClass.ON or BidiClass.B or BidiClass.S or BidiClass.BN;

        static BidiClass FindStrong(BidiClass[] t, int start, int direction, BidiClass baseDir)
        {
            for (var i = start; i >= 0 && i < t.Length; i += direction)
            {
                if (t[i] is BidiClass.L or BidiClass.R)
                {
                    return t[i];
                }
            }

            return baseDir;
        }
    }

    private static int[] ComputeImplicitLevels(BidiClass[] types, int baseLevel)
    {
        var levels = new int[types.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = baseLevel;
        }

        for (var i = 0; i < types.Length; i++)
        {
            var level = levels[i];
            var t = types[i];

            if ((level & 1) == 0)
            {
                // Even level.
                if (t == BidiClass.R)
                {
                    levels[i] = level + 1;
                }
                else if (t is BidiClass.AN or BidiClass.EN)
                {
                    levels[i] = level + 2;
                }
            }
            else
            {
                // Odd level.
                if (t == BidiClass.L)
                {
                    levels[i] = level + 1;
                }
                else if (t is BidiClass.AN or BidiClass.EN)
                {
                    levels[i] = level + 1;
                }
            }
        }

        return levels;
    }

    private static int[] ComputeVisualOrder(int[] levels)
    {
        var order = new int[levels.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        if (levels.Length == 0)
        {
            return order;
        }

        var max = 0;
        for (var i = 0; i < levels.Length; i++)
        {
            max = Math.Max(max, levels[i]);
        }

        for (var level = max; level > 0; level--)
        {
            var i = 0;
            while (i < levels.Length)
            {
                while (i < levels.Length && levels[i] < level)
                {
                    i++;
                }

                var start = i;
                while (i < levels.Length && levels[i] >= level)
                {
                    i++;
                }

                var end = i - 1;
                if (end > start)
                {
                    Array.Reverse(order, start, end - start + 1);
                }
            }
        }

        return order;
    }

    private static ImmutableArray<InlineRunLayout> AlignRunsRight(ImmutableArray<InlineRunLayout> runs, float paddingLeft, float contentWidth)
    {
        if (runs.Length == 0)
        {
            return runs;
        }

        var maxRight = float.NegativeInfinity;
        for (var i = 0; i < runs.Length; i++)
        {
            maxRight = Math.Max(maxRight, runs[i].Bounds.Right);
        }

        if (!float.IsFinite(maxRight))
        {
            return runs;
        }

        var contentRight = paddingLeft + Math.Max(0, contentWidth);
        var dx = contentRight - maxRight;
        if (dx <= 0)
        {
            return runs;
        }

        return runs.Select(r => ShiftRunX(r, dx)).ToImmutableArray();
    }

    private static InlineRunLayout ShiftRunX(InlineRunLayout run, float dx)
    {
        if (dx == 0)
        {
            return run;
        }

        var b = run.Bounds;
        var shiftedBounds = new RectF(b.X + dx, b.Y, b.Width, b.Height);

        var gx = run.GlyphX;
        if (!gx.IsDefault && gx.Length > 0)
        {
            var builder = ImmutableArray.CreateBuilder<float>(gx.Length);
            for (var i = 0; i < gx.Length; i++)
            {
                builder.Add(gx[i] + dx);
            }
            gx = builder.ToImmutable();
        }

        var shifted = run with { Bounds = shiftedBounds, GlyphX = gx };
#if DEBUG
        ValidateRunGeometry(shifted);
#endif
        return shifted;
    }

    private static bool ContainsStrongRtl(IEnumerable<string> texts)
    {
        foreach (var t in texts)
        {
            if (TryGetFirstStrongDirection(t, out var rtl))
            {
                return rtl;
            }
        }

        return false;
    }

    private static bool ContainsStrongRtl(IEnumerable<BlockNode> blocks)
    {
        foreach (var b in blocks)
        {
            if (TryGetFirstStrongDirection(b, out var rtl))
            {
                return rtl;
            }
        }

        return false;
    }

    private static bool TryGetFirstStrongDirection(BlockNode block, out bool isRtl)
    {
        switch (block)
        {
            case ParagraphBlockNode p:
                return TryGetFirstStrongDirection(p.Inlines, out isRtl);
            case HeadingBlockNode h:
                return TryGetFirstStrongDirection(h.Inlines, out isRtl);
            case BlockQuoteBlockNode q:
                return TryGetFirstStrongDirection(q.Blocks, out isRtl);
            case ListBlockNode l:
                return TryGetFirstStrongDirection(l.Items.SelectMany(static it => it.Blocks), out isRtl);
            case ListItemBlockNode li:
                return TryGetFirstStrongDirection(li.Blocks, out isRtl);
            case TableBlockNode t:
                return TryGetFirstStrongDirection(t.Rows.SelectMany(static r => r.Cells).SelectMany(static c => c.Blocks), out isRtl);
            case TableRowBlockNode tr:
                return TryGetFirstStrongDirection(tr.Cells.SelectMany(static c => c.Blocks), out isRtl);
            case TableCellBlockNode tc:
                return TryGetFirstStrongDirection(tc.Blocks, out isRtl);
            case CodeBlockNode:
            case ThematicBreakBlockNode:
                isRtl = false;
                return false;
            default:
                isRtl = false;
                return false;
        }
    }

    private static bool TryGetFirstStrongDirection(IEnumerable<BlockNode> blocks, out bool isRtl)
    {
        foreach (var b in blocks)
        {
            if (TryGetFirstStrongDirection(b, out isRtl))
            {
                return true;
            }
        }

        isRtl = false;
        return false;
    }

    private static bool TryGetFirstStrongDirection(ImmutableArray<InlineNode> inlines, out bool isRtl)
    {
        for (var i = 0; i < inlines.Length; i++)
        {
            if (TryGetFirstStrongDirection(inlines[i], out isRtl))
            {
                return true;
            }
        }

        isRtl = false;
        return false;
    }

    private static bool TryGetFirstStrongDirection(InlineNode inline, out bool isRtl)
    {
        switch (inline)
        {
            case TextInlineNode t:
                return TryGetFirstStrongDirection(t.Text, out isRtl);
            case InlineCodeNode c:
                return TryGetFirstStrongDirection(c.Code, out isRtl);
            case EmphasisInlineNode e:
                return TryGetFirstStrongDirection(e.Inlines, out isRtl);
            case StrongInlineNode s:
                return TryGetFirstStrongDirection(s.Inlines, out isRtl);
            case StrikethroughInlineNode st:
                return TryGetFirstStrongDirection(st.Inlines, out isRtl);
            case LinkInlineNode l:
                return TryGetFirstStrongDirection(l.Inlines, out isRtl);
            case ImageInlineNode img:
                return TryGetFirstStrongDirection(img.AltText, out isRtl);
            case LineBreakInlineNode:
                isRtl = false;
                return false;
            default:
                isRtl = false;
                return false;
        }
    }

    private static bool TryGetFirstStrongDirection(string? text, out bool isRtl)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            isRtl = false;
            return false;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsStrongRtlRune(rune))
            {
                isRtl = true;
                return true;
            }

            if (IsStrongLtrRune(rune))
            {
                isRtl = false;
                return true;
            }
        }

        isRtl = false;
        return false;
    }

    private bool DetermineIsRtl(IEnumerable<string> texts)
    {
        foreach (var t in texts)
        {
            if (TryGetFirstStrongDirection(t, out var rtl))
            {
                return rtl;
            }
        }

        return DefaultIsRtl;
    }

    private bool DetermineIsRtl(IEnumerable<BlockNode> blocks)
    {
        foreach (var b in blocks)
        {
            if (TryGetFirstStrongDirection(b, out var rtl))
            {
                return rtl;
            }
        }

        return DefaultIsRtl;
    }

    private static bool IsStrongRtlRune(Rune r)
    {
        // Heuristic: treat common RTL blocks as strong RTL.
        // Hebrew, Arabic, Arabic Supplement, Arabic Extended, Arabic Presentation Forms.
        var v = r.Value;
        return (v >= 0x0590 && v <= 0x08FF)
            || (v >= 0xFB1D && v <= 0xFEFF);
    }

    private static bool IsStrongLtrRune(Rune r)
    {
        // Latin letters count as strong LTR for our heuristic.
        var v = r.Value;
        return (v >= 'A' && v <= 'Z')
            || (v >= 'a' && v <= 'z');
    }

    private static ImmutableArray<float> BuildGlyphX(string text, MarkdownTextStyle style, float scale, ITextMeasurer measurer, float startX, float extraLeft, bool isRightToLeft)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ImmutableArray<float>.Empty;
        }

        if (measurer is ITextShaper shaper)
        {
            var shaped = shaper.Shape(text, style, scale, isRightToLeft);
            var caret = shaped.CaretX;
            if (!caret.IsDefault && caret.Length > 0)
            {
                var caretBuilder = ImmutableArray.CreateBuilder<float>(caret.Length);
                var offset = startX + Math.Max(0, extraLeft);
                for (var i = 0; i < caret.Length; i++)
                {
                    caretBuilder.Add(caret[i] + offset);
                }

                return caretBuilder.ToImmutable();
            }
        }

        var builder = ImmutableArray.CreateBuilder<float>(text.Length + 1);
        var x = startX + Math.Max(0, extraLeft);
        builder.Add(x);

        for (var i = 0; i < text.Length; i++)
        {
            // Fallback: per-char measurement (approximate caret positions).
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

        // InlineCodeNode renders as its inner code text, but its SourceSpan includes the backticks.
        // Do NOT slice the span based on rendered text offsets; keep it as the original node span.
        // Also treat inline code as a single unbreakable token for determinism.
        if (segment.Kind == NodeKind.InlineCode && !segment.IsCodeBlockLine)
        {
            if (s.Length > 0)
            {
                yield return new Token(segment.Id, segment.Kind, segment.Span, segment.Style, s, segment.Url, segment.IsStrikethrough, segment.IsCodeBlockLine, IsWhitespace: false);
            }
            yield break;
        }

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
