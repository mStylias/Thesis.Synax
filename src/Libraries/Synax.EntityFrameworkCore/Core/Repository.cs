using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Synax.EntityFrameworkCore.Abstractions.Auditing;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Ownership;
using Synax.EntityFrameworkCore.Patching;
using Synax.EntityFrameworkCore.SoftDelete;

namespace Synax.EntityFrameworkCore.Core;

/// <summary>
/// The EF-backed <see cref="IRepository{TEntity,TContext}"/>. Public (like
/// <see cref="UnitOfWork{TContext,TDbContext}"/> and <see cref="DatabaseManager{TContext,TDbContext}"/>)
/// because deferred registrations close it reflectively — see
/// <c>AddSynaxRepository&lt;TEntity, TContext&gt;()</c>.
/// </summary>
public sealed class Repository<TEntity, TContext, TDbContext> : IRepository<TEntity, TContext>
    where TEntity : class, IEntity
    where TContext : ISynaxContext
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly DbSet<TEntity> _dbSet;
    private readonly ISoftDeleteUserProvider? _userProvider;
    private readonly IServiceProvider _serviceProvider;
    private IRepositoryMapper? _mapper;

    public Repository(
        TDbContext dbContext,
        IEnumerable<ISoftDeleteUserProvider> userProviders,
        IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<TEntity>();
        _userProvider = userProviders.FirstOrDefault();
        _serviceProvider = serviceProvider;
    }

    // Resolved lazily so repositories work without any mapper registration as long as
    // the DTO features are never used.
    private IRepositoryMapper Mapper
        => _mapper ??= _serviceProvider.GetService(typeof(IRepositoryMapper)) as IRepositoryMapper
            ?? throw new InvalidOperationException(
                "QueryDto/AddDtoAsync require an IRepositoryMapper registration. Reference " +
                "Synax.EntityFrameworkCore.Mapper and call services.AddSynaxRepositoryMapper() " +
                "(alongside AddSynaxMapper), or register your own IRepositoryMapper adapter.");
    
    // --------- Query ---------- //
    public ISynaxQuery<TEntity> Query(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null,
        bool asNoTracking = true)
    {
        var dbSet = asNoTracking ? _dbSet.AsNoTracking() : _dbSet.AsTracking();

        return new SynaxQuery<TEntity>(query is null ? dbSet : query(dbSet));
    }

    public ISynaxQuery<TDto> QueryDto<TDto>(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null,
        IReadOnlyDictionary<string, object?>? parameters = null)
        where TDto : class
    {
        // Projections are never tracked; always start from a no-tracking source.
        var source = _dbSet.AsNoTracking();
        var entityQueryable = query is null ? source : query(source);

        return new SynaxQuery<TDto>(Mapper.ProjectTo<TDto>(entityQueryable, parameters));
    }

    public ISynaxQuery<TResult> QuerySelect<TResult>(
        Expression<Func<TEntity, TResult>> selector,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null)
    {
        var source = _dbSet.AsNoTracking();
        var entityQueryable = query is null ? source : query(source);

        return new SynaxQuery<TResult>(entityQueryable.Select(selector));
    }

    public Task<List<TEntity>> GetListAsync(bool asNoTracking = true, CancellationToken cancellationToken = default)
    {
        var dbSet = asNoTracking ? _dbSet.AsNoTracking() : _dbSet.AsTracking();
        return dbSet.ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> GetByIdAsync(object[] keyValues, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync(keyValues, cancellationToken);
    }

    public async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync(new[] { id }, cancellationToken);
    }
    
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return _dbSet.AnyAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return _dbSet.CountAsync(cancellationToken);
    }

    // --------- Add ---------- //
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
    }

    public async Task<TEntity> AddDtoAsync<TDto>(TDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var entity = Mapper.Map<TEntity>(dto);
        await AddAsync(entity, cancellationToken);
        return entity;
    }

    // --------- Update ---------- //
    public void Update(TEntity entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> ExecuteUpdateAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> updateQueryableFunc,
        Action<UpdateSettersBuilder<TEntity>> setPropertyCalls,
        CancellationToken cancellationToken = default)
    {
        return updateQueryableFunc(_dbSet).ExecuteUpdateAsync(setPropertyCalls, cancellationToken);
    }

    public Task<int> ExecutePatchAsync<TPatch>(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> updateQueryableFunc,
        TPatch patch,
        CancellationToken cancellationToken = default)
        where TPatch : class
    {
        ArgumentNullException.ThrowIfNull(patch);

        // Providers resolve per call, mirroring the soft-delete execute path.
        var timeProvider = _serviceProvider.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var auditUserProvider = _serviceProvider.GetService(typeof(IAuditUserProvider)) as IAuditUserProvider;

        return PatchExecutor.ExecuteAsync(
            updateQueryableFunc(_dbSet),
            patch,
            auditUserProvider?.GetCurrentUserId(),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    // --------- Remove ---------- //
    public void Remove(TEntity entity)
    {
        _dbSet.Remove(entity);
    }

    // Built once per closed generic repository type; only touched for soft-delete entities.
    private static readonly Lazy<Func<IQueryable<TEntity>, object?, DateTime, CancellationToken, Task<int>>>
        SoftDeleteExecute = new(() => typeof(SoftDeleteExecutor)
            .GetMethod(nameof(SoftDeleteExecutor.ExecuteAsync), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(TEntity))
            .CreateDelegate<Func<IQueryable<TEntity>, object?, DateTime, CancellationToken, Task<int>>>());

    public Task<int> ExecuteDeleteAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> deleteFunc,
        CancellationToken cancellationToken = default)
    {
        if (SoftDeleteExecutor.IsSoftDelete<TEntity>())
        {
            var timeProvider = _serviceProvider.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

            return SoftDeleteExecute.Value(
                deleteFunc(_dbSet),
                _userProvider?.GetCurrentUserId(),
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
        }

        return deleteFunc(_dbSet).ExecuteDeleteAsync(cancellationToken);
    }

    public void HardDelete(TEntity entity)
    {
        HardDeleteTracker.MarkForHardDelete(_dbContext, entity);
        _dbSet.Remove(entity);
    }

    public Task<int> ExecuteHardDeleteAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> deleteFunc,
        CancellationToken cancellationToken = default)
    {
        return deleteFunc(_dbSet).ExecuteDeleteAsync(cancellationToken);
    }

    public void HardDeleteRange(List<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            HardDeleteTracker.MarkForHardDelete(_dbContext, entity);
        }

        _dbSet.RemoveRange(entities);
    }

    // --------- Bulk ---------- //
    public async Task BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var entityList = entities as IReadOnlyList<TEntity> ?? entities.ToList();

        if (entityList.Count == 0)
            return;

        // Bulk insert bypasses the change tracker, so the OwnershipInterceptor never runs;
        // apply the same owner assignment here before writing.
        OwnershipAssigner.AssignOwners(entityList, _serviceProvider);

        await BulkInsertHelper.BulkInsertAsync(_dbContext, entityList, cancellationToken);
    }

    public void UpdateRange(IEnumerable<TEntity> entities)
    {
        _dbSet.UpdateRange(entities);
    }

    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        _dbSet.RemoveRange(entities);
    }
 }
