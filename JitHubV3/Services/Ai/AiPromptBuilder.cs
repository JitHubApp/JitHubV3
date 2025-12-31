using System.Text;
using JitHubV3.Presentation.ComposeSearch;

namespace JitHubV3.Services.Ai;

public static class AiPromptBuilder
{
    public static string BuildSystemPrompt(IReadOnlyList<ComposeSearchDomain>? allowedDomains)
    {
        var domains = allowedDomains is { Count: > 0 }
            ? allowedDomains
            : new[]
            {
                ComposeSearchDomain.IssuesAndPullRequests,
                ComposeSearchDomain.Repositories,
                ComposeSearchDomain.Users,
                ComposeSearchDomain.Code,
            };

        var sb = new StringBuilder();
        sb.AppendLine("You convert a user request into a GitHub Search query and a domain selection.");
        sb.AppendLine("Return ONLY a single JSON object. No markdown. No code fences.");
        sb.AppendLine("Schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"query\": string,              // GitHub search query syntax");
        sb.AppendLine("  \"domain\": string|null,        // optional single domain");
        sb.AppendLine("  \"domains\": string[]|null,     // optional multiple domains");
        sb.AppendLine("  \"explanation\": string|null    // optional short explanation");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Allowed domain values (case-insensitive):");
        foreach (var d in domains.Distinct())
        {
            sb.AppendLine("- " + DomainToValue(d));
        }
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Always include 'query'.");
        sb.AppendLine("- Use 'domain' OR 'domains' (prefer 'domains' when multiple apply).");
        sb.AppendLine("- If unsure, default to 'issues'.");
        sb.AppendLine("- Keep explanation under 1 sentence.");

        return sb.ToString();
    }

    private static string DomainToValue(ComposeSearchDomain domain) => domain switch
    {
        ComposeSearchDomain.IssuesAndPullRequests => "issues",
        ComposeSearchDomain.Repositories => "repos",
        ComposeSearchDomain.Users => "users",
        ComposeSearchDomain.Code => "code",
        _ => "issues",
    };
}
