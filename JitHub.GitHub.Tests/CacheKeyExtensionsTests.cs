using FluentAssertions;
using JitHub.Data.Caching;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class CacheKeyExtensionsTests
{
    [Test]
    public void GetParameterValue_returns_null_when_missing()
    {
        var key = CacheKey.Create("op", userScope: null, ("a", "1"));

        key.GetParameterValue("missing").Should().BeNull();
        key.GetParameterValueOrEmpty("missing").Should().BeEmpty();
    }

    [Test]
    public void GetParameterValue_returns_value_when_present()
    {
        var key = new CacheKey(
            operation: "op",
            userScope: null,
            parameters:
            [
                new KeyValuePair<string, string>(" owner ", " octo "),
                new KeyValuePair<string, string>("repo", "hello"),
            ]);

        key.GetParameterValue("owner").Should().Be("octo");
        key.GetParameterValueOrEmpty("repo").Should().Be("hello");
    }
}
