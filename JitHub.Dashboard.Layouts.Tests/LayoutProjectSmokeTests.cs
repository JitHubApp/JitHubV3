using FluentAssertions;

namespace JitHub.Dashboard.Layouts.Tests;

public sealed class LayoutProjectSmokeTests
{
    [Test]
    public void Project_wires_up()
    {
        typeof(JitHub.Dashboard.Layouts.AssemblyMarker).Should().NotBeNull();
    }
}
