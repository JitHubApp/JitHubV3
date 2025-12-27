using SkiaSharp;

namespace JitHub.Markdown;

public sealed class SkiaMarkdownRenderer : IMarkdownRenderer
{
    public void Render(MarkdownLayout layout, RenderContext context)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (context.Canvas is null) throw new ArgumentException("Canvas is required", nameof(context));
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

        using var paint = CreateTextPaint(run.Style, context.Scale);
        paint.GetFontMetrics(out var metrics);

        var x = run.Bounds.X;
        var baselineY = run.Bounds.Y - metrics.Ascent;

        context.Canvas.DrawText(run.Text, x, baselineY, paint);

        if (run.Style.Underline)
        {
            var w = paint.MeasureText(run.Text);
            using var underline = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, (run.Style.FontSize * 0.08f) * context.Scale),
                Color = paint.Color,
            };

            var y = baselineY + Math.Max(1, run.Style.FontSize * 0.12f * context.Scale);
            context.Canvas.DrawLine(x, y, x + w, y, underline);
        }
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
