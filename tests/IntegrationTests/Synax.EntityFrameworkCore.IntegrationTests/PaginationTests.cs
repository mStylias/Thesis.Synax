using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.IntegrationTests.TestModels;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Covers offset and keyset pagination against real Postgres — including SQL translation of the
/// keyset predicate for both operator-comparable keys (int) and CompareTo-based keys (string).
/// </summary>
[ClassDataSource<BookingDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class PaginationTests(BookingDatabaseFixture fixture)
{
    private static List<Booking> CreateBookings(string prefix, int count)
        => Enumerable.Range(1, count)
            .Select(i => new Booking { Name = $"{prefix} {i:D2}", Description = prefix })
            .ToList();

    [Test]
    public async Task ToPagedListAsync_ReturnsPageAndTotals()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<Booking>();

        foreach (var booking in CreateBookings("PageOffset", 25))
        {
            await repo.AddAsync(booking);
        }

        await unitOfWork.SaveChangesAsync();

        var page = await repo
            .Query(q => q.Where(b => b.Description == "PageOffset").OrderBy(b => b.Name))
            .ToPagedListAsync(page: 2, pageSize: 10);

        await Assert.That(page.TotalCount).IsEqualTo(25);
        await Assert.That(page.TotalPages).IsEqualTo(3);
        await Assert.That(page.Items).Count().IsEqualTo(10);
        await Assert.That(page.Items[0].Name).IsEqualTo("PageOffset 11");
        await Assert.That(page.HasPreviousPage).IsTrue();
        await Assert.That(page.HasNextPage).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ToPagedListAsync_OnDtoProjection_Works()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<Booking>();

        foreach (var booking in CreateBookings("PageDto", 5))
        {
            await repo.AddAsync(booking);
        }

        await unitOfWork.SaveChangesAsync();

        var page = await repo
            .QueryDto<ReferenceViewDto>(q => q.Where(b => b.Description == "PageDto").OrderBy(b => b.Name))
            .ToPagedListAsync(page: 1, pageSize: 3);

        await Assert.That(page.Items).Count().IsEqualTo(3);
        await Assert.That(page.TotalCount).IsEqualTo(5);

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ToKeysetPageAsync_IntKey_WalksAllPagesWithoutSkipsOrDuplicates()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<Booking>();

        foreach (var booking in CreateBookings("PageKeyset", 23))
        {
            await repo.AddAsync(booking);
        }

        await unitOfWork.SaveChangesAsync();

        var seen = new List<int>();
        object? cursor = null;
        KeysetPage<Booking, int> page;

        do
        {
            page = await repo
                .Query(q => q.Where(b => b.Description == "PageKeyset"))
                .ToKeysetPageAsync(b => b.Id, pageSize: 10, after: cursor);
            seen.AddRange(page.Items.Select(b => b.Id));
            cursor = page.NextCursor;
        } while (page.HasMore);

        await Assert.That(seen).Count().IsEqualTo(23);
        await Assert.That(seen.Distinct().Count()).IsEqualTo(23);
        await Assert.That(seen).IsEquivalentTo(seen.OrderBy(id => id).ToList());

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ToKeysetPageAsync_StringKey_TranslatesCompareToInSql()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<Booking>();

        foreach (var booking in CreateBookings("PageString", 5))
        {
            await repo.AddAsync(booking);
        }

        await unitOfWork.SaveChangesAsync();

        var first = await repo
            .Query(q => q.Where(b => b.Description == "PageString"))
            .ToKeysetPageAsync(b => b.Name, pageSize: 2);
        var second = await repo
            .Query(q => q.Where(b => b.Description == "PageString"))
            .ToKeysetPageAsync(b => b.Name, pageSize: 2, after: first.NextCursor);

        await Assert.That(first.Items.Select(b => b.Name)).IsEquivalentTo(["PageString 01", "PageString 02"]);
        await Assert.That(second.Items.Select(b => b.Name)).IsEquivalentTo(["PageString 03", "PageString 04"]);
        await Assert.That(second.HasMore).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }

    [Test]
    public async Task ToKeysetPageAsync_Descending_PagesNewestFirst()
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        await unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        var repo = unitOfWork.GetRepository<Booking>();

        foreach (var booking in CreateBookings("PageDesc", 5))
        {
            await repo.AddAsync(booking);
        }

        await unitOfWork.SaveChangesAsync();

        var page = await repo
            .Query(q => q.Where(b => b.Description == "PageDesc"))
            .ToKeysetPageAsync(b => b.Name, pageSize: 3, descending: true);

        await Assert.That(page.Items.Select(b => b.Name))
            .IsEquivalentTo(["PageDesc 05", "PageDesc 04", "PageDesc 03"]);
        await Assert.That(page.HasMore).IsTrue();

        await unitOfWork.RollbackTransactionAsync();
    }
}
