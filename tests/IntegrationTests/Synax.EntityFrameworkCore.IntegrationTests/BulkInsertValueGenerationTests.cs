using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Proves that bulk insert runs EF's client-side value generators (Guid keys), which the change
/// tracker would normally run — instead of silently inserting <c>Guid.Empty</c>.
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class BulkInsertValueGenerationTests(BookingDatabaseFixture fixture)
{
    [Test]
    public async Task BulkInsertAsync_WithClientGeneratedGuidKeys_GeneratesValues()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<GuidKeyedBooking>();

        var bookings = Enumerable.Range(1, 10)
            .Select(i => new GuidKeyedBooking { Name = $"GuidBulk {i}" })
            .ToList();

        await repo.BulkInsertAsync(bookings);

        // Keys were generated client-side before the COPY, and are unique.
        await Assert.That(bookings.All(b => b.Id != Guid.Empty)).IsTrue();
        await Assert.That(bookings.Select(b => b.Id).Distinct().Count()).IsEqualTo(10);

        var stored = await repo.Query(q => q.Where(b => b.Name.StartsWith("GuidBulk"))).ToListAsync();
        await Assert.That(stored).Count().IsEqualTo(10);
        await Assert.That(stored.All(b => b.Id != Guid.Empty)).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task BulkInsertAsync_WithExplicitGuidKeys_KeepsExplicitValues()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<GuidKeyedBooking>();

        var explicitId = Guid.NewGuid();
        await repo.BulkInsertAsync([new GuidKeyedBooking { Id = explicitId, Name = "GuidExplicit" }]);

        var stored = await repo.Query(q => q.Where(b => b.Name == "GuidExplicit")).SingleAsync();
        await Assert.That(stored.Id).IsEqualTo(explicitId);

        await unitOfWork.RollbackTransactionAsync();
    }
}
