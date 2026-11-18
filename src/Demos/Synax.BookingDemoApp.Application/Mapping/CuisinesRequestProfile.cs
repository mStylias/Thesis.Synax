using Synax.BookingDemoApp.Application.Features.Cuisines;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Mapping;

/// <summary>
/// Request → entity map. The id is generated here; the optional owner is explicitly
/// <c>Ignore</c>d — the ownership interceptor stamps the caller's tenant on save, which is
/// what makes a created cuisine tenant-private.
/// </summary>
public class CuisinesRequestProfile : MapperProfile
{
    public CuisinesRequestProfile()
    {
        CreateMap<CreateCuisineRequest, Cuisine>()
            .For(d => d.Id, _ => Guid.CreateVersion7())
            // Stamped by the ownership interceptor on save:
            .Ignore(d => d.OwnerId);
    }
}
