using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class SourceSpanTests
{
    [Test]
    public void FromInclusive_converts_to_exclusive_end()
    {
        SourceSpan.FromInclusive(5, 7).Should().Be(new SourceSpan(5, 8));
    }

    [Test]
    public void FromInclusive_end_before_start_produces_empty_span_at_start()
    {
        var s = SourceSpan.FromInclusive(5, 3);
        s.Should().Be(new SourceSpan(5, 5));
        s.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void IsEmpty_is_true_when_length_leq_zero()
    {
        new SourceSpan(0, 0).IsEmpty.Should().BeTrue();
        new SourceSpan(1, 0).IsEmpty.Should().BeTrue();
    }
}
