using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class RepoKeyTests
{
    [Test]
    public void Ctor_trims_owner_and_name()
    {
        var key = new RepoKey("  dotnet  ", "  runtime ");

        key.Owner.Should().Be("dotnet");
        key.Name.Should().Be("runtime");
        key.ToString().Should().Be("dotnet/runtime");
    }

    [Test]
    public void Ctor_rejects_empty_owner()
    {
        var act = () => _ = new RepoKey("   ", "repo");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Ctor_rejects_empty_name()
    {
        var act = () => _ = new RepoKey("owner", "   ");
        act.Should().Throw<ArgumentException>();
    }
}
