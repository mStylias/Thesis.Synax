using Synax.BookingDemoApp.Domain.Entities;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Contracts.Bookings;

/// <summary>
/// Entity → response map. Automapping (on by default) binds every same-named member, including
/// the enum→string conversion for <c>Status</c> — no explicit <c>For</c> needed.
/// </summary>
public class BookingsResponseProfile : MapperProfile
{
    public BookingsResponseProfile()
    {
        CreateMap<Booking, BookingResponse>();
    }
}
