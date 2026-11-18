namespace Synax.EntityFrameworkCore.Abstractions.Auditing;

/// <summary>
/// Tracks when an entity was last modified. Stamped automatically on every tracked update by the
/// auditing interceptor; <c>null</c> until the first modification. Soft deletes are not counted
/// as modifications (the deletion time records those).
/// </summary>
public interface IHasModificationTime
{
    /// <summary>The UTC time of the last modification, or <c>null</c> if never modified.</summary>
    DateTime? UpdatedAt { get; set; }
}
