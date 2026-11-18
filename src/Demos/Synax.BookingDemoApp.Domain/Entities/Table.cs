using Synax.BookingDemoApp.Domain.Enums;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.BookingDemoApp.Domain.Entities;

/// <summary>
/// A lighter interface mix on purpose: creation time and anonymous soft delete (no user
/// tracking) — the Synax interfaces are granular opt-ins, not an all-or-nothing bundle.
/// </summary>
public class Table :
    IEntity,
    IHasOwner<Guid>,
    IHasCreationTime,
    ISoftDelete,
    IHasDeletionTime
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public int Number { get; set; }
    public int Seats { get; set; }
    public TableLocation Location { get; set; }

    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
