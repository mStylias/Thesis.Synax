using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mediator;

namespace Synax.EntityFrameworkCore.Mediator.UnitTests.TestFixtures.Outbox;

public sealed class OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options)
    : DbContext(options), ISynaxContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSynaxOutbox<OutboxTestDbContext>();
        modelBuilder.Entity<OutboxTestEntity>().HasKey(e => e.Id);

        // Sqlite cannot compare/order DateTimeOffset columns (the sweep query does both);
        // store them as ticks. Ordering stays correct because all outbox timestamps are UTC.
        modelBuilder.Entity<OutboxMessage<OutboxTestDbContext>>()
            .Property(m => m.CreatedAt)
            .HasConversion(new DateTimeOffsetToBinaryConverter());
        modelBuilder.Entity<OutboxMessage<OutboxTestDbContext>>()
            .Property(m => m.NextAttemptAt)
            .HasConversion(new DateTimeOffsetToBinaryConverter());
    }
}

public sealed class OutboxTestEntity : IEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed record OutboxTestNotification(Guid MarkerId, string Text) : INotification;

public sealed record AlwaysFailingNotification(Guid MarkerId) : INotification;

/// <summary>Singleton spy recording which notifications reached their handlers.</summary>
public sealed class NotificationRecorder
{
    private readonly List<OutboxTestNotification> _received = [];
    private int _failingAttempts;
    private readonly Lock _lock = new();

    public void Record(OutboxTestNotification notification)
    {
        lock (_lock) _received.Add(notification);
    }

    public void RecordFailingAttempt() => Interlocked.Increment(ref _failingAttempts);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _attemptsByMarker = new();

    /// <summary>Per-marker attempt counter for handlers that always throw.</summary>
    public void RecordAttempt(Guid markerId) => _attemptsByMarker.AddOrUpdate(markerId, 1, (_, n) => n + 1);

    public int AttemptsFor(Guid markerId) => _attemptsByMarker.GetValueOrDefault(markerId);

    public IReadOnlyList<OutboxTestNotification> Received
    {
        get { lock (_lock) return _received.ToList(); }
    }

    public int FailingAttempts => Volatile.Read(ref _failingAttempts);
}

public sealed class OutboxTestNotificationHandler(NotificationRecorder recorder)
    : INotificationHandler<OutboxTestNotification>
{
    public Task HandleAsync(OutboxTestNotification notification, CancellationToken cancellationToken)
    {
        recorder.Record(notification);
        return Task.CompletedTask;
    }
}

// ---------- Dead-letter fixtures ----------

/// <summary>An exception type the outbox knows nothing about — matched via DeadLetterWhen.</summary>
public sealed class TestPermanentException(string message) : Exception(message);

public sealed record DeadLetterNotification(Guid MarkerId) : INotification;

public sealed record PermanentByPredicateNotification(Guid MarkerId) : INotification;

public sealed record WrappedDeadLetterNotification(Guid MarkerId) : INotification;

public sealed class DeadLetterNotificationHandler(NotificationRecorder recorder)
    : INotificationHandler<DeadLetterNotification>
{
    public Task HandleAsync(DeadLetterNotification notification, CancellationToken cancellationToken)
    {
        recorder.RecordAttempt(notification.MarkerId);
        throw new OutboxDeadLetterException("Recipient is permanently invalid");
    }
}

public sealed class PermanentByPredicateNotificationHandler(NotificationRecorder recorder)
    : INotificationHandler<PermanentByPredicateNotification>
{
    public Task HandleAsync(PermanentByPredicateNotification notification, CancellationToken cancellationToken)
    {
        recorder.RecordAttempt(notification.MarkerId);
        throw new TestPermanentException("Downstream rejected the payload for good");
    }
}

public sealed class WrappedDeadLetterNotificationHandler(NotificationRecorder recorder)
    : INotificationHandler<WrappedDeadLetterNotification>
{
    public Task HandleAsync(WrappedDeadLetterNotification notification, CancellationToken cancellationToken)
    {
        recorder.RecordAttempt(notification.MarkerId);
        throw new InvalidOperationException(
            "Handler wrapper failure",
            new OutboxDeadLetterException("Inner permanent failure"));
    }
}

public sealed class AlwaysFailingNotificationHandler(NotificationRecorder recorder)
    : INotificationHandler<AlwaysFailingNotification>
{
    public Task HandleAsync(AlwaysFailingNotification notification, CancellationToken cancellationToken)
    {
        recorder.RecordFailingAttempt();
        throw new InvalidOperationException("Outbox handler exploded");
    }
}

/// <summary>
/// Full end-to-end harness: shared-cache in-memory Sqlite (each DbContext instance gets its
/// own connection to the same database, safe under the dispatcher's background scopes),
/// real interceptor, real mediator, real dispatcher hosted service.
/// </summary>
public sealed class OutboxTestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly List<IHostedService> _started = [];

    private OutboxTestHarness(SqliteConnection keepAlive, ServiceProvider provider)
    {
        _keepAlive = keepAlive;
        _provider = provider;
    }

    public static async Task<OutboxTestHarness> CreateAsync(Action<OutboxOptions>? configure = null)
    {
        // Named shared-cache in-memory database: alive while the keep-alive connection is open,
        // unique per harness so tests cannot see each other's data.
        var connectionString = $"Data Source=outbox-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseSqlite(connectionString)
            .AddOutboxInterceptor<OutboxTestDbContext, OutboxTestDbContext>());
        services.AddSynaxEfCore(opts => opts
            .ForDbContext<OutboxTestDbContext>()
            .ScanNamespaceOf<OutboxTestEntity>());
        services.AddSynaxMediator(opts => opts.AddHandlersFromAssemblyContaining<OutboxTestDbContext>());
        services.AddSingleton<NotificationRecorder>();
        services.AddSynaxOutbox<OutboxTestDbContext>(configure);

        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.EnsureCreatedAsync();
        }

        return new OutboxTestHarness(keepAlive, provider);
    }

    public NotificationRecorder Recorder => _provider.GetRequiredService<NotificationRecorder>();

    public IServiceScope NewScope() => _provider.CreateScope();

    public async Task StartDispatcherAsync()
    {
        foreach (var hostedService in _provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
            _started.Add(hostedService);
        }
    }

    public async Task<List<OutboxMessage<OutboxTestDbContext>>> GetOutboxRowsAsync()
    {
        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        return await context.Set<OutboxMessage<OutboxTestDbContext>>().AsNoTracking().ToListAsync();
    }

    /// <summary>Inserts a row directly, bypassing IOutbox — no channel signal is produced,
    /// which is exactly the "lost signal / crashed instance" scenario the sweep recovers.</summary>
    public async Task InsertRowDirectlyAsync(OutboxMessage<OutboxTestDbContext> row)
    {
        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        context.Add(row);
        await context.SaveChangesAsync();
    }

    /// <summary>Polls the condition until it holds or the timeout elapses; returns the final evaluation.</summary>
    public static async Task<bool> PollUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }

        return await condition();
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = _started.Count - 1; i >= 0; i--)
            await _started[i].StopAsync(CancellationToken.None);

        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}
