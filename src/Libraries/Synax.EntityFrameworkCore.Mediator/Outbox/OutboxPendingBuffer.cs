using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Scoped buffer of notifications enqueued in the current unit of work, keeping the
/// materialized instances alongside their staged rows so the post-commit hot path never
/// re-reads the database. Items move through three states: enqueued (staged, not saved),
/// saved (row written, transaction pending), and flushed to the channel on commit.
/// Rolled-back items are discarded — their rows no longer exist.
/// </summary>
internal sealed class OutboxPendingBuffer<TContext> where TContext : ISynaxContext
{
    private readonly List<Entry> _entries = [];
    private readonly Lock _lock = new();

    private sealed class Entry(Guid id, INotification notification)
    {
        public Guid Id { get; } = id;
        public INotification Notification { get; } = notification;
        public bool Saved { get; set; }
    }

    public void Add(Guid id, INotification notification)
    {
        lock (_lock) _entries.Add(new Entry(id, notification));
    }

    /// <summary>Marks everything staged so far as durably written (SaveChanges succeeded).</summary>
    public void MarkAllSaved()
    {
        lock (_lock)
        {
            foreach (var entry in _entries) entry.Saved = true;
        }
    }

    /// <summary>Removes and returns the saved items — called once their transaction committed
    /// (or immediately after a save with no transaction).</summary>
    public List<OutboxItem> TakeSaved()
    {
        lock (_lock)
        {
            List<OutboxItem> taken = [];
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (!_entries[i].Saved) continue;
                taken.Add(new OutboxItem(_entries[i].Id, _entries[i].Notification));
                _entries.RemoveAt(i);
            }

            taken.Reverse(); // restore enqueue order
            return taken;
        }
    }

    /// <summary>Discards saved items after a rollback — their rows were never durable.</summary>
    public void DropSaved()
    {
        lock (_lock) _entries.RemoveAll(e => e.Saved);
    }
}
