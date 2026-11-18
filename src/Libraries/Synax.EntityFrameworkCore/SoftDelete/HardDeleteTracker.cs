using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Synax.EntityFrameworkCore.SoftDelete;

/// <summary>
/// Tracks entities that have been explicitly marked for hard (permanent) deletion,
/// so the <see cref="SoftDeleteInterceptor"/> knows to skip them.
/// <para>
/// Marks live in the context-scoped <see cref="HardDeleteRegistry"/> when the soft-delete feature
/// was registered via <c>AddSoftDeleteInterceptor</c>. The static weak table is only a fallback for
/// contexts configured without the options extension (e.g. a manually constructed interceptor).
/// </para>
/// </summary>
internal static class HardDeleteTracker
{
    private static readonly ConditionalWeakTable<DbContext, HashSet<object>> Fallback = new();

    internal static void MarkForHardDelete(DbContext context, object entity)
    {
        var set = GetRegistrySet(context)
            ?? Fallback.GetValue(context, static _ => new HashSet<object>(ReferenceEqualityComparer.Instance));

        set.Add(entity);
    }

    internal static bool IsMarkedForHardDelete(DbContext context, object entity)
    {
        var set = GetRegistrySet(context)
            ?? (Fallback.TryGetValue(context, out var fallbackSet) ? fallbackSet : null);

        return set?.Contains(entity) == true;
    }

    internal static void ClearMarks(DbContext context)
    {
        GetRegistrySet(context)?.Clear();

        if (Fallback.TryGetValue(context, out var fallbackSet))
        {
            fallbackSet.Clear();
        }
    }

    private static HashSet<object>? GetRegistrySet(DbContext context)
        => (context.GetInfrastructure().GetService(typeof(HardDeleteRegistry)) as HardDeleteRegistry)?.Entities;
}
