using Microsoft.EntityFrameworkCore;

namespace Synax.EntityFrameworkCore.Patching;

/// <summary>
/// Applies a <c>PatchValue&lt;T&gt;</c>-based patch DTO with a single set-based
/// <c>ExecuteUpdateAsync</c>: one <c>SetProperty</c> call per <b>set</b> member, matched to the
/// entity property of the same name — no mapper or profile configuration. Modification audit
/// members (<c>UpdatedAt</c> / <c>ModifiedById</c>) are stamped alongside, because
/// <c>ExecuteUpdate</c> bypasses the auditing interceptor (same reasoning as
/// <see cref="SoftDelete.SoftDeleteExecutor"/>).
/// </summary>
internal static class PatchExecutor
{
    internal static Task<int> ExecuteAsync<TEntity, TPatch>(
        IQueryable<TEntity> query,
        TPatch patch,
        object? userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
        where TEntity : class
        where TPatch : class
    {
        var toApply = PatchMemberCache<TEntity, TPatch>.Members
            .Where(m => m.IsSet(patch))
            .ToArray();

        if (toApply.Length == 0)
        {
            // Nothing was provided — touch no rows, but still report how many the patch
            // addressed so callers keep one "matched rows" semantic (0 ⇒ target absent).
            return query.CountAsync(cancellationToken);
        }

        return query.ExecuteUpdateAsync(s =>
        {
            foreach (var member in toApply)
            {
                member.ApplyToBuilder(s, patch);
            }

            if (PatchAuditSetters<TEntity>.UpdatedAtLambda is { } updatedAt)
            {
                s.SetProperty(updatedAt, utcNow);
            }

            if (userId is not null && PatchAuditSetters<TEntity>.SetModifiedById is { } setModifiedById)
            {
                setModifiedById(s, userId);
            }
        }, cancellationToken);
    }
}
