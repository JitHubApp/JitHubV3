using FluentAssertions;
using JitHubV3.Presentation.ComposeSearch;
using JitHubV3.Services.Ai;
using NUnit.Framework;
using System.Linq;

namespace JitHubV3.Tests.Ai;

public sealed class AiGitHubQueryPlanValidatorTests
{
    [Test]
    public void Validate_ReturnsNull_WhenQueryMissing()
    {
        var plan = AiGitHubQueryPlanValidator.Validate(new AiGitHubQueryPlanCandidate(Query: null, Domain: "issues"));
        plan.Should().BeNull();
    }

    [Test]
    public void Validate_DefaultsToIssues_WhenDomainsMissingOrInvalid()
    {
        var plan = AiGitHubQueryPlanValidator.Validate(new AiGitHubQueryPlanCandidate(
            Query: "find bugs",
            Domains: ["nonsense", "", "   "]));

        plan.Should().NotBeNull();
        plan!.Domains.Should().Equal([ComposeSearchDomain.IssuesAndPullRequests]);
    }

    [Test]
    public void Validate_ParsesSynonyms_AndOrdersDomainsDeterministically()
    {
        var plan = AiGitHubQueryPlanValidator.Validate(new AiGitHubQueryPlanCandidate(
            Query: "http client",
            Domains: ["users", "repos", "code", "issues"]));

        plan.Should().NotBeNull();
        plan!.Domains.Should().Equal(new[]
        {
            ComposeSearchDomain.IssuesAndPullRequests,
            ComposeSearchDomain.Repositories,
            ComposeSearchDomain.Users,
            ComposeSearchDomain.Code,
        });
    }

    [Test]
    public void Validate_ClampsQuery_AndNormalizesWhitespace()
    {
        var longQuery = string.Join(" ", Enumerable.Repeat("x", 600));
        var plan = AiGitHubQueryPlanValidator.Validate(new AiGitHubQueryPlanCandidate(
            Query: "  hello\r\nworld\t" + longQuery,
            Domain: "repos"));

        plan.Should().NotBeNull();
        plan!.Query.Should().NotContain("\r");
        plan.Query.Should().NotContain("\n");
        plan.Query.Should().NotContain("\t");
        plan.Query.Length.Should().BeLessOrEqualTo(512);
    }
}
