using Synax.BookingDemoApp.Application.Features.Reviews;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Mapping;

/// <summary>
/// Request → entity map. Generated/derived members use <c>For</c>; everything the interceptors
/// stamp (ownership, auditing) is explicitly <c>Ignore</c>d so
/// <c>AssertConfigurationIsValid()</c> knows they are intentional.
/// </summary>
public class ReviewsRequestProfile : MapperProfile
{
    public ReviewsRequestProfile()
    {
        CreateMap<CreateReviewRequest, Review>()
            .For(d => d.Id, _ => Guid.CreateVersion7())
            // Stamped by the ownership/auditing interceptors on save:
            .Ignore(d => d.OwnerId)
            .Ignore(d => d.CreatedAt)
            .Ignore(d => d.CreatedById)
            .Ignore(d => d.CreatedBy);
    }
}
