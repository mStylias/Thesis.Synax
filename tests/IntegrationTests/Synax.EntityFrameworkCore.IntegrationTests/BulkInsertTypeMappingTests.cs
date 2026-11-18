using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Proves bulk insert goes through EF's relational type mappings and value converters:
/// DateTime columns (stored as <c>timestamp with time zone</c> by Npgsql), enum columns
/// (default int conversion), enum-to-string conversions, and numeric columns with facets.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class BulkInsertTypeMappingTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task BulkInsertAsync_WhenConvertedAndTemporalColumns_ShouldRoundTripValues()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<TypedBooking>();

        var createdAt = new DateTime(2026, 7, 4, 12, 30, 0, DateTimeKind.Utc);
        var bookings = Enumerable.Range(1, 50)
            .Select(i => new TypedBooking
            {
                Name = $"Typed {i}",
                CreatedAt = createdAt,
                Status = BookingStatus.Confirmed,
                StatusText = BookingStatus.Cancelled,
                Price = 19.99m
            })
            .ToList();

        await repo.BulkInsertAsync(bookings);

        var results = await repo.GetListAsync();

        await Assert.That(results).Count().IsEqualTo(50);
        await Assert.That(results.All(b => b.CreatedAt == createdAt)).IsTrue();
        await Assert.That(results.All(b => b.Status == BookingStatus.Confirmed)).IsTrue();
        await Assert.That(results.All(b => b.StatusText == BookingStatus.Cancelled)).IsTrue();
        await Assert.That(results.All(b => b.Price == 19.99m)).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }
}
