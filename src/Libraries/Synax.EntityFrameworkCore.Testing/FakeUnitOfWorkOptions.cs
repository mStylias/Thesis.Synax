using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.Testing;

/// <summary>
/// Configures the behavior of <see cref="FakeUnitOfWork{TContext}"/> and its repositories.
/// </summary>
public sealed class FakeUnitOfWorkOptions
{
    /// <summary>
    /// Whether queries emulate the library's global query filters (soft delete and ownership).
    /// Defaults to <c>true</c>; <c>IgnoreQueryFilters()</c> inside a query bypasses them, exactly
    /// like the real filters.
    /// </summary>
    public bool ApplyQueryFilters { get; set; } = true;

    /// <summary>
    /// The ambient owner id used for ownership stamping on add and for the ownership query filter.
    /// Mirrors <c>IOwnerProvider&lt;TKey&gt;.GetCurrentOwnerId()</c>. Defaults to no owner.
    /// </summary>
    public Func<object?> CurrentOwnerId { get; set; } = () => null;

    /// <summary>
    /// Whether owner scoping is lifted — every owner's rows are visible to queries, exactly as
    /// under <c>IUserContextScope.BecomeAllOwners</c> with the cross-owner query filter wired.
    /// Mirrors <c>IOwnerProvider&lt;TKey&gt;.IsCrossOwner()</c>. Defaults to <c>false</c>.
    /// <para>
    /// Reads only, like the real thing: adding an entity with a required owner and no explicit
    /// <c>OwnerId</c> still throws while this is on.
    /// </para>
    /// </summary>
    public Func<bool> CrossOwner { get; set; } = () => false;

    /// <summary>
    /// The ambient user id stamped into <c>DeletedById</c> on soft delete and into
    /// <c>CreatedById</c>/<c>ModifiedById</c> on audited adds/updates. Mirrors
    /// <c>ISoftDeleteUserProvider</c>/<c>IAuditUserProvider</c>. Defaults to no user.
    /// </summary>
    public Func<object?> CurrentUserId { get; set; } = () => null;

    /// <summary>
    /// The clock used for <c>DeletedAt</c> stamping. Defaults to <see cref="TimeProvider.System"/>;
    /// supply a fixed provider for deterministic timestamps.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// The mapper used by <c>QueryDto</c> / <c>AddDtoAsync</c>. Required only when those members are
    /// used. Any <see cref="IRepositoryMapper"/> works — e.g. wrap a Synax.Mapper configuration with
    /// <c>SynaxMapperRepositoryMapper</c> from <c>Synax.EntityFrameworkCore.Mapper</c>.
    /// </summary>
    public IRepositoryMapper? Mapper { get; set; }

    internal Dictionary<Type, Func<object, object?>> KeySelectors { get; } = [];

    /// <summary>
    /// Overrides primary-key resolution for <typeparamref name="TEntity"/> (used by
    /// <c>GetByIdAsync</c> and by update/remove matching of detached instances).
    /// By convention a property named <c>Id</c> is used when no selector is configured.
    /// </summary>
    public FakeUnitOfWorkOptions UseKey<TEntity>(Func<TEntity, object?> keySelector)
        where TEntity : class
    {
        KeySelectors[typeof(TEntity)] = entity => keySelector((TEntity)entity);
        return this;
    }

    internal Func<object, object?>? GetKeySelector(Type entityType)
    {
        if (KeySelectors.TryGetValue(entityType, out var configured))
        {
            return configured;
        }

        var idProperty = entityType.GetProperty("Id");
        return idProperty is null ? null : entity => idProperty.GetValue(entity);
    }
}
