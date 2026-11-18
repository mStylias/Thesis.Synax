using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

public class OwnedBooking : IEntity, IHasOwner<int>
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int OwnerId { get; set; }
}
