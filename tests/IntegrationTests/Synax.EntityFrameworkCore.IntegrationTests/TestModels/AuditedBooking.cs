using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

/// <summary>Fully audited entity: creation/modification timestamps plus user tracking.</summary>
public class AuditedBooking : IEntity, IAuditedEntity<TestUser, int?>
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? CreatedById { get; set; }
    public TestUser? CreatedBy { get; set; }
    public int? ModifiedById { get; set; }
    public TestUser? ModifiedBy { get; set; }
}
