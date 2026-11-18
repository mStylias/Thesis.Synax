namespace Synax.EntityFrameworkCore.Abstractions.SoftDelete;

/// <summary>
/// Provides properties to track which user performed a logical delete.
/// </summary>
public interface IDeletedBy<TUser, TUserKey>
    where TUser : class
{
    /// <summary>
    /// The unique identifier of the user who deleted this entity.
    /// </summary>
    TUserKey DeletedById { get; set; }

    /// <summary>
    /// The user entity who deleted this record.
    /// </summary>
    TUser? DeletedBy { get; set; }
}
