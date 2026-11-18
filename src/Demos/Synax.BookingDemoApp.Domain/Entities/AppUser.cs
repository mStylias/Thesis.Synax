using Synax.BookingDemoApp.Domain.Enums;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.BookingDemoApp.Domain.Entities;

/// <summary>
/// A platform user. Deliberately not tenant-scoped: users log in first and their token carries
/// the tenant they act within. Audit navigations (<c>CreatedBy</c>/<c>ModifiedBy</c>/...) on other
/// entities point here.
/// </summary>
public class AppUser : IEntity, IHasAuditTimes
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    /// <summary>The tenant this user belongs to; becomes the <c>tenant_id</c> JWT claim.</summary>
    public Guid TenantId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
