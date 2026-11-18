using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.Auditing;

/// <summary>
/// A <see cref="SaveChangesInterceptor"/> that stamps audit properties: on insert,
/// <see cref="IHasCreationTime.CreatedAt"/> and <c>CreatedById</c> (unless set explicitly); on
/// update, <see cref="IHasModificationTime.UpdatedAt"/> and <c>ModifiedById</c> (always).
/// <para>
/// Soft deletes are not treated as modifications: entities whose <see cref="ISoftDelete.IsDeleted"/>
/// is <c>true</c> are skipped — the deletion time and user already record that operation.
/// </para>
/// <para>
/// The <see cref="IAuditUserProvider"/> and <see cref="TimeProvider"/> can either be passed to the
/// constructor or, when omitted, are resolved from the application's service provider (as
/// configured via <c>AddDbContext</c>) on each save — a DI registration alone is sufficient.
/// </para>
/// </summary>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly IAuditUserProvider? _userProvider;
    private readonly TimeProvider? _timeProvider;

    public AuditingInterceptor(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider;
    }

    public AuditingInterceptor(IAuditUserProvider userProvider, TimeProvider? timeProvider = null)
    {
        _userProvider = userProvider;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Resolve time/user only when the save actually contains auditable work.
        var resolved = false;
        var utcNow = default(DateTime);
        object? userId = null;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            // A soft delete surfaces as Modified with IsDeleted set; it is a delete, not an
            // update — DeletedAt/DeletedById record it, so modification stamps are skipped.
            if (entry.State == EntityState.Modified && entry.Entity is ISoftDelete { IsDeleted: true })
            {
                continue;
            }

            if (!resolved)
            {
                var timeProvider = _timeProvider
                    ?? ApplicationServices.GetService<TimeProvider>(context)
                    ?? TimeProvider.System;
                utcNow = timeProvider.GetUtcNow().UtcDateTime;
                userId = (_userProvider ?? ApplicationServices.GetService<IAuditUserProvider>(context))
                    ?.GetCurrentUserId();
                resolved = true;
            }

            if (entry.State == EntityState.Added)
            {
                AuditStamper.StampCreation(entry.Entity, utcNow, userId);
            }
            else
            {
                AuditStamper.StampModification(entry.Entity, utcNow, userId);
            }
        }
    }
}
