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

        var visible = layout.GetVisibleBlockIndices(context.Viewport.Y, context.Viewport.Height, context.Overscan);
        for (var i = 0; i < visible.Length; i++)
        {
            var block = layout.Blocks[visible[i]];
            RenderBlock(block, context);
        }

        context.Canvas.Restore();
    }

    private static void RenderBlock(BlockLayout block, RenderContext context)
    {
        DrawBlockBackground(block, context);

        switch (block)
        {
            case ParagraphLayout p:
                RenderLines(p.Lines, context);
                break;
            case HeadingLayout h:
                RenderLines(h.Lines, context);
                break;
            case CodeBlockLayout c:
                RenderLines(c.Lines, context);
                break;
            case BlockQuoteLayout q:
                foreach (var child in q.Blocks)
                {
                    RenderBlock(child, context);
                }
                break;
            case ThematicBreakLayout:
                // Baseline: background already drawn by block style (if any).
                break;
            default:
                break;
        }
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

    private static void RenderLines(System.Collections.Immutable.ImmutableArray<LineLayout> lines, RenderContext context)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            for (var j = 0; j < line.Runs.Length; j++)
            {
                var run = line.Runs[j];
                DrawRun(run, context);
            }
        }
    }

    private static void DrawRun(InlineRunLayout run, RenderContext context)
    {
        if (string.IsNullOrEmpty(run.Text))
        {
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
