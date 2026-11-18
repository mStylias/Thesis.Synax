namespace Synax.EntityFrameworkCore.Abstractions.Auditing;

/// <summary>
/// Tracks which user created an entity. <c>CreatedById</c> is stamped on insert from
/// <see cref="IAuditUserProvider"/> unless a non-default value was set explicitly.
/// </summary>
/// <typeparam name="TUser">The type of the user entity.</typeparam>
/// <typeparam name="TUserKey">The type of the user entity's key.</typeparam>
public interface ICreatedBy<TUser, TUserKey>
    where TUser : class
{
    /// <summary>The unique identifier of the user who created this entity.</summary>
    TUserKey CreatedById { get; set; }

    /// <summary>The user entity who created this record.</summary>
    TUser? CreatedBy { get; set; }
}
