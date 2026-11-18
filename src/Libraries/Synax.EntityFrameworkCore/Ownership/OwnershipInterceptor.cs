using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Synax.EntityFrameworkCore.Abstractions.Ownership;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.Ownership;

/// <summary>
/// A <see cref="SaveChangesInterceptor"/> that automatically assigns the current owner's identifier
/// to newly added entities implementing <see cref="IHasOwner{TKey}"/> or
/// <see cref="IHasOptionalOwner{TKey}"/> when their owner has not been set explicitly.
/// <para>
/// The <see cref="IOwnerProvider{TKey}"/> can either be passed to the constructor or, when omitted,
/// is resolved from the application's service provider (as configured via <c>AddDbContext</c>) on
/// each save — so the DI registration made by <c>WithOwnership</c> is sufficient, mirroring the
/// soft-delete interceptor's fallback.
/// </para>
/// </summary>
/// <typeparam name="TKey">The type of the owner's identifier (e.g. <c>int</c>, <c>Guid</c>).</typeparam>
public sealed class OwnershipInterceptor<TKey> : SaveChangesInterceptor
    where TKey : notnull
{
    private readonly IOwnerProvider<TKey>? _ownerProvider;

    // Cache per entity type: Required, Optional, or None.
    private static readonly ConcurrentDictionary<Type, OwnershipKind> TypeCache = new();

    public OwnershipInterceptor()
    {
    }

    public OwnershipInterceptor(IOwnerProvider<TKey> ownerProvider)
    {
        _ownerProvider = ownerProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AssignOwners(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AssignOwners(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AssignOwners(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Resolve the provider (and read the owner id) only when the save actually contains
        // ownership-scoped adds, so unrelated saves never touch DI or the provider.
        var ownerIdResolved = false;
        object? rawOwnerId = null;
        var crossOwner = false;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var kind = TypeCache.GetOrAdd(entry.Entity.GetType(), Classify);

            if (kind == OwnershipKind.None)
            {
                continue;
            }

            if (!ownerIdResolved)
            {
                var provider = _ownerProvider ?? ApplicationServices.GetService<IOwnerProvider<TKey>>(context);
                rawOwnerId = provider?.GetCurrentOwnerId();
                crossOwner = provider?.IsCrossOwner() ?? false;
                ownerIdResolved = true;
            }

            switch (kind)
            {
                case OwnershipKind.Required:
                    OwnershipAssigner.AssignRequired<TKey>(entry.Entity, rawOwnerId, crossOwner);
                    break;
                case OwnershipKind.Optional:
                    OwnershipAssigner.AssignOptional(entry.Entity, rawOwnerId);
                    break;
            }
        }
    }

    private static OwnershipKind Classify(Type entityType)
    {
        if (typeof(IHasOwner<TKey>).IsAssignableFrom(entityType))
        {
            return OwnershipKind.Required;
        }

        var optionalInterface = entityType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasOptionalOwner<>));

        if (optionalInterface is not null)
        {
            var keyArg = optionalInterface.GetGenericArguments()[0];
            var underlying = Nullable.GetUnderlyingType(keyArg) ?? keyArg;
            if (underlying == typeof(TKey))
            {
                return OwnershipKind.Optional;
            }
        }

        return OwnershipKind.None;
    }

    private enum OwnershipKind
    {
        None,
        Required,
        Optional
    }
}
