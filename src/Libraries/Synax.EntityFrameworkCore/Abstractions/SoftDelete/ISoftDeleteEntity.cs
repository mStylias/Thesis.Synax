namespace Synax.EntityFrameworkCore.Abstractions.SoftDelete;

/// <summary>
/// Defines a contract for entities that support soft deletion, including tracking of deletion time and
/// the user responsible for the deletion.
/// </summary>
/// <typeparam name="TUser">The type of the user entity</typeparam>
/// <typeparam name="TUserKey">The type of the key of the user entity</typeparam>
public interface ISoftDeleteEntity<TUser, TUserKey> : ISoftDelete, IHasDeletionTime, IDeletedBy<TUser, TUserKey> 
    where TUser : class
{
    
}
