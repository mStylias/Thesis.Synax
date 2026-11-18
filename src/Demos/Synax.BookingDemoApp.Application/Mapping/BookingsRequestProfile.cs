using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.Mapper.Core;

namespace Synax.BookingDemoApp.Application.Mapping;

/// <summary>
/// Request → entity map. Generated/derived members use <c>For</c>; everything the interceptors
/// stamp (ownership, auditing) is explicitly <c>Ignore</c>d so
/// <c>AssertConfigurationIsValid()</c> knows they are intentional.
/// </summary>
public class BookingsRequestProfile : MapperProfile
{
    public BookingsRequestProfile()
    {
        CreateMap<CreateBookingRequest, Booking>()
            .For(d => d.Id, _ => Guid.CreateVersion7())
            .For(d => d.Status, _ => BookingStatus.Confirmed)
            // Stamped by the ownership/auditing interceptors on save:
            .Ignore(d => d.OwnerId)
            .Ignore(d => d.CreatedAt)
            .Ignore(d => d.UpdatedAt)
            .Ignore(d => d.CreatedById)
            .Ignore(d => d.CreatedBy)
            .Ignore(d => d.ModifiedById)
            .Ignore(d => d.ModifiedBy);
    }
}
