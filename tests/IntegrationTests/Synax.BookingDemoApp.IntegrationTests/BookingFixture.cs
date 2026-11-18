using Synax.BookingDemoApp.Domain;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Application.Features.Reviews;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Infrastructure;
using Synax.Crud;
using Synax.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Auditing;
using Synax.EntityFrameworkCore.Mapper;
using Synax.EntityFrameworkCore.Mediator;
using Synax.EntityFrameworkCore.Ownership;
using Synax.EntityFrameworkCore.SoftDelete;
using Synax.Mapper;
using Synax.Mediator;
using Synax.Tests.Common;
using ApplicationAnchor = Synax.BookingDemoApp.Application.Auth.JwtOptions;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Mutable ambient identity for the whole fixture: tests switch tenant/user via
/// <see cref="BookingFixture.ActAs"/> and the interceptors/query filters pick the change up
/// per save / per query through the provider singletons below.
/// </summary>
public sealed class TestIdentity
{
    public Guid? OwnerId { get; set; }
    public Guid? UserId { get; set; }
}

public sealed class TestIdentityOwnerProvider(TestIdentity identity) : IOwnerProvider<Guid>
{
    public object? GetCurrentOwnerId() => identity.OwnerId;
}

public sealed class TestIdentityAuditUserProvider(TestIdentity identity) : IAuditUserProvider
{
    public object? GetCurrentUserId() => identity.UserId;
}

public sealed class TestIdentitySoftDeleteUserProvider(TestIdentity identity) : ISoftDeleteUserProvider
{
    public object? GetCurrentUserId() => identity.UserId;
}

/// <summary>
/// Records the SQL EF actually sends, so a test can assert on the emitted statement rather than
/// only on the rows it returns — the difference between "the filter worked" and "the filter ran in
/// the database". Query for the statement you care about (<see cref="LastMentioning"/>) instead of
/// assuming the last command is yours: the outbox dispatcher may be sweeping in the background.
/// </summary>
public sealed class SqlRecorder : DbCommandInterceptor
{
    private const int Capacity = 200;

    private readonly Lock _gate = new();
    private readonly List<string> _commands = [];

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <summary>The most recent statement containing <paramref name="fragment"/>, ignoring case.</summary>
    public string LastMentioning(string fragment)
    {
        lock (_gate)
        {
            for (var index = _commands.Count - 1; index >= 0; index--)
                if (_commands[index].Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return _commands[index];
        }

        throw new InvalidOperationException($"No recorded SQL mentions '{fragment}'.");
    }

    private void Record(string commandText)
    {
        lock (_gate)
        {
            _commands.Add(commandText);
            if (_commands.Count > Capacity)
                _commands.RemoveRange(0, _commands.Count - Capacity);
        }
    }
}

/// <summary>
/// Real-PostgreSQL fixture for the demo app: the full BookingDbContext (ownership + soft-delete
/// query filters, auditing/soft-delete/ownership/outbox interceptors), the real mediator with the
/// demo's handlers, and the real outbox dispatcher (started on demand by outbox tests).
/// Mirrors what BookingModule and AddInfrastructure register, wired directly so the fixture can
/// vary timings and identity without going through the host.
/// </summary>
public sealed class BookingFixture : DatabaseFixture<BookingDbContext>, IAsyncDisposable
{
    /// <summary>Outbox attempts before dead-lettering — kept small so tests observe it fast.</summary>
    public const int OutboxMaxAttempts = 2;

    private readonly SemaphoreSlim _dispatcherGate = new(1, 1);
    private readonly List<IHostedService> _startedServices = [];

    public TestIdentity Identity { get; } = new();

    /// <summary>The SQL EF emitted, for tests that assert translation and not just results.</summary>
    public SqlRecorder Sql { get; } = new();

    protected override void ConfigureDbContext(DbContextOptionsBuilder options) => options
        .AddAuditingInterceptor()
        .AddSoftDeleteInterceptor()
        .AddOwnershipInterceptor<Guid>()
        .AddOutboxInterceptor<BookingContext, BookingDbContext>()
        .AddInterceptors(Sql);

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);

        // Ambient identity read by the interceptors (stamping) and the query filters (scoping).
        services.AddSingleton(Identity);
        services.AddSingleton<IOwnerProvider<Guid>>(new TestIdentityOwnerProvider(Identity));
        services.AddSingleton<IAuditUserProvider>(new TestIdentityAuditUserProvider(Identity));
        services.AddSingleton<ISoftDeleteUserProvider>(new TestIdentitySoftDeleteUserProvider(Identity));

        services.AddSynaxEfCore(options => options
            .ForDbContext<BookingDbContext>()
            .As<BookingContext>()
            .ScanNamespaceOf<Booking>());
        services.AddSynaxRepositoryMapper();

        services.AddSynaxMapper(typeof(ApplicationAnchor).Assembly);

        services.AddSynaxMediator(options =>
        {
            options.AddHandlersFromAssembly(typeof(ApplicationAnchor).Assembly);

            // Mirrors what BookingModule declares: Crud*Request subclasses (e.g.
            // Features/Cuisines/CuisineCrud.cs) need a generic handler registered explicitly —
            // assembly scanning alone does not pick them up.
            options.AddSynaxCrud<BookingContext>(typeof(ApplicationAnchor).Assembly);
        });

        // Aggressive sweep timings so retry/orphan recovery is observable within a test.
        services.AddSynaxOutbox<BookingContext>(options =>
        {
            options.MaxAttempts = OutboxMaxAttempts;
            options.SweepInterval = TimeSpan.FromSeconds(1);
            options.GracePeriod = TimeSpan.FromSeconds(1);
            options.MapNotification<BookingCreatedNotification>("booking-created");
            options.MapNotification<ReviewSubmittedNotification>("review-submitted");
        });
    }

    // ---------- Ambient identity helpers ----------

    /// <summary>Acts as a specific user inside a tenant. The user must exist in the users table
    /// when audited entities are saved (CreatedById is a real foreign key).</summary>
    public void ActAs(Guid userId, Guid tenantId)
    {
        Identity.UserId = userId;
        Identity.OwnerId = tenantId;
    }

    /// <summary>Acts as an anonymous member of a tenant — owner scoping applies, no user stamping.</summary>
    public void ActAsTenant(Guid tenantId)
    {
        Identity.UserId = null;
        Identity.OwnerId = tenantId;
    }

    public void ActAsNobody()
    {
        Identity.UserId = null;
        Identity.OwnerId = null;
    }

    // ---------- Outbox dispatcher (hosted service; a plain ServiceProvider never auto-starts it) ----------

    /// <summary>Starts the outbox dispatcher hosted service once; idempotent across tests.</summary>
    public async Task StartDispatcherAsync()
    {
        await _dispatcherGate.WaitAsync();
        try
        {
            if (_startedServices.Count > 0)
                return;

            foreach (var hostedService in Services.GetServices<IHostedService>())
            {
                await hostedService.StartAsync(CancellationToken.None);
                _startedServices.Add(hostedService);
            }
        }
        finally
        {
            _dispatcherGate.Release();
        }
    }

    // ---------- Data helpers ----------

    /// <summary>Saves entities through the real DbContext — the interceptors run, so explicit
    /// OwnerId/CreatedAt values win and defaults are stamped from the ambient identity.</summary>
    public async Task SeedAsync(params object[] entities)
    {
        await using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<OutboxMessage<BookingContext>>> GetOutboxRowsAsync()
    {
        await using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await dbContext.Set<OutboxMessage<BookingContext>>().AsNoTracking().ToListAsync();
    }

    /// <summary>Polls the condition until it holds or the timeout elapses; returns the final evaluation.</summary>
    public static async Task<bool> PollUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(100);
        }

        return await condition();
    }

    public new async ValueTask DisposeAsync()
    {
        for (var i = _startedServices.Count - 1; i >= 0; i--)
            await _startedServices[i].StopAsync(CancellationToken.None);

        await base.DisposeAsync();
    }
}
