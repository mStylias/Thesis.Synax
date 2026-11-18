using Synax.BookingDemoApp.Domain.Enums;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.BookingDemoApp.Domain.Entities;

/// <summary>
/// Tenant-owned and fully audited, but deliberately not soft-deletable: cancelling a booking is
/// a status change (<see cref="BookingStatus.Cancelled"/>), not a delete.
/// </summary>
public class Booking :
    IEntity,
    IHasOwner<Guid>,
    IAuditedEntity<AppUser, Guid?>
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public int PartySize { get; set; }
    public BookingStatus Status { get; set; }
    public string? Notes { get; set; }

    public Guid OwnerId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public Guid? ModifiedById { get; set; }
    public AppUser? ModifiedBy { get; set; }
}
