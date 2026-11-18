using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Contracts.Cuisines;

/// <summary>
/// Entity → response map with a computed binding: <c>IsGlobal</c> derives from the optional
/// owner (<c>OwnerId == null</c> means the row is a global cuisine visible to all tenants).
/// Everything else automaps by name.
/// </summary>
public class CuisinesResponseProfile : MapperProfile
{
    public CuisinesResponseProfile()
    {
        CreateMap<Cuisine, CuisineResponse>()
            .For(d => d.IsGlobal, s => s.OwnerId == null);
    }
}
