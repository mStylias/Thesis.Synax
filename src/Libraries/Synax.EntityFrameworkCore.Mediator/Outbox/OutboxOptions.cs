using System.Collections.Concurrent;
using Synax.Mediator;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Configuration for the transactional outbox.
/// </summary>
public sealed class OutboxOptions
{
    private readonly Dictionary<Type, string> _namesByType = [];
    private readonly Dictionary<string, Type> _typesByName = [];
    private readonly ConcurrentDictionary<string, Type> _resolutionCache = new();

    /// <summary>Capacity of the in-process hot-path channel. When full, signals are dropped
    /// (safe: the recovery sweep picks the rows up). Defaults to 1024.</summary>
    public int ChannelCapacity { get; set; } = 1024;

    /// <summary>How often the recovery sweep looks for due/orphaned rows. Defaults to 30s.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How old a never-attempted row must be before the sweep considers it orphaned
    /// (its hot-path signal was lost). Defaults to 1 minute.</summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Attempts before a row is dead-lettered (left in the table with its error). Defaults to 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Maximum rows claimed per sweep cycle. Defaults to 100.</summary>
    public int SweepBatchSize { get; set; } = 100;

    /// <summary>Retry delay as a function of the attempt number (1-based).
    /// Defaults to exponential: 30s, 60s, 120s, ... capped at 1 hour.</summary>
    public Func<int, TimeSpan> Backoff { get; set; } =
        attempt => TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempt - 1), 3600));

    /// <summary>
    /// Central policy marking dispatch failures as permanent: when the predicate returns
    /// <c>true</c> for a thrown exception, the message is dead-lettered immediately instead
    /// of retried. <see cref="OutboxDeadLetterException"/> is always treated as permanent,
    /// regardless of this predicate. Inner exceptions (including aggregates) are also matched.
    /// </summary>
    public Func<Exception, bool>? DeadLetterWhen { get; set; }

    /// <summary>
    /// Maps a notification type to a stable stored name, decoupling the database payload from
    /// CLR type names (which break on rename/move). Unmapped types fall back to
    /// <see cref="System.Type.FullName"/>.
    /// </summary>
    public OutboxOptions MapNotification<TNotification>(string storedName)
        where TNotification : INotification
    {
        if (_typesByName.TryGetValue(storedName, out var existing) && existing != typeof(TNotification))
            throw new ArgumentException(
                $"Stored name '{storedName}' is already mapped to '{existing.FullName}'.", nameof(storedName));

        _namesByType[typeof(TNotification)] = storedName;
        _typesByName[storedName] = typeof(TNotification);
        return this;
    }

    internal string GetStoredName(Type notificationType)
        => _namesByType.TryGetValue(notificationType, out var name)
            ? name
            : notificationType.FullName
              ?? throw new InvalidOperationException(
                  $"Notification type '{notificationType}' has no FullName; " +
                  "map it explicitly via OutboxOptions.MapNotification<T>(...).");

    internal Type ResolveType(string storedName)
    {
        if (_typesByName.TryGetValue(storedName, out var mapped))
            return mapped;

        return _resolutionCache.GetOrAdd(storedName, name =>
        {
            var match = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Select(a => a.GetType(name, throwOnError: false))
                .FirstOrDefault(t => t is not null);

            return match ?? throw new InvalidOperationException(
                $"Outbox message type '{name}' could not be resolved. If the notification was " +
                "renamed or moved, map its old stored name via OutboxOptions.MapNotification<T>(...).");
        });
    }
}
