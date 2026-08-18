using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public class BlazorSongQueueService
{
    private readonly List<SongQueueItem> _items = new();
    private readonly object _lock = new();

    public event Action? OnQueueChanged;

    public IReadOnlyList<SongQueueItem> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.ToList().AsReadOnly();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public void Enqueue(SongQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
        {
            _items.Add(item);
        }
        OnQueueChanged?.Invoke();
    }

    public SongQueueItem Enqueue(string title, string artist = "", SongSource source = SongSource.Local, string? extraInfo = null, string? filePath = null)
    {
        var item = new SongQueueItem
        {
            Title = title,
            Artist = artist,
            Source = source,
            ExtraInfo = extraInfo,
            FilePath = filePath,
            QueuedAt = DateTime.Now
        };
        Enqueue(item);
        return item;
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index >= 0)
            {
                _items.RemoveAt(index);
                removed = true;
            }
            else
            {
                removed = false;
            }
        }

        if (removed)
        {
            OnQueueChanged?.Invoke();
        }
        return removed;
    }

    public void MoveUp(Guid id)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index > 0)
            {
                var item = _items[index];
                _items.RemoveAt(index);
                _items.Insert(index - 1, item);
            }
            else
            {
                return;
            }
        }
        OnQueueChanged?.Invoke();
    }

    public void MoveDown(Guid id)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index >= 0 && index < _items.Count - 1)
            {
                var item = _items[index];
                _items.RemoveAt(index);
                _items.Insert(index + 1, item);
            }
            else
            {
                return;
            }
        }
        OnQueueChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return;
            _items.Clear();
        }
        OnQueueChanged?.Invoke();
    }
}
