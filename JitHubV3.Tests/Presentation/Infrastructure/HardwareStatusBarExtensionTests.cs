using FluentAssertions;
using JitHubV3.Presentation;

namespace JitHubV3.Tests.Presentation.Infrastructure;

public sealed class HardwareStatusBarExtensionTests
{
    [Test]
    public void Reports_cpu_conservatively()
    {
        var ext = new HardwareStatusBarExtension();

        ext.Segments.Should().ContainSingle();
        ext.Segments[0].Id.Should().Be("hardware");
        ext.Segments[0].Text.Should().Be("HW: CPU");
    }
}
