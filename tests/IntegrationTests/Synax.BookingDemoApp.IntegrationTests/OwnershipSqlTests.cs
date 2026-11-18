using Synax.BookingDemoApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves tenant isolation against real SQL: the ownership query filters registered by
/// <c>ApplyOwnershipQueryFilters</c> in BookingDbContext re-evaluate the ambient
/// <c>IOwnerProvider&lt;Guid&gt;</c> per query (no baked-in constant in cached SQL), and the
/// optional-owner semantics of the shared Cuisine lookup (null owner = visible to every tenant).
/// The in-memory fakes emulate these filters; this proves the translated SQL does the same.
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class OwnershipSqlTests(BookingFixture fixture)
{
    [Test]
    public async Task Restaurant_QueriedByAnotherTenant_ShouldBeInvisible_AndVisibleAgainToItsOwner()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenantA);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenantA, cuisineId));

        // Tenant A sees its own restaurant.
        await Assert.That(await FindRestaurantAsync(restaurantId)).IsNotNull();

        // Tenant B gets nothing — the same SQL, re-scoped per query by the owner accessor.
        fixture.ActAsTenant(tenantB);
        await Assert.That(await FindRestaurantAsync(restaurantId)).IsNull();

        // Back to tenant A: visible again.
        fixture.ActAsTenant(tenantA);
        await Assert.That(await FindRestaurantAsync(restaurantId)).IsNotNull();
    }

    [Test]
    public async Task Cuisine_WithOptionalOwner_SharedRowVisibleToAllTenants_CustomRowOnlyToItsOwner()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sharedCuisineId = Guid.NewGuid();
        var customCuisineId = Guid.NewGuid();

        // Shared row: seeded with no ambient owner so OwnerId stays null.
        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(sharedCuisineId));

        // Tenant A's custom cuisine.
        fixture.ActAsTenant(tenantA);
        await fixture.SeedAsync(TestData.Cuisine(customCuisineId, ownerId: tenantA));

        var visibleToA = await GetCuisineIdsAsync(sharedCuisineId, customCuisineId);
        await Assert.That(visibleToA).Contains(sharedCuisineId);
        await Assert.That(visibleToA).Contains(customCuisineId);

        // Tenant B sees the shared row but not tenant A's custom one.
        fixture.ActAsTenant(tenantB);
        var visibleToB = await GetCuisineIdsAsync(sharedCuisineId, customCuisineId);
        await Assert.That(visibleToB).Contains(sharedCuisineId);
        await Assert.That(visibleToB).DoesNotContain(customCuisineId);
    }

    private async Task<Restaurant?> FindRestaurantAsync(Guid restaurantId)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        return await unitOfWork.GetRepository<Restaurant>()
            .Query(q => q.Where(r => r.Id == restaurantId))
            .FirstOrDefaultAsync();
    }

    private async Task<List<Guid>> GetCuisineIdsAsync(params Guid[] candidateIds)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        var cuisines = await unitOfWork.GetRepository<Cuisine>()
            .Query(q => q.Where(c => candidateIds.Contains(c.Id)))
            .ToListAsync();

        return cuisines.Select(c => c.Id).ToList();
    }
}
