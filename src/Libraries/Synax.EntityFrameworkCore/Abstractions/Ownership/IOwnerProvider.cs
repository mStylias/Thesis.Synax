namespace Synax.EntityFrameworkCore.Abstractions.Ownership;

/// <summary>
/// Provides the identifier of the current owner (e.g. company, tenant) for ownership-scoped
/// persistence and querying.
/// </summary>
/// <typeparam name="TKey">
/// The type of the owner's identifier. The generic parameter is used for DI resolution only;
/// the return type is <c>object?</c> to support both value- and reference-type keys without
/// nullable-generic limitations.
/// </typeparam>
public interface IOwnerProvider<TKey>
{
    /// <summary>
    /// Returns the current owner's identifier, or <c>null</c> when no owner is ambient (for example,
    /// in background jobs or unauthenticated calls). Ownership-scoped queries will then return only
    /// rows that are shared (<see cref="IHasOptionalOwner{TKey}"/> with a <c>null</c> owner).
    /// <para>
    /// The returned value must be assignable to <typeparamref name="TKey"/>.
    /// </para>
    /// </summary>
    object? GetCurrentOwnerId();

    /// <summary>
    /// Returns <c>true</c> while owner scoping is deliberately lifted — cross-tenant reporting,
    /// admin tooling, maintenance sweeps — in which case ownership query filters admit every
    /// owner's rows and <see cref="GetCurrentOwnerId"/> is not consulted for reads.
    /// <para>
    /// Reads only: there is no ambient owner to stamp, so inserting an entity with a required
    /// owner while this is <c>true</c> still throws unless <c>OwnerId</c> was set explicitly.
    /// </para>
    /// <para>
    /// Defaults to <c>false</c>, so an existing provider needs no change. Honouring it also
    /// requires the cross-owner accessor to be passed to <c>ApplyOwnershipQueryFilters</c>.
    /// </para>
    /// </summary>
    bool IsCrossOwner() => false;
}
