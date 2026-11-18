using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.UnitTests.TestSupport;
using Synax.Mediator;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// Showcases: data-carrying validation on an update handler (the tracked entity is fetched once
/// in <c>ValidateAsync</c> and consumed in <c>HandleAsync</c> via <c>this.ValidationData()</c>)
/// and the mapper's update overload <c>Map(request, entity)</c> — the PUT pattern — all driven
/// through <c>ValidateAndHandleAsync</c> against the fake unit of work.
/// </summary>
public class UpdateRestaurantHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

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
        var handler = new UpdateRestaurantHandler(uow, TestWorld.Mapper);

        var result = await handler.ValidateAndHandleAsync(new UpdateRestaurantRequest { Id = Guid.NewGuid() });

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Error!.ErrorCode).IsEqualTo(ErrorCodes.Restaurants.NotFound);
    }

    [Test]
    public async Task ExistingRestaurant_IsUpdatedThroughTheMapperUpdateOverload()
    {
        var uow = TestWorld.CreateUow(tenant: Tenant);
        var restaurant = SeededRestaurant();
        uow.Seed(restaurant);
        var handler = new UpdateRestaurantHandler(uow, TestWorld.Mapper);

        var request = new UpdateRestaurantRequest
        {
            Id = restaurant.Id,
            Name = "New Name",
            Address = "2 New Avenue",
            PhoneNumber = "555-0199",
            CuisineId = restaurant.CuisineId,
            MaxCapacity = 55
        };

        var result = await handler.ValidateAndHandleAsync(request);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Data!.Name).IsEqualTo("New Name");

        // Map(request, entity) copied the editable fields onto the committed entity...
        var committed = uow.Entities<Restaurant>().Single();
        await Assert.That(committed.Name).IsEqualTo("New Name");
        await Assert.That(committed.MaxCapacity).IsEqualTo(55);
        // ...while the Ignored members (identity, audit trail) stayed untouched.
        await Assert.That(committed.Id).IsEqualTo(restaurant.Id);
        await Assert.That(committed.CreatedAt).IsEqualTo(new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
