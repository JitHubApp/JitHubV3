namespace JitHub.Markdown;

public sealed class GitHubEnrichmentsOptions
{
    /// <summary>
    /// Base GitHub web URL. Defaults to https://github.com.
    /// </summary>
    public string BaseUrl { get; init; } = "https://github.com";

    /// <summary>
    /// Optional repository in the form "owner/repo".
    /// When provided, #123 links and commit links target this repository.
    /// </summary>
    public string? RepositorySlug { get; init; }

    /// <summary>
    /// When true, recognizes short SHAs (7+ hex). When false, only 40-char SHAs.
    /// </summary>
    public bool AllowShortShas { get; init; } = true;
}
