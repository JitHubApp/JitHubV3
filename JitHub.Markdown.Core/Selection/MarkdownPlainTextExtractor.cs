using System.Text;

namespace JitHub.Markdown;

public static class MarkdownPlainTextExtractor
{
    public static string Extract(string markdown)
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown ?? string.Empty);

        var sb = new StringBuilder();
        for (var i = 0; i < doc.Blocks.Length; i++)
        {
            AppendBlock(sb, doc.Blocks[i]);
            if (i < doc.Blocks.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private static void AppendBlock(StringBuilder sb, BlockNode block)
    {
        switch (block)
        {
            case ParagraphBlockNode p:
                AppendInlines(sb, p.Inlines);
                return;

            case HeadingBlockNode h:
                AppendInlines(sb, h.Inlines);
                return;

            case BlockQuoteBlockNode q:
                for (var i = 0; i < q.Blocks.Length; i++)
                {
                    AppendBlock(sb, q.Blocks[i]);
                    if (i < q.Blocks.Length - 1) sb.Append('\n');
                }
                return;

            case ListBlockNode l:
                for (var i = 0; i < l.Items.Length; i++)
                {
                    AppendBlock(sb, l.Items[i]);
                    if (i < l.Items.Length - 1) sb.Append('\n');
                }
                return;

            case ListItemBlockNode li:
                for (var i = 0; i < li.Blocks.Length; i++)
                {
                    AppendBlock(sb, li.Blocks[i]);
                    if (i < li.Blocks.Length - 1) sb.Append('\n');
                }
                return;

            case CodeBlockNode cb:
                sb.Append(cb.Code);
                return;

            case TableBlockNode t:
                for (var r = 0; r < t.Rows.Length; r++)
                {
                    var row = t.Rows[r];
                    for (var c = 0; c < row.Cells.Length; c++)
                    {
                        var cell = row.Cells[c];
                        for (var bi = 0; bi < cell.Blocks.Length; bi++)
                        {
                            AppendBlock(sb, cell.Blocks[bi]);
                        }
                        if (c < row.Cells.Length - 1) sb.Append('\t');
                    }
                    if (r < t.Rows.Length - 1) sb.Append('\n');
                }
                return;

            default:
                return;
        }
    }

    private static void AppendInlines(StringBuilder sb, IReadOnlyList<InlineNode> inlines)
    {
        for (var i = 0; i < inlines.Count; i++)
        {
            AppendInline(sb, inlines[i]);
        }
    }

    private static void AppendInline(StringBuilder sb, InlineNode inline)
    {
        switch (inline)
        {
            case TextInlineNode t:
                sb.Append(t.Text);
                return;

            case LineBreakInlineNode:
                sb.Append('\n');
                return;

            case InlineCodeNode c:
                sb.Append(c.Code);
                return;

            case LinkInlineNode l:
                for (var i = 0; i < l.Inlines.Length; i++)
                {
                    AppendInline(sb, l.Inlines[i]);
                }
                return;

            case ImageInlineNode img:
                for (var i = 0; i < img.AltText.Length; i++)
                {
                    AppendInline(sb, img.AltText[i]);
                }
                return;

            case EmphasisInlineNode e:
                for (var i = 0; i < e.Inlines.Length; i++)
                {
                    AppendInline(sb, e.Inlines[i]);
                }
                return;

            case StrongInlineNode s:
                for (var i = 0; i < s.Inlines.Length; i++)
                {
                    AppendInline(sb, s.Inlines[i]);
                }
                return;

            case StrikethroughInlineNode st:
                for (var i = 0; i < st.Inlines.Length; i++)
                {
                    AppendInline(sb, st.Inlines[i]);
                }
                return;

            default:
                return;
        }
    }
}
