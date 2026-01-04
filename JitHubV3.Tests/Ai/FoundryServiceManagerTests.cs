using System;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

[TestFixture]
public sealed class FoundryServiceManagerTests
{
    [TestCase(true, "where")]
    [TestCase(false, "which")]
    public void GetLocatorExeForOs_UsesExpectedCommand(bool isWindows, string expected)
    {
        var asm = typeof(JitHubV3.Services.Ai.FoundryLocal.FoundryClient).Assembly;
        var type = asm.GetType("JitHubV3.Services.Ai.FoundryLocal.FoundryServiceManager", throwOnError: true)!;

        var method = type.GetMethod(
            "GetLocatorExeForOs",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        method.Should().NotBeNull();

        var value = method!.Invoke(null, new object?[] { isWindows });
        value.Should().Be(expected);
    }
}
