namespace Synax.EntityFrameworkCore.Abstractions.SoftDelete;

/// <summary>
/// Provides the identity of the current user for populating <see cref="IDeletedBy{TUser,TUserKey}.DeletedById"/>
/// during soft delete operations.
/// </summary>
public interface ISoftDeleteUserProvider
{
    /// <summary>
    /// Returns the current user's identifier, or <c>null</c> if no user is available.
    /// The returned value must be assignable to <c>TUserKey</c> of the entity's
    /// <see cref="IDeletedBy{TUser,TUserKey}"/> implementation.
    /// </summary>
    object? GetCurrentUserId();
}
