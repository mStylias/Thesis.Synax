using Microsoft.EntityFrameworkCore.Storage;

namespace Synax.EntityFrameworkCore.Core;

/// <summary>
/// A non-owning handle over an already-active <see cref="IDbContextTransaction"/>, returned by
/// <see cref="UnitOfWork{TDbContext}"/> when a transaction is requested while one is already in progress.
/// <para>
/// <see cref="Commit"/> and <see cref="Dispose"/> (and their async counterparts) are no-ops so that a
/// nested <c>await using</c> scope cannot commit or roll back the outer transaction it does not own —
/// only the outermost owner completes the transaction. <see cref="Rollback"/> is deliberately
/// <b>not</b> a no-op: a rollback request from a nested scope must not be silently swallowed, so it
/// rolls back the underlying transaction.
/// </para>
/// </summary>
internal sealed class NestedDbContextTransaction : IDbContextTransaction
{
    private readonly IDbContextTransaction _inner;

    internal NestedDbContextTransaction(IDbContextTransaction inner)
    {
        _inner = inner;
    }

    public Guid TransactionId => _inner.TransactionId;

    public bool SupportsSavepoints => _inner.SupportsSavepoints;

    public void Commit()
    {
        // No-op: the outer owner commits.
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void Rollback()
    {
        _inner.Rollback();
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return _inner.RollbackAsync(cancellationToken);
    }

    public void CreateSavepoint(string name)
    {
        _inner.CreateSavepoint(name);
    }

    public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        return _inner.CreateSavepointAsync(name, cancellationToken);
    }

    public void RollbackToSavepoint(string name)
    {
        _inner.RollbackToSavepoint(name);
    }

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        return _inner.RollbackToSavepointAsync(name, cancellationToken);
    }

    public void ReleaseSavepoint(string name)
    {
        _inner.ReleaseSavepoint(name);
    }

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        return _inner.ReleaseSavepointAsync(name, cancellationToken);
    }

    public void Dispose()
    {
        // No-op: the outer owner disposes the underlying transaction.
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
