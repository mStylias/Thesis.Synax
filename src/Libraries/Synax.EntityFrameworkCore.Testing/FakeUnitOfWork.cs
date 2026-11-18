using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.Testing;

/// <summary>
/// An in-memory <see cref="IUnitOfWork{TContext}"/> for unit tests — no database, no EF provider,
/// no DI container. Seed data with <see cref="Seed{TEntity}"/>, hand it to the code under test, and
/// assert against <see cref="Entities{TEntity}"/> or the tracked transaction counters.
/// <code>
/// var uow = new FakeUnitOfWork&lt;BookingContext&gt;();
/// uow.Seed(new Booking { Id = 1, Name = "A" });
/// var handler = new GetBookingHandler(uow);
/// var result = await handler.HandleAsync(new GetBookingRequest(1), CancellationToken.None);
/// </code>
/// </summary>
public sealed class FakeUnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : ISynaxContext
{
    private readonly Dictionary<Type, IFakeStore> _stores = [];

    public FakeUnitOfWork(FakeUnitOfWorkOptions? options = null)
    {
        Options = options ?? new FakeUnitOfWorkOptions();
    }

    /// <summary>The options this fake was created with (owner/user accessors, clock, mapper, filters).</summary>
    public FakeUnitOfWorkOptions Options { get; }

    /// <summary>Transactions handed out by the Begin* methods, in order.</summary>
    public List<FakeDbContextTransaction> Transactions { get; } = [];

    /// <summary>Number of times a transaction commit was requested (via the unit of work or a transaction handle).</summary>
    public int TransactionCommits { get; private set; }

    /// <summary>Number of times a transaction rollback was requested (via the unit of work or a transaction handle).</summary>
    public int TransactionRollbacks { get; private set; }

    /// <summary>Seeds committed data for <typeparamref name="TEntity"/>, visible to queries immediately.</summary>
    public FakeUnitOfWork<TContext> Seed<TEntity>(params TEntity[] entities)
        where TEntity : class, IEntity
    {
        GetStore<TEntity>().Seed(entities);
        return this;
    }

    /// <summary>The committed entities of <typeparamref name="TEntity"/> — assert final state against this.</summary>
    public IReadOnlyList<TEntity> Entities<TEntity>()
        where TEntity : class, IEntity
        => GetStore<TEntity>().Committed;

    public IRepository<TEntity, TContext> GetRepository<TEntity>()
        where TEntity : class, IEntity
        => new FakeRepository<TEntity, TContext>(GetStore<TEntity>(), Options);

    public int SaveChanges()
        => _stores.Values.Sum(store => store.SaveChanges());

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SaveChanges());

    // --------- Transactions (no-ops, tracked for assertions) ---------- //

    public IDbContextTransaction BeginTransaction()
    {
        var transaction = new FakeDbContextTransaction(OnTransactionCommitted, OnTransactionRolledBack);
        Transactions.Add(transaction);
        return transaction;
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(BeginTransaction());

    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
        => Task.FromResult(BeginTransaction());

    public void CommitTransaction() => TransactionCommits++;

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        CommitTransaction();
        return Task.CompletedTask;
    }

    public void RollbackTransaction() => TransactionRollbacks++;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        RollbackTransaction();
        return Task.CompletedTask;
    }

    public IExecutionStrategy CreateExecutionStrategy() => new FakeExecutionStrategy();

    internal void OnTransactionCommitted() => TransactionCommits++;

    internal void OnTransactionRolledBack() => TransactionRollbacks++;

    private FakeStore<TEntity> GetStore<TEntity>()
        where TEntity : class
    {
        if (_stores.TryGetValue(typeof(TEntity), out var existing))
        {
            return (FakeStore<TEntity>)existing;
        }

        var store = new FakeStore<TEntity>(Options);
        _stores[typeof(TEntity)] = store;
        return store;
    }
}

/// <summary>
/// A no-op <see cref="IDbContextTransaction"/> that records commit/rollback calls for assertions.
/// </summary>
public sealed class FakeDbContextTransaction : IDbContextTransaction
{
    private readonly Action _onCommitted;
    private readonly Action _onRolledBack;

    internal FakeDbContextTransaction(Action onCommitted, Action onRolledBack)
    {
        _onCommitted = onCommitted;
        _onRolledBack = onRolledBack;
    }

    public Guid TransactionId { get; } = Guid.NewGuid();

    /// <summary>Whether <c>Commit</c> was called on this handle.</summary>
    public bool WasCommitted { get; private set; }

    /// <summary>Whether <c>Rollback</c> was called on this handle.</summary>
    public bool WasRolledBack { get; private set; }

    public bool SupportsSavepoints => true;

    public void Commit()
    {
        WasCommitted = true;
        _onCommitted();
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Commit();
        return Task.CompletedTask;
    }

    public void Rollback()
    {
        WasRolledBack = true;
        _onRolledBack();
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Rollback();
        return Task.CompletedTask;
    }

    public void CreateSavepoint(string name)
    {
    }

    public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RollbackToSavepoint(string name)
    {
    }

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void ReleaseSavepoint(string name)
    {
    }

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// A pass-through <see cref="IExecutionStrategy"/> that executes operations exactly once.
/// The <see cref="DbContext"/> argument passed to operations is <c>null</c> — fakes have no context;
/// operations that capture their dependencies (the normal pattern) are unaffected.
/// </summary>
public sealed class FakeExecutionStrategy : IExecutionStrategy
{
    public bool RetriesOnFailure => false;

    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
        => operation(null!, state);

    public Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default)
        => operation(null!, state, cancellationToken);
}
