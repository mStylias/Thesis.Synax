using Synax.BookingDemoApp.Domain.Enums;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.BookingDemoApp.Domain.Entities;

public class MenuItem :
    IEntity,
    IHasOwner<Guid>,
    IHasAuditTimes
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public MenuCategory Category { get; set; }
    public bool IsAvailable { get; set; } = true;

    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
