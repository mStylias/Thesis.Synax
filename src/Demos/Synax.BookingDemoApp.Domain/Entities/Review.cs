using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.BookingDemoApp.Domain.Entities;

/// <summary>
/// A customer review. Tracks only who created it (<see cref="ICreatedBy{TUser,TUserKey}"/>);
/// reviews are never modified, so there is no modification tracking.
/// </summary>
public class Review :
    IEntity,
    IHasOwner<Guid>,
    IHasCreationTime,
    ICreatedBy<AppUser, Guid?>
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;

    /// <summary>Where the "thanks for your review" email goes — the outbox dead-letter demo.</summary>
    public string ReviewerEmail { get; set; } = string.Empty;

    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
}
