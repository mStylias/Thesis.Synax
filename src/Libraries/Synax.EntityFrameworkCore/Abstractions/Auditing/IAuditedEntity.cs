namespace Synax.EntityFrameworkCore.Abstractions.Auditing;

/// <summary>
/// Full auditing contract: creation and modification timestamps plus the users who performed them.
/// </summary>
/// <typeparam name="TUser">The type of the user entity.</typeparam>
/// <typeparam name="TUserKey">The type of the user entity's key.</typeparam>
public interface IAuditedEntity<TUser, TUserKey>
    : IHasAuditTimes, ICreatedBy<TUser, TUserKey>, IModifiedBy<TUser, TUserKey>
    where TUser : class
{
}
