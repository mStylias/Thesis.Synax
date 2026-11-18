using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Contracts.MenuItems;

public class MenuItemsResponseProfile : MapperProfile
{
    public MenuItemsResponseProfile()
    {
        CreateMap<MenuItem, MenuItemResponse>();
    }
}
