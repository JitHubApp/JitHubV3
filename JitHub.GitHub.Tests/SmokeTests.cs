using FluentAssertions;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class SmokeTests
{
    [Test]
    public void True_is_true()
    {
        true.Should().BeTrue();
    }
}
