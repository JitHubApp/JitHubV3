namespace JitHub.Markdown;

public enum MarkdownElement
{
    Paragraph = 0,

    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,

    Link,
    InlineCode,

    CodeBlock,
    BlockQuote,
    ThematicBreak,

    Unknown,
}
