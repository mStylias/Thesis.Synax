using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Contracts.Restaurants;

/// <summary>
/// Entity → response map. Automapping (on by default) binds every same-named member;
/// <c>CuisineName</c> is the only member that needs an explicit <c>For</c>, and
/// <c>Tables</c> binds automatically through the registered <c>Table</c> → <c>TableResponse</c>
/// element map (see <c>TablesResponseProfile</c>).
/// </summary>
public class RestaurantsResponseProfile : MapperProfile
{
    public RestaurantsResponseProfile()
    {
        CreateMap<Restaurant, RestaurantResponse>();

        CreateMap<Restaurant, RestaurantDetailResponse>()
            .For(d => d.CuisineName, s => s.Cuisine == null ? string.Empty : s.Cuisine.Name);

        CreateMap<Restaurant, RestaurantRecordResponse>()
            .For(d => d.TableCount, s => s.Tables.Count);
    }
}
