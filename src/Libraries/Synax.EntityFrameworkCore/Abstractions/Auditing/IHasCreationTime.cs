namespace Synax.EntityFrameworkCore.Abstractions.Auditing;

/// <summary>
/// Tracks when an entity was created. Stamped automatically on insert by the auditing
/// interceptor (and by bulk insert) unless a non-default value was set explicitly.
/// </summary>
public interface IHasCreationTime
{
    /// <summary>The UTC creation time of this entity.</summary>
    DateTime CreatedAt { get; set; }
}
