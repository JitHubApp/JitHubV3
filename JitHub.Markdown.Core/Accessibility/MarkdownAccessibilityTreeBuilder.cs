using System.Collections.Immutable;

namespace JitHub.Markdown;

public static class MarkdownAccessibilityTreeBuilder
{
    public static MarkdownAccessibilityTree Build(MarkdownLayout layout, float viewportTop, float viewportHeight, float overscan = 0)
    {
        _ = layout ?? throw new ArgumentNullException(nameof(layout));

        var view = new RectF(0, viewportTop - overscan, layout.Width, viewportHeight + (overscan * 2));

        var childrenBuilder = ImmutableArray.CreateBuilder<AccessibilityNode>();
        var indices = layout.GetVisibleBlockIndices(viewportTop, viewportHeight, overscan);
        for (var i = 0; i < indices.Length; i++)
        {
            var block = layout.Blocks[indices[i]];
            if (!block.Bounds.IntersectsWith(view))
            {
                continue;
            }

            var node = BuildBlockNode(block, view);
            if (node is not null)
            {
                childrenBuilder.Add(node);
            }
        }

        var rootBounds = new RectF(0, 0, layout.Width, layout.Height);
        var root = new AccessibilityNode(
            Id: new NodeId(0),
            Role: MarkdownAccessibilityRole.Document,
            Bounds: rootBounds,
            Name: null,
            Url: null,
            Level: null,
            IsOrdered: null,
            Children: childrenBuilder.ToImmutable());

        return new MarkdownAccessibilityTree(root);
    }

    private static AccessibilityNode? BuildBlockNode(BlockLayout block, RectF view)
        => block switch
        {
            HeadingLayout h => BuildHeading(h),
            ParagraphLayout p => BuildParagraph(p),
            ListLayout l => BuildList(l, view),
            ListItemLayout li => BuildListItem(li, view),
            BlockQuoteLayout q => BuildGroup(q.Id, q.Bounds, q.Blocks, view),
            TableLayout t => new AccessibilityNode(t.Id, MarkdownAccessibilityRole.Group, t.Bounds, Name: null, Url: null, Level: null, IsOrdered: null, Children: ImmutableArray<AccessibilityNode>.Empty),
            CodeBlockLayout c => new AccessibilityNode(c.Id, MarkdownAccessibilityRole.Group, c.Bounds, Name: null, Url: null, Level: null, IsOrdered: null, Children: ImmutableArray<AccessibilityNode>.Empty),
            ThematicBreakLayout hr => new AccessibilityNode(hr.Id, MarkdownAccessibilityRole.Group, hr.Bounds, Name: null, Url: null, Level: null, IsOrdered: null, Children: ImmutableArray<AccessibilityNode>.Empty),
            UnknownBlockLayout u => new AccessibilityNode(u.Id, MarkdownAccessibilityRole.Group, u.Bounds, Name: null, Url: null, Level: null, IsOrdered: null, Children: ImmutableArray<AccessibilityNode>.Empty),
            _ => new AccessibilityNode(block.Id, MarkdownAccessibilityRole.Group, block.Bounds, Name: null, Url: null, Level: null, IsOrdered: null, Children: ImmutableArray<AccessibilityNode>.Empty),
        };

    private static AccessibilityNode BuildGroup(NodeId id, RectF bounds, ImmutableArray<BlockLayout> blocks, RectF view)
    {
        var childrenBuilder = ImmutableArray.CreateBuilder<AccessibilityNode>();
        for (var i = 0; i < blocks.Length; i++)
        {
            var child = blocks[i];
            if (!child.Bounds.IntersectsWith(view))
            {
                continue;
            }

            var childNode = BuildBlockNode(child, view);
            if (childNode is not null)
            {
                childrenBuilder.Add(childNode);
            }
        }

        return new AccessibilityNode(
            Id: id,
            Role: MarkdownAccessibilityRole.Group,
            Bounds: bounds,
            Name: null,
            Url: null,
            Level: null,
            IsOrdered: null,
            Children: childrenBuilder.ToImmutable());
    }

    private static AccessibilityNode BuildHeading(HeadingLayout heading)
    {
        var children = BuildInlineChildren(heading.Lines);
        return new AccessibilityNode(
            Id: heading.Id,
            Role: MarkdownAccessibilityRole.Heading,
            Bounds: heading.Bounds,
            Name: GetBlockText(heading.Lines),
            Url: null,
            Level: heading.Level,
            IsOrdered: null,
            Children: children);
    }

    private static AccessibilityNode BuildParagraph(ParagraphLayout paragraph)
    {
        var children = BuildInlineChildren(paragraph.Lines);
        return new AccessibilityNode(
            Id: paragraph.Id,
            Role: MarkdownAccessibilityRole.Paragraph,
            Bounds: paragraph.Bounds,
            Name: GetBlockText(paragraph.Lines),
            Url: null,
            Level: null,
            IsOrdered: null,
            Children: children);
    }

    private static AccessibilityNode BuildList(ListLayout list, RectF view)
    {
        var itemsBuilder = ImmutableArray.CreateBuilder<AccessibilityNode>();
        for (var i = 0; i < list.Items.Length; i++)
        {
            var item = list.Items[i];
            if (!item.Bounds.IntersectsWith(view))
            {
                continue;
            }

            itemsBuilder.Add(BuildListItem(item, view));
        }

        return new AccessibilityNode(
            Id: list.Id,
            Role: MarkdownAccessibilityRole.List,
            Bounds: list.Bounds,
            Name: null,
            Url: null,
            Level: null,
            IsOrdered: list.IsOrdered,
            Children: itemsBuilder.ToImmutable());
    }

    private static AccessibilityNode BuildListItem(ListItemLayout item, RectF view)
    {
        var childrenBuilder = ImmutableArray.CreateBuilder<AccessibilityNode>();
        for (var i = 0; i < item.Blocks.Length; i++)
        {
            var child = item.Blocks[i];
            if (!child.Bounds.IntersectsWith(view))
            {
                continue;
            }

            var node = BuildBlockNode(child, view);
            if (node is not null)
            {
                childrenBuilder.Add(node);
            }
        }

        var name = item.MarkerText;
        return new AccessibilityNode(
            Id: item.Id,
            Role: MarkdownAccessibilityRole.ListItem,
            Bounds: item.Bounds,
            Name: string.IsNullOrWhiteSpace(name) ? null : name,
            Url: null,
            Level: null,
            IsOrdered: null,
            Children: childrenBuilder.ToImmutable());
    }

    private static ImmutableArray<AccessibilityNode> BuildInlineChildren(ImmutableArray<LineLayout> lines)
    {
        var builder = ImmutableArray.CreateBuilder<AccessibilityNode>();

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            for (var runIndex = 0; runIndex < line.Runs.Length; runIndex++)
            {
                var run = line.Runs[runIndex];
                if (run.Kind != NodeKind.Link)
                {
                    continue;
                }

                builder.Add(new AccessibilityNode(
                    Id: run.Id,
                    Role: MarkdownAccessibilityRole.Link,
                    Bounds: run.Bounds,
                    Name: string.IsNullOrWhiteSpace(run.Text) ? null : run.Text,
                    Url: string.IsNullOrWhiteSpace(run.Url) ? null : run.Url,
                    Level: null,
                    IsOrdered: null,
                    Children: ImmutableArray<AccessibilityNode>.Empty));
            }
        }

        return builder.ToImmutable();
    }

    private static string? GetBlockText(ImmutableArray<LineLayout> lines)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            for (var j = 0; j < line.Runs.Length; j++)
            {
                var t = line.Runs[j].Text;
                if (string.IsNullOrEmpty(t))
                {
                    continue;
                }

                builder.Append(t);
            }

            if (i < lines.Length - 1)
            {
                builder.Append(' ');
            }
        }

        var s = builder.ToString().Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
