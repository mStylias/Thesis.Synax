namespace Synax.EntityFrameworkCore.Abstractions.SoftDelete;

/// <summary>
/// Tracks when an entity was soft-deleted.
/// </summary>
public interface IHasDeletionTime
{
    /// <summary>
    /// The deletion time for this entity
    /// </summary>
    DateTime? DeletedAt { get; set; }
}
