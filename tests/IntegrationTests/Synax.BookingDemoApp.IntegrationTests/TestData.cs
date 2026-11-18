using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Entity builders with unique ids per call site. The database container is shared across the
/// whole test session, so every test seeds its own graph under its own tenant and filters its
/// assertions by the ids it created.
/// </summary>
internal static class TestData
{
    public static AppUser User(Guid id, Guid tenantId) => new()
    {
        Id = id,
        Name = $"User {id:N}",
        Email = $"{id:N}@test.demo",
        Role = UserRole.Owner,
        TenantId = tenantId
    };

    /// <summary>OwnerId null = a shared (global) cuisine; seed those while acting as nobody so the
    /// ownership interceptor does not stamp an ambient tenant over the null.</summary>
    public static Cuisine Cuisine(Guid id, Guid? ownerId = null) => new()
    {
        Id = id,
        Name = $"Cuisine {id:N}",
        Description = "Test cuisine",
        OwnerId = ownerId
    };

    public static Restaurant Restaurant(Guid id, Guid ownerId, Guid cuisineId) => new()
    {
        Id = id,
        Name = $"Restaurant {id:N}",
        Address = "1 Test Street",
        PhoneNumber = "555-0100",
        CuisineId = cuisineId,
        MaxCapacity = 60,
        OwnerId = ownerId
    };

    public static Table Table(Guid id, Guid restaurantId, Guid ownerId, int number = 1, int seats = 4) => new()
    {
        Id = id,
        RestaurantId = restaurantId,
        Number = number,
        Seats = seats,
        Location = TableLocation.Indoor,
        OwnerId = ownerId
    };

    public static Booking Booking(
        Guid id, Guid restaurantId, Guid tableId, Guid ownerId,
        DateTimeOffset date, BookingStatus status) => new()
    {
        Id = id,
        RestaurantId = restaurantId,
        TableId = tableId,
        CustomerName = "Test Customer",
        CustomerEmail = $"{id:N}@customers.demo",
        Date = date,
        PartySize = 2,
        Status = status,
        OwnerId = ownerId
    };

    public static MenuItem MenuItem(Guid id, Guid restaurantId, Guid ownerId, decimal price) => new()
    {
        Id = id,
        RestaurantId = restaurantId,
        Name = $"Dish {id:N}",
        Price = price,
        Category = MenuCategory.Main,
        IsAvailable = true,
        OwnerId = ownerId
    };
}
