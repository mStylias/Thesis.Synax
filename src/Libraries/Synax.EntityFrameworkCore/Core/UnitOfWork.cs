using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.Core;

public class UnitOfWork<TContext, TDbContext> : IUnitOfWork<TContext>
    where TContext : ISynaxContext
    where TDbContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TDbContext _dbContext;
    private readonly DatabaseFacade _dbFacade;
    private IDbContextTransaction? CurrentTransaction => _dbFacade.CurrentTransaction;

    public UnitOfWork(IServiceProvider serviceProvider, TDbContext dbContext)
    {
        _serviceProvider = serviceProvider;
        _dbContext = dbContext;
        _dbFacade = dbContext.Database;
    }

    public IRepository<TEntity, TContext> GetRepository<TEntity>()
        where TEntity : class, IEntity
    {
        return _serviceProvider.GetRequiredService<IRepository<TEntity, TContext>>();
    }

    /// <inheritdoc />
    public int SaveChanges()
    {
        return _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public IDbContextTransaction BeginTransaction()
    {
        // When a transaction is already active, hand out a non-owning handle so a nested
        // scope disposing/committing it cannot end the outer transaction.
        return CurrentTransaction is not null
            ? new NestedDbContextTransaction(CurrentTransaction)
            : _dbFacade.BeginTransaction();
    }

    /// <inheritdoc />
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return CurrentTransaction is not null
            ? Task.FromResult<IDbContextTransaction>(new NestedDbContextTransaction(CurrentTransaction))
            : _dbFacade.BeginTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        return CurrentTransaction is not null
            ? Task.FromResult<IDbContextTransaction>(new NestedDbContextTransaction(CurrentTransaction))
            : _dbFacade.BeginTransactionAsync(isolationLevel, cancellationToken);
    }
    
    /// <inheritdoc />
    public void CommitTransaction()
    {
        _dbFacade.CommitTransaction();
    }

    /// <inheritdoc />
    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _dbFacade.CommitTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void RollbackTransaction()
    {
        _dbFacade.RollbackTransaction();
    }

    /// <inheritdoc />
    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _dbFacade.RollbackTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public IExecutionStrategy CreateExecutionStrategy()
    {
        return _dbFacade.CreateExecutionStrategy();
    }
}
