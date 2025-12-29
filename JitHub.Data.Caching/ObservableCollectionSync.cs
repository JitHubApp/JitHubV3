using System.Collections.ObjectModel;

namespace JitHub.Data.Caching;

public static class ObservableCollectionSync
{
    public static void SyncById<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> source,
        Func<T, long> getId,
        Func<T, T, bool> shouldReplace)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (getId is null)
        {
            throw new ArgumentNullException(nameof(getId));
        }

        if (shouldReplace is null)
        {
            throw new ArgumentNullException(nameof(shouldReplace));
        }

        if (source.Count == 0)
        {
            if (target.Count != 0)
            {
                target.Clear();
            }

            return;
        }

        // Build desired order without a full reset (avoids ListView flicker).
        for (var desiredIndex = 0; desiredIndex < source.Count; desiredIndex++)
        {
            var desiredItem = source[desiredIndex];
            var desiredId = getId(desiredItem);

            var currentIndex = -1;
            for (var i = desiredIndex; i < target.Count; i++)
            {
                if (getId(target[i]) == desiredId)
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                target.Insert(desiredIndex, desiredItem);
                continue;
            }

            if (currentIndex != desiredIndex)
            {
                target.Move(currentIndex, desiredIndex);
            }

            if (shouldReplace(target[desiredIndex], desiredItem))
            {
                target[desiredIndex] = desiredItem;
            }
        }

        // Remove any trailing items not present in the source.
        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
