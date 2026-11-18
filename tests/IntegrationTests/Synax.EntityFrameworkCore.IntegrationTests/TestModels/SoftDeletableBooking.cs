using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

public class SoftDeletableBooking : IEntity, ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
}
