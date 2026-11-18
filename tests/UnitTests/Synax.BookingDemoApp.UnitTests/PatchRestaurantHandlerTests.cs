using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.UnitTests.TestSupport;
using Synax.EntityFrameworkCore.Abstractions.Patching;
using Synax.Mediator;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases the merge-patch flow: a <c>PatchValue&lt;T&gt;</c> request applied through
/// <c>ExecutePatchAsync</c> — only provided members are written (matched by name, no mapper
/// profile), modification audit members are stamped by the executor, and the fake emulates all
/// of it in-memory so the whole handler is unit-testable.
/// </summary>
public class PatchRestaurantHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static Restaurant SeededRestaurant() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Old Name",
        Address = "1 Old Road",
        PhoneNumber = "555-0100",
        CuisineId = Guid.NewGuid(),
        MaxCapacity = 30,
        OwnerId = Tenant,
        CreatedAt = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Test]
    public async Task UnknownRestaurant_FailsWithRestaurantNotFound()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant);
        var handler = new PatchRestaurantHandler(uow);

        var result = await handler.ValidateAndHandleAsync(
            new PatchRestaurantRequest { Id = Guid.NewGuid(), Name = "New" });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo(ErrorCodes.Restaurants.NotFound);
    }

    [Test]
    public async Task ProvidedMembersOnly_AreApplied_AndAuditIsStamped()
    {
        var userId = Guid.NewGuid();
        var uow = TestWorld.CreateUow(tenant: Tenant, user: userId, clock: new FixedTimeProvider(FixedNow));
        var restaurant = SeededRestaurant();
        uow.Seed(restaurant);
        var handler = new PatchRestaurantHandler(uow);

        // Only MaxCapacity is present in the "body" — everything else stays untouched.
        var result = await handler.ValidateAndHandleAsync(
            new PatchRestaurantRequest { Id = restaurant.Id, MaxCapacity = 80 });

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.MaxCapacity).IsEqualTo(80);

        var committed = uow.Entities<Restaurant>().Single();
        await Assert.That(committed.MaxCapacity).IsEqualTo(80);
        await Assert.That(committed.Name).IsEqualTo("Old Name");
        await Assert.That(committed.Address).IsEqualTo("1 Old Road");
        await Assert.That(committed.PhoneNumber).IsEqualTo("555-0100");
        // The executor stamps modification audit members — ExecuteUpdate bypasses the interceptor.
        await Assert.That(committed.UpdatedAt).IsEqualTo(FixedNow.UtcDateTime);
        await Assert.That(committed.ModifiedById).IsEqualTo(userId);
        await Assert.That(committed.CreatedAt).IsEqualTo(new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task ProvidedButInvalidMember_FailsValidation_AndChangesNothing()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant);
        var restaurant = SeededRestaurant();
        uow.Seed(restaurant);
        var handler = new PatchRestaurantHandler(uow);

        // Name is present but blank; an *absent* Name would not be an error.
        var result = await handler.ValidateAndHandleAsync(
            new PatchRestaurantRequest { Id = restaurant.Id, Name = "  ", MaxCapacity = 80 });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ValidationErrors!.Single().Code)
            .IsEqualTo(ErrorCodes.Restaurants.InvalidName);
        await Assert.That(uow.Entities<Restaurant>().Single().MaxCapacity).IsEqualTo(30);
    }

    [Test]
    public async Task ProvidedCuisine_MustExist()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant);
        var restaurant = SeededRestaurant();
        uow.Seed(restaurant);
        var handler = new PatchRestaurantHandler(uow);

        var result = await handler.ValidateAndHandleAsync(
            new PatchRestaurantRequest { Id = restaurant.Id, CuisineId = Guid.NewGuid() });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ValidationErrors!.Single().Code)
            .IsEqualTo(ErrorCodes.Restaurants.UnknownCuisine);
    }

    [Test]
    public async Task ExplicitNull_IsSetToNull_NotUnset()
    {
        // The three-state contract at the request level: Of(null) is "clear", default is "absent".
        var set = PatchValue<string>.Of(null);
        var unset = PatchValue<string>.Unset;

        await Assert.That(set.IsSet).IsTrue();
        await Assert.That(set.Value).IsNull();
        await Assert.That(unset.IsSet).IsFalse();
    }
}
