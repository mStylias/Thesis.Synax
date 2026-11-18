using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Contracts.Reviews;

/// <summary>
/// Entity → response map. Automapping (on by default) binds every same-named member. This map
/// is also used as an <c>IQueryable</c> projection (<c>GetExpression</c>), so it must stay
/// fully SQL-translatable — no custom delegates.
/// </summary>
public class ReviewsResponseProfile : MapperProfile
{
    public ReviewsResponseProfile()
    {
        CreateMap<Review, ReviewResponse>();
    }
}
