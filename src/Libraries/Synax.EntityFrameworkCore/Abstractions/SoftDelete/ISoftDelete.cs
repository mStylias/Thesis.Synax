namespace Synax.EntityFrameworkCore.Abstractions.SoftDelete;

/// <summary>
/// Defines a contract for entities that support "soft deletion," 
/// allowing data to be hidden from application logic without physical removal from the database.
/// </summary>
public interface ISoftDelete
{
    /// <summary>
    /// Whether this entity is soft deleted
    /// </summary>
    bool IsDeleted { get; set; }
}