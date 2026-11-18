using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

/// <summary>
/// Audit timestamps combined with soft delete — proves a soft delete is not stamped as a
/// modification.
/// </summary>
public class AuditTimesSoftDeleteBooking : IEntity, IHasAuditTimes, ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
