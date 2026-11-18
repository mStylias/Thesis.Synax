using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;

namespace Synax.EntityFrameworkCore.IntegrationTests.TestModels;

/// <summary>
/// Dedicated entity for the targeted-update soft delete tests, so committed rows
/// cannot interfere with other test classes querying shared tables.
/// </summary>
public class ConcurrentSoftDeleteBooking : IEntity, ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
}
