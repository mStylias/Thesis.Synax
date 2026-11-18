using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

public class AuditedSoftDeletableBooking : IEntity, ISoftDeleteEntity<TestUser, int?>
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedById { get; set; }
    public TestUser? DeletedBy { get; set; }
}
