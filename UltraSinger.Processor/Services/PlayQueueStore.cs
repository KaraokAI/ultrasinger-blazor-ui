using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// In-memory, SortOrder-sorted view of the "sing next" play queue, write-through to SQLite
/// via <see cref="PlayQueueDatabase"/>. Mutations only happen on human actions (add/remove/
/// move/mark-sung), so unlike <see cref="SqliteSongStore"/> there's no need for a dirty-flag
/// write-behind flusher — every mutation persists immediately.
/// </summary>
public class PlayQueueStore(PlayQueueDatabase database, ILogger<PlayQueueStore> logger)
{
    private readonly List<PlayQueueItemRecord> _items = new();
    private readonly object _lock = new();

    public void Load()
    {
        database.Initialise();

        lock (_lock)
        {
            _items.Clear();
            _items.AddRange(database.LoadAll().OrderBy(x => x.SortOrder));
        }

        logger.LogInformation("Loaded {Count} play queue item(s) from disk.", _items.Count);
    }

    public List<PlayQueueItemRecord> GetAll()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public PlayQueueItemRecord Add(string title, string artist, SongSource source, string? extraInfo, string? filePath)
    {
        PlayQueueItemRecord item;
        lock (_lock)
        {
            var nextSortOrder = _items.Count == 0 ? 0 : _items.Max(x => x.SortOrder) + 1;
            item = new PlayQueueItemRecord
            {
                Id = Guid.NewGuid(),
                Title = title,
                Artist = artist,
                Source = source,
                ExtraInfo = extraInfo,
                FilePath = filePath,
                QueuedAt = DateTime.Now,
                SortOrder = nextSortOrder
            };
            _items.Add(item);
        }

        database.Upsert(item);
        return item;
    }

    public bool Remove(Guid id)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(x => x.Id == id);
            if (index < 0)
            {
                return false;
            }

            _items.RemoveAt(index);
        }

        database.Delete(id);
        return true;
    }

    public bool MoveUp(Guid id)
    {
        (PlayQueueItemRecord, PlayQueueItemRecord)? swapped = null;

        lock (_lock)
        {
            var index = _items.FindIndex(x => x.Id == id);
            if (index <= 0)
            {
                return false;
            }

            var current = _items[index];
            var previous = _items[index - 1];
            (current.SortOrder, previous.SortOrder) = (previous.SortOrder, current.SortOrder);
            _items.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            swapped = (current, previous);
        }

        database.SaveOrder([(swapped.Value.Item1.Id, swapped.Value.Item1.SortOrder), (swapped.Value.Item2.Id, swapped.Value.Item2.SortOrder)]);
        return true;
    }

    public bool MoveDown(Guid id)
    {
        (PlayQueueItemRecord, PlayQueueItemRecord)? swapped = null;

        lock (_lock)
        {
            var index = _items.FindIndex(x => x.Id == id);
            if (index < 0 || index >= _items.Count - 1)
            {
                return false;
            }

            var current = _items[index];
            var next = _items[index + 1];
            (current.SortOrder, next.SortOrder) = (next.SortOrder, current.SortOrder);
            _items.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            swapped = (current, next);
        }

        database.SaveOrder([(swapped.Value.Item1.Id, swapped.Value.Item1.SortOrder), (swapped.Value.Item2.Id, swapped.Value.Item2.SortOrder)]);
        return true;
    }

    public bool MarkSung(Guid id, bool isSung)
    {
        PlayQueueItemRecord? item;
        lock (_lock)
        {
            item = _items.FirstOrDefault(x => x.Id == id);
            if (item == null)
            {
                return false;
            }

            item.IsSung = isSung;
            item.SungAt = isSung ? DateTime.Now : null;
        }

        database.Upsert(item);
        return true;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }

        database.DeleteAll();
    }
}
