namespace JitHubV3.Tests;

public sealed record AppConfig(string? Environment);

public class AppInfoTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void AppInfoCreation()
    {
        var appInfo = new AppConfig(Environment: "Test");

        appInfo.Should().NotBeNull();
        appInfo.Environment.Should().Be("Test");
    }
}
