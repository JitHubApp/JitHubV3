using JitHubV3.Presentation.ComposeSearch;

namespace JitHubV3.Services.Ai;

public static class AiGitHubQueryPlanValidator
{
    private const int MaxQueryLength = 512;
    private const int MaxExplanationLength = 512;

    private static readonly ComposeSearchDomain[] DomainOrder =
    [
        ComposeSearchDomain.IssuesAndPullRequests,
        ComposeSearchDomain.Repositories,
        ComposeSearchDomain.Users,
        ComposeSearchDomain.Code,
    ];

    public static AiGitHubQueryPlan? Validate(AiGitHubQueryPlanCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        var query = NormalizeSingleLine(candidate.Query, MaxQueryLength);
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var allowed = new HashSet<ComposeSearchDomain>();

        if (!string.IsNullOrWhiteSpace(candidate.Domain))
        {
            if (TryParseDomain(candidate.Domain!, out var d))
            {
                allowed.Add(d);
            }
        }

        if (candidate.Domains is not null)
        {
            foreach (var raw in candidate.Domains)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (TryParseDomain(raw, out var d))
                {
                    allowed.Add(d);
                }
            }
        }

        if (allowed.Count == 0)
        {
            allowed.Add(ComposeSearchDomain.IssuesAndPullRequests);
        }

        var orderedDomains = DomainOrder.Where(allowed.Contains).ToArray();

        var explanation = NormalizeSingleLine(candidate.Explanation, MaxExplanationLength);
        if (string.IsNullOrWhiteSpace(explanation))
        {
            explanation = null;
        }

        return new AiGitHubQueryPlan(query, orderedDomains, explanation);
    }

    private static bool TryParseDomain(string raw, out ComposeSearchDomain domain)
    {
        var s = raw.Trim();

        // Common synonyms expected from LLM output.
        if (string.Equals(s, "issues", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "prs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "pullrequests", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "issuesandpullrequests", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "issues_and_pull_requests", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "issues_and_pullrequests", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "issues/prs", StringComparison.OrdinalIgnoreCase))
        {
            domain = ComposeSearchDomain.IssuesAndPullRequests;
            return true;
        }

        if (string.Equals(s, "repos", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "repositories", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "repo", StringComparison.OrdinalIgnoreCase))
        {
            domain = ComposeSearchDomain.Repositories;
            return true;
        }

        if (string.Equals(s, "users", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "user", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "accounts", StringComparison.OrdinalIgnoreCase))
        {
            domain = ComposeSearchDomain.Users;
            return true;
        }

        if (string.Equals(s, "code", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "source", StringComparison.OrdinalIgnoreCase))
        {
            domain = ComposeSearchDomain.Code;
            return true;
        }

        // Finally, attempt enum-name parse.
        if (Enum.TryParse<ComposeSearchDomain>(s, ignoreCase: true, out var parsed))
        {
            domain = parsed;
            return true;
        }

        domain = default;
        return false;
    }

    private static string NormalizeSingleLine(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = value.Trim();
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

        while (s.Contains("  ", StringComparison.Ordinal))
        {
            s = s.Replace("  ", " ");
        }

        if (s.Length > maxLen)
        {
            s = s.Substring(0, maxLen).Trim();
        }

        return s;
    }
}
