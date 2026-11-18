namespace Synax.EntityFrameworkCore.Abstractions.Auditing;

/// <summary>
/// Tracks which user last modified an entity. <c>ModifiedById</c> is stamped on every tracked
/// update from <see cref="IAuditUserProvider"/>.
/// </summary>
/// <typeparam name="TUser">The type of the user entity.</typeparam>
/// <typeparam name="TUserKey">The type of the user entity's key.</typeparam>
public interface IModifiedBy<TUser, TUserKey>
    where TUser : class
{
    /// <summary>The unique identifier of the user who last modified this entity.</summary>
    TUserKey ModifiedById { get; set; }

    /// <summary>The user entity who last modified this record.</summary>
    TUser? ModifiedBy { get; set; }
}
