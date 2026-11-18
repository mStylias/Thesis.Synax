using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Ownership;

namespace Synax.BookingDemoApp.Domain.Entities;

/// <summary>
/// A shared lookup with optional ownership: rows with <c>OwnerId == null</c> are global and
/// visible to every tenant; tenants can also add their own custom cuisines, visible only to them.
/// </summary>
public class Cuisine :
    IEntity,
    IHasOptionalOwner<Guid?>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid? OwnerId { get; set; }
}
