using FluentAssertions;
using JitHubV3.Presentation;
using JitHubV3.Services.Platform;

namespace JitHubV3.Tests.Presentation.Infrastructure;

public sealed class HardwareStatusBarExtensionTests
{
    [Test]
    public void Reports_cpu_conservatively()
    {
        var ext = new HardwareStatusBarExtension(new FakeCapabilities());

        ext.Segments.Should().ContainSingle();
        ext.Segments[0].Id.Should().Be("hardware");
        ext.Segments[0].Text.Should().Be("HW: CPU");
    }

    private sealed class FakeCapabilities : IPlatformCapabilities
    {
        public bool SupportsSecureSecretStore => true;
        public bool SupportsLocalFoundryDetection => false;
        public bool SupportsHardwareAccelerationIntrospection => false;
    }
}
