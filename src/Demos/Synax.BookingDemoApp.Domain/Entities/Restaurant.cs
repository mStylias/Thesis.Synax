using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.BookingDemoApp.Domain.Entities;

/// <summary>
/// The flagship fully-decorated entity: tenant-owned, fully audited (times + users), and
/// soft-deletable with user tracking. All of those columns are stamped by interceptors —
/// application code never sets them.
/// </summary>
public class Restaurant :
    IEntity,
    IHasOwner<Guid>,
    IAuditedEntity<AppUser, Guid?>,
    ISoftDeleteEntity<AppUser, Guid?>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid CuisineId { get; set; }
    public Cuisine? Cuisine { get; set; }
    public int MaxCapacity { get; set; }
    public List<Table> Tables { get; set; } = [];

    // Ownership (tenant scoping) — stamped by the ownership interceptor.
    public Guid OwnerId { get; set; }

    // Auditing — stamped by the auditing interceptor.
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public Guid? ModifiedById { get; set; }
    public AppUser? ModifiedBy { get; set; }

    // Soft delete — stamped by the soft-delete interceptor.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public AppUser? DeletedBy { get; set; }
}
