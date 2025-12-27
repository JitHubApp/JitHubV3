using SkiaSharp;

namespace JitHub.Markdown;

public sealed class SkiaMarkdownRenderer : IMarkdownRenderer
{
    public void Render(MarkdownLayout layout, RenderContext context)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (context.Canvas is null) throw new ArgumentException("Canvas is required", nameof(context));
        if (context.Theme is null) throw new ArgumentException("Theme is required", nameof(context));
        if (context.Scale <= 0) throw new ArgumentOutOfRangeException(nameof(context.Scale));

        // Clip to viewport and render visible blocks only.
        context.Canvas.Save();
        context.Canvas.ClipRect(new SKRect(context.Viewport.X, context.Viewport.Y, context.Viewport.Right, context.Viewport.Bottom));

        var selectionGeometry = context.Selection is not null
            ? SelectionGeometryBuilder.Build(layout, context.Selection.Value)
            : null;

        var visible = layout.GetVisibleBlockIndices(context.Viewport.Y, context.Viewport.Height, context.Overscan);
        for (var i = 0; i < visible.Length; i++)
        {
            var block = layout.Blocks[visible[i]];
            RenderBlock(block, context, selectionGeometry, isInQuote: false);
        }

        context.Canvas.Restore();
    }

    private static void RenderBlock(BlockLayout block, RenderContext context, SelectionGeometry? selectionGeometry, bool isInQuote)
    {
        DrawBlockBackground(block, context);

        switch (block)
        {
            case ParagraphLayout p:
                RenderLines(p.Lines, context, selectionGeometry, isInQuote);
                break;
            case HeadingLayout h:
                RenderLines(h.Lines, context, selectionGeometry, isInQuote);
                break;
            case CodeBlockLayout c:
                RenderLines(c.Lines, context, selectionGeometry, isInQuote);
                break;
            case BlockQuoteLayout q:
                DrawBlockQuoteStripe(q, context);
                foreach (var child in q.Blocks)
                {
                    RenderBlock(child, context, selectionGeometry, isInQuote: true);
                }
                break;
            case ListLayout l:
                foreach (var item in l.Items)
                {
                    RenderBlock(item, context, selectionGeometry, isInQuote);
                }
                break;
            case ListItemLayout li:
                DrawListMarker(li, context);
                foreach (var child in li.Blocks)
                {
                    RenderBlock(child, context, selectionGeometry, isInQuote);
                }
                break;
            case TableLayout t:
                DrawTableGrid(t, context);
                for (var r = 0; r < t.Rows.Length; r++)
                {
                    var row = t.Rows[r];
                    for (var c = 0; c < row.Cells.Length; c++)
                    {
                        var cell = row.Cells[c];
                        for (var bi = 0; bi < cell.Blocks.Length; bi++)
                        {
                            RenderBlock(cell.Blocks[bi], context, selectionGeometry, isInQuote);
                        }
                    }
                }
                break;
            case ThematicBreakLayout:
                DrawThematicBreak(block, context);
                break;
            default:
                break;
        }
    }

    private static void DrawTableGrid(TableLayout table, RenderContext context)
    {
        if (table.Rows.Length == 0)
        {
            return;
        }

        var stroke = Math.Max(1f, context.Scale);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            Color = context.Theme.Colors.ThematicBreak.ToSKColor(),
        };

        // Outer border.
        var r = new SKRect(table.Bounds.X, table.Bounds.Y, table.Bounds.Right, table.Bounds.Bottom);
        context.Canvas.DrawRect(r, paint);

        // Vertical separators based on first row.
        var firstRow = table.Rows[0];
        for (var c = 0; c < firstRow.Cells.Length; c++)
        {
            var cell = firstRow.Cells[c];
            var x = cell.Bounds.Right;
            if (x <= table.Bounds.X || x >= table.Bounds.Right)
            {
                continue;
            }

            context.Canvas.DrawLine(x, table.Bounds.Y, x, table.Bounds.Bottom, paint);
        }

        // Horizontal separators.
        for (var ri = 0; ri < table.Rows.Length; ri++)
        {
            var y = table.Rows[ri].Bounds.Bottom;
            if (y <= table.Bounds.Y || y >= table.Bounds.Bottom)
            {
                continue;
            }

            context.Canvas.DrawLine(table.Bounds.X, y, table.Bounds.Right, y, paint);
        }
    }

    private static void DrawThematicBreak(BlockLayout block, RenderContext context)
    {
        var y = block.Bounds.Y + (block.Bounds.Height / 2f);
        var thickness = Math.Max(1f, context.Scale);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            Color = context.Theme.Colors.ThematicBreak.ToSKColor(),
        };

        var x1 = block.Bounds.X;
        var x2 = block.Bounds.Right;
        context.Canvas.DrawLine(x1, y, x2, y, paint);
    }

    private static void DrawBlockQuoteStripe(BlockQuoteLayout quote, RenderContext context)
    {
        // Phase 4.2.7: quote stripe + background (background is handled via block style).
        var padding = Math.Max(0, quote.Style.Padding) * context.Scale;
        if (padding <= 0)
        {
            return;
        }

        var stripeWidth = Math.Max(2f, MathF.Round(padding * 0.2f));
        var stripeX = quote.Bounds.X + MathF.Round(padding * 0.35f);
        var stripeTop = quote.Bounds.Y + MathF.Round(padding * 0.2f);
        var stripeBottom = quote.Bounds.Bottom - MathF.Round(padding * 0.2f);

        if (stripeBottom <= stripeTop)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = context.Theme.Colors.ThematicBreak.ToSKColor(),
        };

        var r = new SKRect(stripeX, stripeTop, stripeX + stripeWidth, stripeBottom);
        context.Canvas.DrawRect(r, paint);
    }

    private static void DrawListMarker(ListItemLayout item, RenderContext context)
    {
        if (string.IsNullOrWhiteSpace(item.MarkerText))
        {
            return;
        }

        var style = context.Theme.Typography.Paragraph;
        using var paint = CreateTextPaint(style, context.Scale);
        paint.GetFontMetrics(out var metrics);

        var x = item.MarkerBounds.X;
        var baselineY = item.MarkerBounds.Y - metrics.Ascent;

        context.Canvas.DrawText(item.MarkerText, x, baselineY, paint);
    }

    private static void DrawBlockBackground(BlockLayout block, RenderContext context)
    {
        if (block.Style.Background == ColorRgba.Transparent)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = block.Style.Background.ToSKColor(),
        };

        var r = new SKRect(block.Bounds.X, block.Bounds.Y, block.Bounds.Right, block.Bounds.Bottom);
        var radius = Math.Max(0, block.Style.CornerRadius) * context.Scale;
        if (radius <= 0)
        {
            context.Canvas.DrawRect(r, paint);
        }
        else
        {
            context.Canvas.DrawRoundRect(r, radius, radius, paint);
        }
    }

    private static void RenderLines(System.Collections.Immutable.ImmutableArray<LineLayout> lines, RenderContext context, SelectionGeometry? selectionGeometry, bool isInQuote)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Pass 1: run backgrounds (inline code surface, images).
            for (var j = 0; j < line.Runs.Length; j++)
            {
                var run = line.Runs[j];
                DrawRunBackground(run, context);
            }

            // Pass 2: selection overlay (continuous base fill).
            if (selectionGeometry is not null)
            {
                DrawSelectionForLine(line, selectionGeometry, context, isInQuote);
            }

            // Pass 3: run foreground (text + decorations).
            for (var j = 0; j < line.Runs.Length; j++)
            {
                var run = line.Runs[j];
                DrawRunForeground(run, context);
            }
        }
    }

    private static void DrawSelectionForLine(LineLayout line, SelectionGeometry selectionGeometry, RenderContext context, bool isInQuote)
    {
        // Selection geometry is in layout coordinates; canvas is already clipped to viewport.
        var lineTop = line.Y;
        var lineBottom = line.Y + line.Height;

        var baseFill = context.Theme.Selection.SelectionFill;
        var hasStrongFill = baseFill.A >= 200;

        using var basePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = baseFill.ToSKColor(),
        };

        // Base continuous overlay.
        for (var i = 0; i < selectionGeometry.Rects.Length; i++)
        {
            var r = selectionGeometry.Rects[i];
            if (r.Bottom <= lineTop || r.Y >= lineBottom)
            {
                continue;
            }

            var rr = new SKRect(r.X, r.Y, r.Right, r.Bottom);
            context.Canvas.DrawRect(rr, basePaint);
        }

        // Element-aware overlay decorations (do not break continuity since base fill is already drawn).
        if (hasStrongFill)
        {
            return;
        }

        ColorRgba? quoteOverlay = isInQuote ? WithAlpha(context.Theme.Colors.QuoteBackground, baseFill.A) : null;
        using var quotePaint = quoteOverlay is not null
            ? new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = quoteOverlay.Value.ToSKColor() }
            : null;

        using var inlineCodePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = WithAlpha(context.Theme.Colors.InlineCodeBackground, baseFill.A).ToSKColor(),
        };

        using var codeBlockPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = WithAlpha(context.Theme.Colors.CodeBlockBackground, baseFill.A).ToSKColor(),
        };

        for (var i = 0; i < selectionGeometry.Segments.Length; i++)
        {
            var seg = selectionGeometry.Segments[i];
            var r = seg.Rect;
            if (r.Bottom <= lineTop || r.Y >= lineBottom)
            {
                continue;
            }

            // Quote overlay (applies to all selected segments within the quote container).
            if (quotePaint is not null)
            {
                context.Canvas.DrawRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), quotePaint);
            }

            // Inline code overlay.
            if (seg.Kind == NodeKind.InlineCode && !seg.IsCodeBlockLine)
            {
                context.Canvas.DrawRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), inlineCodePaint);
                continue;
            }

            // Code block line overlay.
            if (seg.IsCodeBlockLine)
            {
                context.Canvas.DrawRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), codeBlockPaint);
            }
        }
    }

    private static ColorRgba WithAlpha(ColorRgba c, byte a) => new(c.R, c.G, c.B, a);

    private static void DrawRunBackground(InlineRunLayout run, RenderContext context)
    {
        if (string.IsNullOrEmpty(run.Text))
        {
            return;
        }

        if (run.Kind == NodeKind.Image)
        {
            DrawImageRun(run, context);
            return;
        }

        // Inline code surface background is drawn before selection so selection can overlay it.
        if (run.Kind == NodeKind.InlineCode && !run.IsCodeBlockLine)
        {
            var radius = Math.Max(0, context.Theme.Metrics.InlineCodeCornerRadius) * context.Scale;

            using var bgPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = context.Theme.Colors.InlineCodeBackground.ToSKColor(),
            };

            var r = new SKRect(run.Bounds.X, run.Bounds.Y, run.Bounds.Right, run.Bounds.Bottom);
            if (radius <= 0)
            {
                context.Canvas.DrawRect(r, bgPaint);
            }
            else
            {
                context.Canvas.DrawRoundRect(r, radius, radius, bgPaint);
            }
        }
    }

    private static void DrawRunForeground(InlineRunLayout run, RenderContext context)
    {
        if (string.IsNullOrEmpty(run.Text))
        {
            return;
        }

        if (run.Kind == NodeKind.Image)
        {
            // Image already drawn in background pass.
            return;
        }

        if (run.Kind == NodeKind.Link && run.Url is not null)
        {
            context.HitRegions?.Add(new HitRegion(run.Id, run.Kind, run.Span, run.Bounds, run.Url));
        }

        var style = run.Style;
        if (run.Kind == NodeKind.Link)
        {
            if (context.PressedId is { } pressed && pressed == run.Id)
            {
                style = style.With(foreground: Multiply(style.Foreground, 0.75f));
            }
            else if (context.HoveredId is { } hovered && hovered == run.Id)
            {
                style = style.With(foreground: Multiply(style.Foreground, 1.1f));
            }
        }

        using var paint = CreateTextPaint(style, context.Scale);
        paint.GetFontMetrics(out var metrics);

        var x = run.Bounds.X;
        var baselineY = run.Bounds.Y - metrics.Ascent;

        // Phase 4.2.5: inline code surface.
        if (run.Kind == NodeKind.InlineCode && !run.IsCodeBlockLine)
        {
            var pad = Math.Max(0, context.Theme.Metrics.InlineCodePadding) * context.Scale;

            x += pad;
            baselineY = (run.Bounds.Y + pad) - metrics.Ascent;
        }

        context.Canvas.DrawText(run.Text, x, baselineY, paint);

        if (style.Underline)
        {
            var w = paint.MeasureText(run.Text);
            using var underline = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, (style.FontSize * 0.08f) * context.Scale),
                Color = paint.Color,
            };

            var y = baselineY + Math.Max(1, style.FontSize * 0.12f * context.Scale);
            context.Canvas.DrawLine(x, y, x + w, y, underline);
        }

        if (run.IsStrikethrough)
        {
            var w = paint.MeasureText(run.Text);
            using var strike = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, (style.FontSize * 0.08f) * context.Scale),
                Color = paint.Color,
            };

            var y = baselineY - Math.Max(1, style.FontSize * 0.3f * context.Scale);
            context.Canvas.DrawLine(x, y, x + w, y, strike);
        }
    }

    private static void DrawImageRun(InlineRunLayout run, RenderContext context)
    {
        var bounds = new SKRect(run.Bounds.X, run.Bounds.Y, run.Bounds.Right, run.Bounds.Bottom);
        var radius = Math.Max(0, context.Theme.Metrics.CornerRadius) * context.Scale;

        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(run.Url))
        {
            if (Uri.TryCreate(run.Url, UriKind.Absolute, out var absolute))
            {
                uri = absolute;
            }
            else if (context.Theme.ImageBaseUri is not null && Uri.TryCreate(context.Theme.ImageBaseUri, run.Url, out var combined))
            {
                uri = combined;
            }
        }

        var image = (uri is not null && context.ImageResolver is not null) ? context.ImageResolver(uri) : null;
        if (image is not null)
        {
            context.Canvas.DrawImage(image, bounds);
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = context.Theme.Colors.CodeBlockBackground.ToSKColor(),
        };

        if (radius <= 0)
        {
            context.Canvas.DrawRect(bounds, paint);
        }
        else
        {
            context.Canvas.DrawRoundRect(bounds, radius, radius, paint);
        }
    }

    private static ColorRgba Multiply(ColorRgba c, float factor)
    {
        static byte Clamp(float v)
        {
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)v;
        }

        return new ColorRgba(
            Clamp(c.R * factor),
            Clamp(c.G * factor),
            Clamp(c.B * factor),
            c.A);
    }

    private static SKPaint CreateTextPaint(MarkdownTextStyle style, float scale)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = style.FontSize * scale,
            Color = style.Foreground.ToSKColor(),
        };

        paint.Typeface = SkiaTypefaceCache.GetTypeface(style);
        return paint;
    }
}
