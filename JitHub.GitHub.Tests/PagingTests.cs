using FluentAssertions;
using JitHub.GitHub.Abstractions.Paging;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class PagingTests
{
    [Test]
    public void FirstPage_sets_page_number_1()
    {
        var page = PageRequest.FirstPage(pageSize: 30);

        page.PageSize.Should().Be(30);
        page.PageNumber.Should().Be(1);
        page.Cursor.Should().BeNull();
    }

    [Test]
    public void FromCursor_normalizes_empty_to_null()
    {
        var page = PageRequest.FromCursor("  ", pageSize: 30);

        page.PageNumber.Should().BeNull();
        page.Cursor.Should().BeNull();
    }
}
