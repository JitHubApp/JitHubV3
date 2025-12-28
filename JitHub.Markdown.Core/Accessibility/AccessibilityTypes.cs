using System.Collections.Immutable;

namespace JitHub.Markdown;

internal enum MarkdownAccessibilityRole
{
    Document = 0,
    Group,
    Heading,
    Paragraph,
    List,
    ListItem,
    Link,
}

internal sealed record AccessibilityNode(
    NodeId Id,
    MarkdownAccessibilityRole Role,
    RectF Bounds,
    string? Name,
    string? Url,
    int? Level,
    bool? IsOrdered,
    ImmutableArray<AccessibilityNode> Children);

internal sealed record MarkdownAccessibilityTree(AccessibilityNode Root);
