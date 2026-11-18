using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace Synax.EntityFrameworkCore.Abstractions.Core;

/// <summary>
/// Coordinates transactions and provides access to repositories for <typeparamref name="TContext"/>.
/// </summary>
public interface IUnitOfWork<TContext>
    where TContext : ISynaxContext
{
    /// <summary>
    /// Gets a repository for <typeparamref name="TEntity"/> from the DI container.
    /// </summary>
    IRepository<TEntity, TContext> GetRepository<TEntity>()
        where TEntity : class, IEntity;

    /// <summary>
    /// Persists all pending changes tracked by the underlying context, across all repositories.
    /// </summary>
    int SaveChanges();

    /// <summary>
    /// Asynchronously persists all pending changes tracked by the underlying context, across all repositories.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Starts a new transaction if one is not already active. Otherwise, it returns a non-owning
    ///     handle to the existing transaction whose <c>Commit</c> and <c>Dispose</c> are no-ops, so
    ///     only the outermost caller completes the transaction.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-transactions">Transactions in EF Core</see> for more information and examples.
    /// </remarks>
    /// <returns>
    ///     A <see cref="IDbContextTransaction" /> that represents the started transaction.
    /// </returns>
    IDbContextTransaction BeginTransaction();
    
    /// <summary>
    ///     Asynchronously starts a new transaction if one is not already active. Otherwise, it returns
    ///     a non-owning handle to the existing transaction whose <c>Commit</c> and <c>Dispose</c> are
    ///     no-ops, so only the outermost caller completes the transaction.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Entity Framework Core does not support multiple parallel operations being run on the same DbContext instance. This
    ///         includes both parallel execution of async queries and any explicit concurrent use from multiple threads.
    ///         Therefore, always await async calls immediately, or use separate DbContext instances for operations that execute
    ///         in parallel. See <see href="https://aka.ms/efcore-docs-threading">Avoiding DbContext threading issues</see>
    ///         for more information and examples.
    ///     </para>
    ///     <para>
    ///         See <see href="https://aka.ms/efcore-docs-transactions">Transactions in EF Core</see> for more information and examples.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    ///     A task that represents the asynchronous transaction initialization. The task result contains a <see cref="IDbContextTransaction" />
    ///     that represents the started transaction.
    /// </returns>
    /// <exception cref="OperationCanceledException">If the <see cref="CancellationToken" /> is canceled.</exception>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Asynchronously starts a new transaction with a given <see cref="IsolationLevel" /> if one is not already active.
    /// Otherwise, it returns a non-owning handle to the existing transaction whose <c>Commit</c> and
    /// <c>Dispose</c> are no-ops, so only the outermost caller completes the transaction.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-transactions">Transactions in EF Core</see> for more information and examples.
    /// </remarks>
    /// <param name="isolationLevel">The <see cref="IsolationLevel" /> to use.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    ///     A task that represents the asynchronous transaction initialization. The task result contains a <see cref="IDbContextTransaction" />
    ///     that represents the started transaction.
    /// </returns>
    /// <exception cref="OperationCanceledException">If the <see cref="CancellationToken" /> is canceled.</exception>
    Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Applies the outstanding operations in the current transaction to the database.
    /// </summary>
    void CommitTransaction();
    
    /// <summary>
    ///     Applies the outstanding operations in the current transaction to the database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Entity Framework Core does not support multiple parallel operations being run on the same DbContext instance. This
    ///         includes both parallel execution of async queries and any explicit concurrent use from multiple threads.
    ///         Therefore, always await async calls immediately, or use separate DbContext instances for operations that execute
    ///         in parallel. See <see href="https://aka.ms/efcore-docs-threading">Avoiding DbContext threading issues</see>
    ///         for more information and examples.
    ///     </para>
    ///     <para>
    ///         See <see href="https://aka.ms/efcore-docs-transactions">Transactions in EF Core</see> for more information and examples.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">If the <see cref="CancellationToken" /> is canceled.</exception>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Discards the outstanding operations in the current transaction.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-transactions">Transactions in EF Core</see> for more information and examples.
    /// </remarks>
    void RollbackTransaction();
    
    /// <summary>
    ///     Discards the outstanding operations in the current transaction.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         See <see href="https://aka.ms/efcore-docs-transactions">Transactions in EF Core</see> for more information and examples.
    ///     </para>
    ///     <para>
    ///         Entity Framework Core does not support multiple parallel operations being run on the same DbContext instance. This
    ///         includes both parallel execution of async queries and any explicit concurrent use from multiple threads.
    ///         Therefore, always await async calls immediately, or use separate DbContext instances for operations that execute
    ///         in parallel. See <see href="https://aka.ms/efcore-docs-threading">Avoiding DbContext threading issues</see>
    ///         for more information and examples.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">If the <see cref="CancellationToken" /> is canceled.</exception>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Creates an instance of the configured <see cref="IExecutionStrategy" />.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://aka.ms/efcore-docs-connection-resiliency">Connection resiliency and database retries</see>
    ///     for more information and examples.
    /// </remarks>
    /// <returns>An <see cref="IExecutionStrategy" /> instance.</returns>
    IExecutionStrategy CreateExecutionStrategy();
}
