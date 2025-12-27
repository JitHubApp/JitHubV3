namespace JitHub.Markdown;

public interface IMarkdownStyleResolver
{
    MarkdownTextStyle ResolveTextStyle(InlineNode inline, MarkdownTheme theme);

    MarkdownBlockStyle ResolveBlockStyle(BlockNode block, MarkdownTheme theme);
}
