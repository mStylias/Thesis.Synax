using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.UnitTests.TestSupport;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: the fake's ownership (multi-tenancy) emulation — the ownership query filter makes
/// other tenants' rows invisible, and adds are stamped with the ambient owner on save, mirroring
/// the real ownership interceptor. Driven through <see cref="GetRestaurantsHandler"/>, which also
/// exercises <c>QueryDto</c> (the mapper projection) + <c>ToPagedListAsync</c> against the fakes.
/// </summary>
public class OwnershipEmulationTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static Restaurant RestaurantFor(Guid tenant, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Address = "1 Demo Street",
        PhoneNumber = "555-0100",
        CuisineId = Guid.NewGuid(),
        MaxCapacity = 40,
        OwnerId = tenant
    };

    [Test]
    public async Task GetRestaurants_ReturnsOnlyTheCurrentTenantsRows()
    {
        var uow = TestWorld.CreateUow(tenant: TenantA);
        uow.Seed(
            RestaurantFor(TenantA, "Alpha Bistro"),
            RestaurantFor(TenantB, "Beta Diner"));
        var handler = new GetRestaurantsHandler(uow);

        var result = await handler.HandleAsync(new GetRestaurantsRequest(), CancellationToken.None);

        // Cross-tenant invisibility: tenant B's restaurant simply does not exist for tenant A.
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.TotalCount).IsEqualTo(1);
        await Assert.That(result.Data!.Items.Single().Name).IsEqualTo("Alpha Bistro");
    }

    [Test]
    public async Task AddAndSave_StampsTheAmbientTenantAsOwner()
    {
        var uow = TestWorld.CreateUow(tenant: TenantA);
        var restaurant = RestaurantFor(TenantA, "Gamma Grill");
        restaurant.OwnerId = default;   // the handler/caller never sets it — the interceptor does

        await uow.GetRepository<Restaurant>().AddAsync(restaurant);
        await uow.SaveChangesAsync();

        await Assert.That(uow.Entities<Restaurant>().Single().OwnerId).IsEqualTo(TenantA);
    }
}
