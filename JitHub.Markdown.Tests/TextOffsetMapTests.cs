using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class TextOffsetMapTests
{
    [Test]
    public void MapRenderedRange_length_leq_zero_returns_empty_span()
    {
        var m = new TextOffsetMap("abc", new[] { 10, 11, 12 });
        m.MapRenderedRangeToSourceSpan(0, 0).Should().Be(new SourceSpan(0, 0));
        m.MapRenderedRangeToSourceSpan(0, -1).Should().Be(new SourceSpan(0, 0));
    }

    [Test]
    public void MapRenderedRange_empty_rendered_text_returns_empty_span()
    {
        var m = new TextOffsetMap("", Array.Empty<int>());
        m.MapRenderedRangeToSourceSpan(0, 1).Should().Be(new SourceSpan(0, 0));
    }

    [Test]
    public void MapRenderedRange_clamps_start_and_end()
    {
        var m = new TextOffsetMap("abcd", new[] { 5, 6, 7, 8 });

        // start negative clamps to 0.
        m.MapRenderedRangeToSourceSpan(-10, 2).Should().Be(new SourceSpan(5, 7));

        // range beyond end clamps.
        m.MapRenderedRangeToSourceSpan(3, 10).Should().Be(new SourceSpan(8, 9));

        // start beyond end yields empty.
        m.MapRenderedRangeToSourceSpan(999, 1).Should().Be(new SourceSpan(0, 0));
    }

    [Test]
    public void MapRenderedRange_ignores_negative_mapping_entries()
    {
        var m = new TextOffsetMap("A\nB", new[] { 100, -1, 200 });

        // Selecting only the newline gives no valid source indices.
        m.MapRenderedRangeToSourceSpan(1, 1).Should().Be(new SourceSpan(0, 0));

        // Selecting across newline spans min..max of valid entries.
        m.MapRenderedRangeToSourceSpan(0, 3).Should().Be(new SourceSpan(100, 201));
    }

    [Test]
    public void MapRenderedRange_returns_min_to_max_plus_one_for_mixed_valid_values()
    {
        var m = new TextOffsetMap("abcd", new[] { 10, 50, -1, 20 });

        // indices 0..4 => valid {10,50,20} => [10,51)
        m.MapRenderedRangeToSourceSpan(0, 4).Should().Be(new SourceSpan(10, 51));
    }
}
