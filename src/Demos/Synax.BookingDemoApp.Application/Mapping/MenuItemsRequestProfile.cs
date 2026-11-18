using Synax.BookingDemoApp.Application.Features.MenuItems;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Mapping;

/// <summary>
/// Request → entity maps. Generated members use <c>For</c>; everything the interceptors stamp
/// (ownership, auditing) is explicitly <c>Ignore</c>d so <c>AssertConfigurationIsValid()</c>
/// knows they are intentional.
/// </summary>
public class MenuItemsRequestProfile : MapperProfile
{
    public MenuItemsRequestProfile()
    {
        // Used by AddDtoAsync in CreateMenuItem — RestaurantId maps by name.
        CreateMap<CreateMenuItemRequest, MenuItem>()
            .For(d => d.Id, _ => Guid.CreateVersion7())
            // Stamped by the ownership/auditing interceptors on save:
            .Ignore(d => d.OwnerId)
            .Ignore(d => d.CreatedAt)
            .Ignore(d => d.UpdatedAt);

        // Used by MapList in SeedMenu — RestaurantId is set in the handler.
        CreateMap<MenuItemInput, MenuItem>()
            .For(d => d.Id, _ => Guid.CreateVersion7())
            .Ignore(d => d.RestaurantId)
            // Stamped by the ownership/auditing interceptors on save (BulkInsertAsync
            // applies creation and ownership stamps itself):
            .Ignore(d => d.OwnerId)
            .Ignore(d => d.CreatedAt)
            .Ignore(d => d.UpdatedAt);
    }
}
