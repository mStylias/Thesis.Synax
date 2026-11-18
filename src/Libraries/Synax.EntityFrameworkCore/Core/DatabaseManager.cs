using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.Core;

public class DatabaseManager<TContext, TDbContext> : IDatabaseManager<TContext>
    where TContext : ISynaxContext
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    public DatabaseManager(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    /// <inheritdoc/>
    public string? ProviderName => _dbContext.Database.ProviderName;
    
    /// <inheritdoc/>
    public ChangeTracker ChangeTracker => _dbContext.ChangeTracker;
    
    /// <inheritdoc/>
    public IModel Model => _dbContext.Model;
    
    /// <inheritdoc/>
    public DbContextId ContextId => _dbContext.ContextId;
    
    /// <inheritdoc/>
    public bool EnsureCreated()
    {
        return _dbContext.Database.EnsureCreated();
    }

    /// <inheritdoc/>
    public Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public bool EnsureDeleted()
    {
        return _dbContext.Database.EnsureDeleted();
    }

    /// <inheritdoc/>
    public Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.EnsureDeletedAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public bool CanConnect()
    {
        return _dbContext.Database.CanConnect();
    }

    /// <inheritdoc/>
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.CanConnectAsync(cancellationToken);
    }
}