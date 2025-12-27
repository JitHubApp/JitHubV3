using System.Collections.Immutable;

namespace JitHub.Markdown;

public enum MarkdownAccessibilityRole
{
    Document = 0,
    Group,
    Heading,
    Paragraph,
    List,
    ListItem,
    Link,
}

public sealed record AccessibilityNode(
    NodeId Id,
    MarkdownAccessibilityRole Role,
    RectF Bounds,
    string? Name,
    string? Url,
    int? Level,
    bool? IsOrdered,
    ImmutableArray<AccessibilityNode> Children);

public sealed record MarkdownAccessibilityTree(AccessibilityNode Root);
