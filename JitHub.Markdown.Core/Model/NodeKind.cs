namespace JitHub.Markdown;

public enum NodeKind
{
    Document = 0,

    Heading,
    Paragraph,
    BlockQuote,
    List,
    ListItem,
    CodeBlock,
    Table,
    ThematicBreak,

    Text,
    Emphasis,
    Strong,
    Strikethrough,
    Link,
    Image,
    InlineCode,
    LineBreak,
}
