using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax.Inlines;

namespace JitHub.Markdown;

internal sealed class GitHubCommitShaInlineParser : InlineParser
{
    private readonly GitHubEnrichmentsOptions _options;

    public GitHubCommitShaInlineParser(GitHubEnrichmentsOptions options)
    {
        _options = options;
        // Hex commits start with [0-9a-fA-F]. We'll only match lowercase/uppercase in body.
        OpeningCharacters = "0123456789abcdefABCDEF".ToCharArray();
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (processor is null)
        {
            return false;
        }

        var repo = _options.RepositorySlug;
        if (string.IsNullOrWhiteSpace(repo))
        {
            return false;
        }

        // Boundary before: avoid matching inside identifiers.
        var prev = slice.PeekCharExtra(-1);
        if (prev != '\0' && (char.IsLetterOrDigit(prev) || prev == '_' || prev == '-'))
        {
            return false;
        }

        var start = slice.Start;
        var i = start;
        var end = slice.Text.Length;

        var max = 40;
        var len = 0;
        while (i < end && len < max)
        {
            var ch = slice.Text[i];
            if (IsHex(ch))
            {
                i++;
                len++;
                continue;
            }
            break;
        }

        if (len == 40)
        {
            // ok
        }
        else if (_options.AllowShortShas && len is >= 7 and <= 12)
        {
            // ok
        }
        else
        {
            return false;
        }

        // Boundary after.
        var after = i < end ? slice.Text[i] : '\0';
        if (after != '\0' && (char.IsLetterOrDigit(after) || after == '_' || after == '-'))
        {
            return false;
        }

        var sha = slice.Text.Substring(start, len);

        var baseUrl = (_options.BaseUrl ?? "https://github.com").TrimEnd('/');
        var repoSlug = repo.Trim().Trim('/');
        var url = $"{baseUrl}/{repoSlug}/commit/{sha}";

        var link = new LinkInline
        {
            Url = url,
            Title = null,
            IsImage = false,
            IsClosed = true,
        };

        link.AppendChild(new LiteralInline(sha));
        link.Span = new Markdig.Syntax.SourceSpan(start, start + len - 1);

        processor.Inline = link;
        slice.Start = start + len;
        return true;
    }

    private static bool IsHex(char c)
        => (c >= '0' && c <= '9')
           || (c >= 'a' && c <= 'f')
           || (c >= 'A' && c <= 'F');
}
