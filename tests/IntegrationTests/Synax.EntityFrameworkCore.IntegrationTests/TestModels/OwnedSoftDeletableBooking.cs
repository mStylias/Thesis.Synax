using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

public class OwnedSoftDeletableBooking : IEntity, ISoftDelete, IHasOwner<int>
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public int OwnerId { get; set; }
}
