namespace JitHub.Markdown;

/// <summary>
/// Allows plugins to customize mapping from a rendered caret (run + text offset) back to a source index.
/// </summary>
public interface ISelectionSourceIndexMapper
{
    bool TryMapCaretToSourceIndex(
        string sourceMarkdown,
        MarkdownDocumentModel document,
        InlineRunLayout run,
        int textOffset,
        out int sourceIndex);
}
