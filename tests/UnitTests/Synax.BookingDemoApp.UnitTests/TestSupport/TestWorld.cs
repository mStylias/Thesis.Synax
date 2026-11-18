using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Mapping;
using Synax.BookingDemoApp.Application.Contracts.Bookings;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.BookingDemoApp.Application.Contracts.MenuItems;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Reviews;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.EntityFrameworkCore.Mapper;
using Synax.EntityFrameworkCore.Mediator;
using Synax.EntityFrameworkCore.Testing;
using Synax.Mapper.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.UnitTests.TestSupport;

/// <summary>A <see cref="TimeProvider"/> pinned to a fixed instant — deterministic clocks in tests.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// A recording <see cref="IOutbox{TDbContext}"/>: collects enqueued notifications so tests can
/// assert what a handler staged for post-commit publication — no channel, dispatcher, or database.
/// </summary>
public sealed class FakeOutbox : IOutbox<BookingContext>
{
    public List<INotification> Enqueued { get; } = [];

    public Task EnqueueAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        Enqueued.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shared plumbing for the demo-app unit tests: the full demo <see cref="MapperConfiguration"/>
/// (every Application request profile + every Infrastructure response profile — the same set the
/// real app scans at startup) and a factory for a fully wired
/// <see cref="FakeUnitOfWork{TDbContext}"/> — no database, no Docker, no DI container.
/// </summary>
public static class TestWorld
{
    /// <summary>Builds the demo's complete mapper configuration, profile by profile.</summary>
    public static MapperConfiguration BuildMapperConfiguration()
        => new(cfg =>
        {
            // Application: request → entity profiles (+ the global DateTime→string converter).
            cfg.AddProfile<AuthResponseProfile>();
            cfg.AddProfile<BookingsRequestProfile>();
            cfg.AddProfile<CuisinesRequestProfile>();
            cfg.AddProfile<MenuItemsRequestProfile>();
            cfg.AddProfile<RestaurantsRequestProfile>();
            cfg.AddProfile<ReviewsRequestProfile>();
            cfg.AddProfile<TablesRequestProfile>();

            // Infrastructure: entity → response profiles.
            cfg.AddProfile<BookingsResponseProfile>();
            cfg.AddProfile<CuisinesResponseProfile>();
            cfg.AddProfile<MenuItemsResponseProfile>();
            cfg.AddProfile<RestaurantsResponseProfile>();
            cfg.AddProfile<ReviewsResponseProfile>();
            cfg.AddProfile<TablesResponseProfile>();
        });

    /// <summary>The shared, immutable mapper built from the full demo configuration.</summary>
    public static IMapper Mapper { get; } = BuildMapperConfiguration().CreateMapper();

    /// <summary>
    /// Creates a <see cref="FakeUnitOfWork{TDbContext}"/> whose options mirror the real app's
    /// DbContext registration: ambient tenant (ownership), ambient user (auditing/soft delete),
    /// a clock, and the Synax.Mapper bridge for <c>QueryDto</c>/<c>AddDtoAsync</c>.
    /// </summary>
    public static FakeUnitOfWork<BookingContext> CreateUow(
        Guid? tenant = null, Guid? user = null, TimeProvider? clock = null)
        => new(new FakeUnitOfWorkOptions
        {
            CurrentOwnerId = () => (object?)tenant,
            CurrentUserId = () => (object?)user,
            TimeProvider = clock ?? TimeProvider.System,
            Mapper = new SynaxMapperRepositoryMapper(Mapper)
        });
}
