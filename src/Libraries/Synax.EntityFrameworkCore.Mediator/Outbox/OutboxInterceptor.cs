using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Synax.EntityFrameworkCore.Abstractions.Core;

// ReSharper disable once CheckNamespace
namespace Synax.EntityFrameworkCore.Mediator;

/// <summary>
/// Signals the outbox channel <em>strictly after</em> outbox rows become durable:
/// immediately after a <c>SaveChanges</c> with no active transaction, or on transaction
/// commit otherwise (only the outermost commit fires the event, so nesting via
/// <c>TransactionBehavior</c> composes). Rollbacks discard the pending signals.
/// Services resolve from the application service provider per save, so the parameterless
/// registration is sufficient.
/// </summary>
public sealed class OutboxInterceptor<TContext, TDbContext> : SaveChangesInterceptor, IDbTransactionInterceptor
    where TContext : ISynaxContext
    where TDbContext : DbContext
{
    // ---------- SaveChanges: mark rows durable; flush when no transaction is active ----------

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        OnSavedChanges(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        OnSavedChanges(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private static void OnSavedChanges(DbContext? context)
    {
        if (context is not TDbContext) return;
        var buffer = GetApplicationService<OutboxPendingBuffer<TContext>>(context);
        if (buffer is null) return;

        buffer.MarkAllSaved();

        if (context.Database.CurrentTransaction is null)
            Flush(context, buffer);
    }

    // ---------- Transaction end: flush on commit, discard on rollback ----------

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => OnTransactionCommitted(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        OnTransactionCommitted(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => OnTransactionRolledBack(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        OnTransactionRolledBack(eventData.Context);
        return Task.CompletedTask;
    }

    private static void OnTransactionCommitted(DbContext? context)
    {
        if (context is not TDbContext) return;
        var buffer = GetApplicationService<OutboxPendingBuffer<TContext>>(context);
        if (buffer is not null) Flush(context, buffer);
    }

    private static void OnTransactionRolledBack(DbContext? context)
    {
        if (context is not TDbContext) return;
        GetApplicationService<OutboxPendingBuffer<TContext>>(context)?.DropSaved();
    }

    // ---------- Helpers ----------

    private static void Flush(DbContext context, OutboxPendingBuffer<TContext> buffer)
    {
        var channel = GetApplicationService<OutboxChannel<TContext>>(context);
        if (channel is null) return;

        foreach (var item in buffer.TakeSaved())
            channel.TryWrite(item); // drop-on-full: the sweep recovers dropped signals
    }

    private static TService? GetApplicationService<TService>(DbContext context) where TService : class
    {
        var applicationServices = context.GetService<IDbContextOptions>()
            .Extensions.OfType<CoreOptionsExtension>()
            .FirstOrDefault()
            ?.ApplicationServiceProvider;

        return applicationServices?.GetService(typeof(TService)) as TService;
    }
}
