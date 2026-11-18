namespace Synax.BookingDemoApp.Application.Contracts.MenuItems;

/// <summary>
/// The menu item wire shape. A positional record on purpose — showcases the mapper's
/// record-constructor binding (automap binds ctor parameters by name, case-insensitive).
/// <c>Category</c> is a string; the mapper's enum→string strategy converts it automatically.
/// </summary>
public record MenuItemResponse(Guid Id, Guid RestaurantId, string Name, decimal Price, string Category, bool IsAvailable);
