using Synax.BookingDemoApp.Application.Features.Restaurants;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Mapping;

/// <summary>
/// Request → entity map. Generated/derived members use <c>For</c>; everything the interceptors
/// stamp (ownership, auditing, soft delete) is explicitly <c>Ignore</c>d so
/// <c>AssertConfigurationIsValid()</c> knows they are intentional.
/// </summary>
public class RestaurantsRequestProfile : MapperProfile
{
    public RestaurantsRequestProfile()
    {
        CreateMap<CreateRestaurantRequest, Restaurant>()
            .For(d => d.Id, _ => Guid.CreateVersion7())
            // Stamped by the ownership/auditing interceptors on save:
            .Ignore(d => d.OwnerId)
            .Ignore(d => d.CreatedAt)
            .Ignore(d => d.UpdatedAt)
            .Ignore(d => d.CreatedById)
            .Ignore(d => d.CreatedBy)
            .Ignore(d => d.ModifiedById)
            .Ignore(d => d.ModifiedBy)
            .Ignore(d => d.IsDeleted)
            .Ignore(d => d.DeletedAt)
            .Ignore(d => d.DeletedById)
            .Ignore(d => d.DeletedBy)
            .Ignore(d => d.Cuisine)
            .Ignore(d => d.Tables);

        // Update (PUT) overload of Map: the route-supplied Id and every interceptor-stamped
        // member are ignored so the existing tracked entity keeps its identity/audit trail.
        CreateMap<UpdateRestaurantRequest, Restaurant>()
            .Ignore(d => d.Id)
            .Ignore(d => d.OwnerId)
            .Ignore(d => d.CreatedAt)
            .Ignore(d => d.UpdatedAt)
            .Ignore(d => d.CreatedById)
            .Ignore(d => d.CreatedBy)
            .Ignore(d => d.ModifiedById)
            .Ignore(d => d.ModifiedBy)
            .Ignore(d => d.IsDeleted)
            .Ignore(d => d.DeletedAt)
            .Ignore(d => d.DeletedById)
            .Ignore(d => d.DeletedBy)
            .Ignore(d => d.Cuisine)
            .Ignore(d => d.Tables);
    }
}
