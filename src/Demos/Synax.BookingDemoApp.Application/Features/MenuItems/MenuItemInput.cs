using Synax.BookingDemoApp.Domain.Enums;

namespace Synax.BookingDemoApp.Application.Features.MenuItems;

/// <summary>
/// Shared input shape for a single menu item — used flat on <c>CreateMenuItemRequest</c>
/// and as the list element of <c>SeedMenuRequest.Items</c>.
/// </summary>
public class MenuItemInput
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public MenuCategory Category { get; set; }
    public bool IsAvailable { get; set; } = true;
}
