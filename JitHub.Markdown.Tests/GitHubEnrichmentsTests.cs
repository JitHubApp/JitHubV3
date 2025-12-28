using JitHub.Markdown;
using Markdig;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class GitHubEnrichmentsTests
{
    [Test]
    public void Enrichments_linkify_mentions_issues_and_commit_shas_when_repo_is_configured()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions { ConfigurePipeline = b => b.UseAdvancedExtensions() },
            new GitHubEnrichmentsPlugin(new GitHubEnrichmentsOptions { RepositorySlug = "uno-platform/uno" }));

        var markdown = "Hi @octocat see #123 and deadbeef0";
        var doc = engine.Parse(markdown);

        var para = doc.Blocks.OfType<ParagraphBlockNode>().Single();
        var links = para.Inlines.OfType<LinkInlineNode>().ToArray();

        links.Select(l => l.Url).Should().Contain(new[]
        {
            "https://github.com/octocat",
            "https://github.com/uno-platform/uno/issues/123",
            "https://github.com/uno-platform/uno/commit/deadbeef0",
        });

        foreach (var link in links)
        {
            link.Span.Start.Should().BeGreaterThanOrEqualTo(0);
            link.Span.EndExclusive.Should().BeLessThanOrEqualTo(markdown.Length);
            markdown.Substring(link.Span.Start, link.Span.Length).Should().NotBeNullOrEmpty();
        }

        var mention = links.Single(l => l.Url == "https://github.com/octocat");
        markdown.Substring(mention.Span.Start, mention.Span.Length).Should().Be("@octocat");

        var issue = links.Single(l => l.Url == "https://github.com/uno-platform/uno/issues/123");
        markdown.Substring(issue.Span.Start, issue.Span.Length).Should().Be("#123");

        var sha = links.Single(l => l.Url == "https://github.com/uno-platform/uno/commit/deadbeef0");
        markdown.Substring(sha.Span.Start, sha.Span.Length).Should().Be("deadbeef0");
    }

    [Test]
    public void Enrichments_linkify_mentions_without_repo_but_do_not_linkify_issues_or_shas()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions { ConfigurePipeline = b => b.UseAdvancedExtensions() },
            new GitHubEnrichmentsPlugin(new GitHubEnrichmentsOptions { RepositorySlug = null }));

        var markdown = "Hi @octocat see #123 and deadbeef0";
        var doc = engine.Parse(markdown);

        var para = doc.Blocks.OfType<ParagraphBlockNode>().Single();
        var links = para.Inlines.OfType<LinkInlineNode>().ToArray();

        links.Should().HaveCount(1);
        links[0].Url.Should().Be("https://github.com/octocat");
    }

    [Test]
    public void Enrichments_do_not_apply_inside_inline_code_or_emails()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions { ConfigurePipeline = b => b.UseAdvancedExtensions() },
            new GitHubEnrichmentsPlugin(new GitHubEnrichmentsOptions { RepositorySlug = "uno-platform/uno" }));

        var markdown = "Email a@b.com and code `@octocat` `#123` `deadbeef0`";
        var doc = engine.Parse(markdown);

        var para = doc.Blocks.OfType<ParagraphBlockNode>().Single();

        para.Inlines.OfType<LinkInlineNode>().Select(l => l.Url).Should().NotContain("https://github.com/octocat");
        para.Inlines.OfType<LinkInlineNode>().Select(l => l.Url).Should().NotContain("https://github.com/uno-platform/uno/issues/123");
        para.Inlines.OfType<LinkInlineNode>().Select(l => l.Url).Should().NotContain("https://github.com/uno-platform/uno/commit/deadbeef0");

        para.Inlines.OfType<InlineCodeNode>().Select(c => c.Code).Should().Contain(new[] { "@octocat", "#123", "deadbeef0" });
    }

    [Test]
    public void Enrichments_trim_base_url_and_repo_slug_when_building_urls()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions { ConfigurePipeline = b => b.UseAdvancedExtensions() },
            new GitHubEnrichmentsPlugin(new GitHubEnrichmentsOptions
            {
                BaseUrl = "https://github.com/",
                RepositorySlug = "uno-platform/uno/",
            }));

        var markdown = "Hi @octocat see #123 and deadbeef0";
        var doc = engine.Parse(markdown);

        var para = doc.Blocks.OfType<ParagraphBlockNode>().Single();
        var links = para.Inlines.OfType<LinkInlineNode>().ToArray();

        links.Select(l => l.Url).Should().Contain(new[]
        {
            "https://github.com/octocat",
            "https://github.com/uno-platform/uno/issues/123",
            "https://github.com/uno-platform/uno/commit/deadbeef0",
        });

        links.Any(l => l.Url?.Contains("//uno-platform", StringComparison.Ordinal) == true).Should().BeFalse();
    }

    [Test]
    public void Enrichments_respect_allow_short_shas_option()
    {
        var full40 = "0123456789abcdef0123456789abcdef01234567";
        full40.Length.Should().Be(40);

        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions { ConfigurePipeline = b => b.UseAdvancedExtensions() },
            new GitHubEnrichmentsPlugin(new GitHubEnrichmentsOptions
            {
                RepositorySlug = "uno-platform/uno",
                AllowShortShas = false,
            }));

        var markdown = $"short deadbeef0 and full {full40}";
        var doc = engine.Parse(markdown);

        var para = doc.Blocks.OfType<ParagraphBlockNode>().Single();
        var links = para.Inlines.OfType<LinkInlineNode>().ToArray();

        links.Select(l => l.Url).Should().Contain($"https://github.com/uno-platform/uno/commit/{full40}");
        links.Select(l => l.Url).Should().NotContain("https://github.com/uno-platform/uno/commit/deadbeef0");
    }

    [Test]
    public void Enrichments_enforce_boundaries_for_mentions_issues_and_shas()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions { ConfigurePipeline = b => b.UseAdvancedExtensions() },
            new GitHubEnrichmentsPlugin(new GitHubEnrichmentsOptions { RepositorySlug = "uno-platform/uno" }));

        var markdown = "prefix_x@octocat ok @octocat bad #123a ok #123 bad deadbeef0_ ok deadbeef0.";
        var doc = engine.Parse(markdown);

        var para = doc.Blocks.OfType<ParagraphBlockNode>().Single();
        var urls = para.Inlines.OfType<LinkInlineNode>().Select(l => l.Url).ToArray();

        urls.Should().Contain("https://github.com/octocat");
        urls.Should().Contain("https://github.com/uno-platform/uno/issues/123");
        urls.Should().Contain("https://github.com/uno-platform/uno/commit/deadbeef0");

        // Not linkified due to boundary/format rules.
        urls.Should().NotContain("https://github.com/prefix_x");
        urls.Should().NotContain("https://github.com/uno-platform/uno/issues/123a");
    }

    [Test]
    public void Enrichments_do_not_linkify_invalid_usernames()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions { ConfigurePipeline = b => b.UseAdvancedExtensions() },
            new GitHubEnrichmentsPlugin(new GitHubEnrichmentsOptions { RepositorySlug = "uno-platform/uno" }));

        var markdown = "@-bad and @bad- and @ok-user";
        var doc = engine.Parse(markdown);

        var para = doc.Blocks.OfType<ParagraphBlockNode>().Single();
        var urls = para.Inlines.OfType<LinkInlineNode>().Select(l => l.Url).ToArray();

        urls.Should().Contain("https://github.com/ok-user");
        urls.Should().NotContain("https://github.com/-bad");
        urls.Should().NotContain("https://github.com/bad-");
    }
}
