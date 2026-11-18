using Synax.BookingDemoApp.Domain;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves <c>ExecutePatchAsync</c> against real SQL: the compiled per-member
/// <c>SetProperty</c> calls translate to a single UPDATE, only the provided
/// <c>PatchValue&lt;T&gt;</c> members are written, modification audit columns are stamped by
/// the executor (ExecuteUpdate bypasses the auditing interceptor), and the ownership query
/// filter scopes the statement — all dispatched through the real mediator pipeline.
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class PatchSqlTests(BookingFixture fixture)
{
    [Test]
    public async Task PatchRestaurant_WritesOnlyProvidedMembers_AndStampsAudit()
    {
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        // The user must exist before audited rows reference it (ModifiedById is a real FK).
        fixture.ActAsNobody();
        await fixture.SeedAsync(
            TestData.User(userId, tenant),
            TestData.Cuisine(cuisineId, ownerId: tenant),
            TestData.Restaurant(restaurantId, tenant, cuisineId));

        fixture.ActAs(userId, tenant);
        var before = await GetRestaurantAsync(restaurantId);

        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new PatchRestaurantRequest
        {
            Id = restaurantId,
            Name = "Patched Name",
            MaxCapacity = 123
        });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Name).IsEqualTo("Patched Name");
        await Assert.That(result.Data!.MaxCapacity).IsEqualTo(123);

        // Fresh query: the UPDATE bypassed the change tracker, so this is real database state.
        var after = await GetRestaurantAsync(restaurantId);
        await Assert.That(after.Name).IsEqualTo("Patched Name");
        await Assert.That(after.MaxCapacity).IsEqualTo(123);
        // Unset members kept their stored values.
        await Assert.That(after.Address).IsEqualTo(before.Address);
        await Assert.That(after.PhoneNumber).IsEqualTo(before.PhoneNumber);
        await Assert.That(after.CuisineId).IsEqualTo(before.CuisineId);
        // The executor stamped the modification audit columns in the same UPDATE.
        await Assert.That(after.UpdatedAt).IsNotNull();
        await Assert.That(after.ModifiedById).IsEqualTo(userId);
    }

    [Test]
    public async Task PatchRestaurant_OfAnotherTenant_IsNotFound_ViaOwnershipFilter()
    {
        var owner = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsTenant(owner);
        await fixture.SeedAsync(
            TestData.Cuisine(cuisineId, ownerId: owner),
            TestData.Restaurant(restaurantId, owner, cuisineId));

        // The other tenant's UPDATE matches zero rows — the ownership filter is in the WHERE.
        fixture.ActAsTenant(otherTenant);
        await using var scope = fixture.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.SendAsync(new PatchRestaurantRequest
        {
            Id = restaurantId,
            Name = "Hijacked"
        });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo(ErrorCodes.Restaurants.NotFound);

        fixture.ActAsTenant(owner);
        var untouched = await GetRestaurantAsync(restaurantId);
        await Assert.That(untouched.Name).IsNotEqualTo("Hijacked");
    }

    private async Task<Restaurant> GetRestaurantAsync(Guid id)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        return await unitOfWork.GetRepository<Restaurant>()
            .Query(q => q.Where(r => r.Id == id))
            .FirstAsync();
    }
}
