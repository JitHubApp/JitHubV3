namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class SourceSelectionTests
{
    [Test]
    public void Length_and_IsEmpty_behave_as_expected()
    {
        var s1 = new SourceSelection(3, 8);
        s1.Length.Should().Be(5);
        s1.IsEmpty.Should().BeFalse();

        var s2 = new SourceSelection(5, 5);
        s2.Length.Should().Be(0);
        s2.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Slice_returns_empty_for_out_of_range_or_inverted_ranges()
    {
        var md = "Hello";

        new SourceSelection(-1, 2).Slice(md).Should().BeEmpty();
        new SourceSelection(0, 999).Slice(md).Should().BeEmpty();
        new SourceSelection(4, 1).Slice(md).Should().BeEmpty();
    }

    [Test]
    public void Slice_throws_for_null_markdown()
    {
        var s = new SourceSelection(0, 1);
        s.Invoking(x => x.Slice(null!)).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Slice_returns_expected_substring_for_valid_range()
    {
        var md = "Hello world";
        new SourceSelection(6, 11).Slice(md).Should().Be("world");
    }
}
