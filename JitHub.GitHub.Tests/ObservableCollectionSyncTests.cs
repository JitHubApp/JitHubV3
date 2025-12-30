using System.Collections.ObjectModel;
using FluentAssertions;
using JitHub.Data.Caching;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class ObservableCollectionSyncTests
{
    private sealed record Item(long Id, int Revision);

    [Test]
    public void SyncById_removes_items_when_source_empty_without_reset()
    {
        var target = new ObservableCollection<Item> { new(1, 0) };

        var sawReset = false;
        target.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                sawReset = true;
            }
        };

        ObservableCollectionSync.SyncById(
            target,
            Array.Empty<Item>(),
            getId: x => x.Id,
            shouldReplace: (_, _) => true);

        target.Should().BeEmpty();
        sawReset.Should().BeFalse();
    }

    [Test]
    public void SyncById_inserts_moves_replaces_and_trims()
    {
        var target = new ObservableCollection<Item>
        {
            new(1, 0),
            new(2, 0),
            new(3, 0),
            new(4, 0),
        };

        // Desired: reorder, replace item 2, remove item 4, insert item 5.
        var source = new[]
        {
            new Item(3, 0),
            new Item(2, 1),
            new Item(1, 0),
            new Item(5, 0),
        };

        ObservableCollectionSync.SyncById(
            target,
            source,
            getId: x => x.Id,
            shouldReplace: (current, next) => current.Revision != next.Revision);

        target.Select(x => x.Id).Should().Equal(3, 2, 1, 5);
        target.Single(x => x.Id == 2).Revision.Should().Be(1);
        target.Any(x => x.Id == 4).Should().BeFalse();
    }
}
