using Synax.BookingDemoApp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves soft delete against real SQL: <c>AddSoftDeleteInterceptor()</c> converts
/// <c>Remove</c> into an UPDATE stamping <c>IsDeleted</c>/<c>DeletedAt</c> (Table implements the
/// granular <c>ISoftDelete</c> + <c>IHasDeletionTime</c> mix), the automatically-applied query
/// filter excludes the row from translated queries, <c>IgnoreQueryFilters()</c> reveals it, and
/// <c>HardDelete</c> bypasses the conversion with a genuine DELETE.
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class SoftDeleteSqlTests(BookingFixture fixture)
{
    [Test]
    public async Task Remove_OnTable_ShouldSoftDeleteInSql_FilterItOut_AndRevealViaIgnoreQueryFilters()
    {
        var tenant = Guid.NewGuid();
        var (_, tableId) = await SeedRestaurantWithTableAsync(tenant);

        await using (var scope = fixture.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();
            var tableRepo = unitOfWork.GetRepository<Table>();

            var table = await tableRepo
                .Query(q => q.Where(t => t.Id == tableId), asNoTracking: false)
                .SingleAsync();

            tableRepo.Remove(table);
            await unitOfWork.SaveChangesAsync();
        }

        // Fresh scope: the filtered query no longer sees the row...
        await using (var scope = fixture.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();
            var tableRepo = unitOfWork.GetRepository<Table>();

            var visible = await tableRepo.Query(q => q.Where(t => t.Id == tableId)).AnyAsync();
            await Assert.That(visible).IsFalse();

            // ...but it is still in the database, stamped as soft-deleted.
            var deleted = await tableRepo
                .Query(q => q.IgnoreQueryFilters().Where(t => t.Id == tableId))
                .SingleAsync();
            await Assert.That(deleted.IsDeleted).IsTrue();
            await Assert.That(deleted.DeletedAt).IsNotNull();
        }
    }

    [Test]
    public async Task HardDelete_OnTable_ShouldRemoveTheRowForReal()
    {
        var tenant = Guid.NewGuid();
        var (_, tableId) = await SeedRestaurantWithTableAsync(tenant);

        await using (var scope = fixture.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();
            var tableRepo = unitOfWork.GetRepository<Table>();

            var table = await tableRepo
                .Query(q => q.Where(t => t.Id == tableId), asNoTracking: false)
                .SingleAsync();

            tableRepo.HardDelete(table);
            await unitOfWork.SaveChangesAsync();
        }

        // Not even IgnoreQueryFilters finds it: this was a genuine DELETE, not a conversion.
        await using (var scope = fixture.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();
            var exists = await unitOfWork.GetRepository<Table>()
                .Query(q => q.IgnoreQueryFilters().Where(t => t.Id == tableId))
                .AnyAsync();

            await Assert.That(exists).IsFalse();
        }
    }

    private async Task<(Guid RestaurantId, Guid TableId)> SeedRestaurantWithTableAsync(Guid tenant)
    {
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            TestData.Cuisine(cuisineId, ownerId: tenant),
            TestData.Restaurant(restaurantId, tenant, cuisineId),
            TestData.Table(tableId, restaurantId, tenant));

        return (restaurantId, tableId);
    }
}
