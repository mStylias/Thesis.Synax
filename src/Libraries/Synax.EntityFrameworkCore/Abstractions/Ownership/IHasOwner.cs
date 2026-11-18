namespace Synax.EntityFrameworkCore.Abstractions.Ownership;

/// <summary>
/// Marks an entity as owned by some scoping target (commonly a company or tenant) identified by
/// <typeparamref name="TKey"/>. The <see cref="OwnerId"/> is required; rows are accessible only
/// to the matching owner.
/// </summary>
/// <typeparam name="TKey">The type of the owner's identifier.</typeparam>
public interface IHasOwner<TKey>
    where TKey : notnull
{
    /// <summary>
    /// The identifier of the owner this row belongs to.
    /// <para>
    /// <c>default(TKey)</c> (e.g. <c>0</c>, <c>Guid.Empty</c>) is reserved as the "not set" sentinel:
    /// the ownership interceptor assigns the ambient owner to entities whose <c>OwnerId</c> still has
    /// its default value. Owner identifiers must therefore be non-default values.
    /// </para>
    /// </summary>
    TKey OwnerId { get; set; }
}
