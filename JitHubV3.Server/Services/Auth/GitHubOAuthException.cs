namespace JitHubV3.Server.Services.Auth;

internal sealed class GitHubOAuthException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public GitHubOAuthException(string message, int statusCode, string? responseBody, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
