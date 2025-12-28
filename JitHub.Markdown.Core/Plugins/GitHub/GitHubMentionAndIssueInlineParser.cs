using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax.Inlines;

namespace JitHub.Markdown;

internal sealed class GitHubMentionAndIssueInlineParser : InlineParser
{
    private readonly GitHubEnrichmentsOptions _options;

    public GitHubMentionAndIssueInlineParser(GitHubEnrichmentsOptions options)
    {
        _options = options;
        OpeningCharacters = ['@', '#'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (processor is null)
        {
            return false;
        }

        var c = slice.CurrentChar;
        if (c != '@' && c != '#')
        {
            return false;
        }

        // Boundary check: avoid C# and emails.
        var prev = slice.PeekCharExtra(-1);
        if (prev != '\0' && (char.IsLetterOrDigit(prev) || prev == '_' || prev == '-'))
        {
            return false;
        }

        if (c == '@')
        {
            return TryMatchMention(processor, ref slice);
        }

        return TryMatchIssue(processor, ref slice);
    }

    private bool TryMatchMention(InlineProcessor processor, ref StringSlice slice)
    {
        var start = slice.Start;
        var i = start + 1;
        var end = slice.Text.Length;

        // GitHub username: 1-39 chars of [A-Za-z0-9-], cannot start/end with '-'.
        var length = 0;
        while (i < end && length < 39)
        {
            var ch = slice.Text[i];
            if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                i++;
                length++;
                continue;
            }
            break;
        }

        if (length == 0)
        {
            return false;
        }

        var username = slice.Text.Substring(start + 1, length);
        if (username[0] == '-' || username[^1] == '-')
        {
            return false;
        }

        // Boundary after.
        var after = i < end ? slice.Text[i] : '\0';
        if (after != '\0' && (char.IsLetterOrDigit(after) || after == '_' || after == '-'))
        {
            return false;
        }

        var baseUrl = (_options.BaseUrl ?? "https://github.com").TrimEnd('/');
        var url = $"{baseUrl}/{username}";

        var link = new LinkInline
        {
            Url = url,
            Title = null,
            IsImage = false,
            IsClosed = true,
        };

        // Child literal that includes the '@' prefix so displayed text matches source.
        link.AppendChild(new LiteralInline(slice.Text.Substring(start, length + 1)));

        // Span should cover the exact mention.
        link.Span = new Markdig.Syntax.SourceSpan(start, start + length);

        processor.Inline = link;
        slice.Start = start + length + 1;
        return true;
    }

    private bool TryMatchIssue(InlineProcessor processor, ref StringSlice slice)
    {
        // Requires repo context.
        var repo = _options.RepositorySlug;
        if (string.IsNullOrWhiteSpace(repo))
        {
            return false;
        }

        var start = slice.Start;
        var i = start + 1;
        var end = slice.Text.Length;

        var digitStart = i;
        while (i < end && char.IsDigit(slice.Text[i]))
        {
            i++;
        }

        var digitLen = i - digitStart;
        if (digitLen == 0)
        {
            return false;
        }

        // Boundary after.
        var after = i < end ? slice.Text[i] : '\0';
        if (after != '\0' && char.IsLetterOrDigit(after))
        {
            return false;
        }

        var number = slice.Text.Substring(digitStart, digitLen);

        var baseUrl = (_options.BaseUrl ?? "https://github.com").TrimEnd('/');
        var repoSlug = repo.Trim().Trim('/');

        // Use /issues/{n}. GitHub will redirect PR numbers appropriately.
        var url = $"{baseUrl}/{repoSlug}/issues/{number}";

        var link = new LinkInline
        {
            Url = url,
            Title = null,
            IsImage = false,
            IsClosed = true,
        };

        link.AppendChild(new LiteralInline(slice.Text.Substring(start, 1 + digitLen)));
        link.Span = new Markdig.Syntax.SourceSpan(start, start + digitLen);

        processor.Inline = link;
        slice.Start = start + 1 + digitLen;
        return true;
    }
}
