using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownAccessibilityTests
{
    private sealed class TestTextMeasurer : ITextMeasurer
    {
        public TextMeasurement Measure(string text, MarkdownTextStyle style, float scale)
        {
            var lineHeight = GetLineHeight(style, scale);
            var charWidth = style.FontSize * 0.6f * scale;
            var width = Math.Max(0, (text ?? string.Empty).Length * charWidth);
            return new TextMeasurement(width, lineHeight);
        }

        public float GetLineHeight(MarkdownTextStyle style, float scale)
            => Math.Max(1, style.FontSize * 1.4f * scale);
    }

    [Test]
    public void Accessibility_tree_contains_heading_nodes()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("# Title\n\nParagraph\n");

        var layout = new MarkdownLayoutEngine().Layout(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());
        var tree = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: layout.Height);

        Flatten(tree.Root).Any(n => n.Role == MarkdownAccessibilityRole.Heading && n.Level == 1 && n.Name == "Title").Should().BeTrue();
    }

    [Test]
    public void Accessibility_tree_contains_list_and_listitem_nodes()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("- one\n- two\n");

        var layout = new MarkdownLayoutEngine().Layout(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());
        var tree = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: layout.Height);

        Flatten(tree.Root).Any(n => n.Role == MarkdownAccessibilityRole.List).Should().BeTrue();
        Flatten(tree.Root).Count(n => n.Role == MarkdownAccessibilityRole.ListItem).Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public void Accessibility_tree_contains_link_nodes_with_url()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("See [GitHub](https://github.com) for more.\n");

        var layout = new MarkdownLayoutEngine().Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());
        var tree = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: layout.Height);

        Flatten(tree.Root).Any(n => n.Role == MarkdownAccessibilityRole.Link && n.Url == "https://github.com" && n.Name == "GitHub").Should().BeTrue();
    }

    [Test]
    public void Accessibility_node_ids_are_stable_for_same_layout()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("# A\n\n- one\n- two\n\nSee [X](https://example.com)\n");

        var layout = new MarkdownLayoutEngine().Layout(doc, width: 480, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var t1 = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: layout.Height);
        var t2 = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: layout.Height);

        var ids1 = Flatten(t1.Root).Select(n => n.Id).ToArray();
        var ids2 = Flatten(t2.Root).Select(n => n.Id).ToArray();

        ids1.Should().Equal(ids2);
    }

    private static IEnumerable<AccessibilityNode> Flatten(AccessibilityNode node)
    {
        yield return node;
        for (var i = 0; i < node.Children.Length; i++)
        {
            foreach (var child in Flatten(node.Children[i]))
            {
                yield return child;
            }
        }
    }
}
