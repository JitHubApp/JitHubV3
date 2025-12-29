using FluentAssertions;
using JitHub.Data.Caching;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class CacheKeyTests
{
    [Test]
    public void Equal_when_parameters_are_provided_in_different_orders()
    {
        var a = CacheKey.Create(
            operation: "issues.list",
            userScope: "user1",
            ("repo", "runtime"),
            ("owner", "dotnet"));

        var b = CacheKey.Create(
            operation: "issues.list",
            userScope: "user1",
            ("owner", "dotnet"),
            ("repo", "runtime"));

        a.Should().Be(b);
        a.ToString().Should().Be(b.ToString());
    }

    [Test]
    public void Trims_operation_and_scope_and_parameters()
    {
        var key = new CacheKey(
            operation: "  repos.list  ",
            userScope: "  me  ",
            parameters: new[]
            {
                new KeyValuePair<string, string>("  owner ", "  dotnet  "),
            });

        key.Operation.Should().Be("repos.list");
        key.UserScope.Should().Be("me");
        key.Parameters.Should().ContainSingle();
        key.Parameters[0].Key.Should().Be("owner");
        key.Parameters[0].Value.Should().Be("dotnet");
    }
}
