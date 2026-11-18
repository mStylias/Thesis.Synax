using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.SoftDelete;
using Synax.EntityFrameworkCore.Auditing;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Patching;

namespace Synax.EntityFrameworkCore.Testing;

/// <summary>
/// An in-memory <see cref="IRepository{TEntity,TContext}"/> for unit tests — no database, no EF
/// provider. Queries run against seeded data through an async-capable LINQ-to-Objects provider that
/// understands EF-specific calls (<c>Include</c>, <c>IgnoreQueryFilters</c>, ...), and the library's
/// soft-delete / ownership semantics are emulated (query filters, delete conversion, owner stamping).
/// </summary>
public sealed class FakeRepository<TEntity, TContext> : IRepository<TEntity, TContext>
    where TEntity : class, IEntity
    where TContext : ISynaxContext
{
    private readonly FakeStore<TEntity> _store;
    private readonly FakeUnitOfWorkOptions _options;

    internal FakeRepository(FakeStore<TEntity> store, FakeUnitOfWorkOptions options)
    {
        _store = store;
        _options = options;
    }

    // --------- Query ---------- //

    public ISynaxQuery<TEntity> Query(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null,
        bool asNoTracking = true)
    {
        var root = CreateRoot();
        return new SynaxQuery<TEntity>(query is null ? root : query(root));
    }

    public ISynaxQuery<TDto> QueryDto<TDto>(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null,
        IReadOnlyDictionary<string, object?>? parameters = null)
        where TDto : class
    {
        var mapper = RequireMapper();
        var root = CreateRoot();
        var entityQueryable = query is null ? root : query(root);

        return new SynaxQuery<TDto>(mapper.ProjectTo<TDto>(entityQueryable, parameters));
    }

    public ISynaxQuery<TResult> QuerySelect<TResult>(
        Expression<Func<TEntity, TResult>> selector,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null)
    {
        var root = CreateRoot();
        var entityQueryable = query is null ? root : query(root);

        return new SynaxQuery<TResult>(entityQueryable.Select(selector));
    }

    public Task<List<TEntity>> GetListAsync(bool asNoTracking = true, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Snapshot(ignoreFilters: false).ToList());

    public Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        var entity = _store.FindByKey(id);

        if (entity is not null
            && _options.ApplyQueryFilters
            && !FakeQueryFilters.IsVisible(entity, _options))
        {
            entity = null;
        }

        return Task.FromResult(entity);
    }

    public Task<TEntity?> GetByIdAsync(object[] keyValues, CancellationToken cancellationToken = default)
    {
        if (keyValues.Length != 1)
        {
            throw new NotSupportedException(
                "FakeRepository supports single-value keys only. Configure a composite key comparison " +
                "via FakeUnitOfWorkOptions.UseKey<TEntity>(...) that projects to a comparable value.");
        }

        return GetByIdAsync(keyValues[0], cancellationToken);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Snapshot(ignoreFilters: false).Any());

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Snapshot(ignoreFilters: false).Count());

    // --------- Add ---------- //

    public Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        _store.StageAdd(entity);
        return Task.CompletedTask;
    }

    public async Task<TEntity> AddDtoAsync<TDto>(TDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var entity = RequireMapper().Map<TEntity>(dto);
        await AddAsync(entity, cancellationToken);
        return entity;
    }

    // --------- Update ---------- //

    public void Update(TEntity entity) => _store.StageUpdate(entity);

    public Task<int> ExecuteUpdateAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> updateQueryableFunc,
        Action<UpdateSettersBuilder<TEntity>> setPropertyCalls,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "ExecuteUpdateAsync cannot be emulated in-memory: EF's UpdateSettersBuilder does not expose " +
            "its collected setters. Cover ExecuteUpdateAsync-based code paths with integration tests, or " +
            "restructure the handler to use Update(...) for unit-testable flows.");
    }

    public Task<int> ExecutePatchAsync<TPatch>(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> updateQueryableFunc,
        TPatch patch,
        CancellationToken cancellationToken = default)
        where TPatch : class
    {
        ArgumentNullException.ThrowIfNull(patch);

        // Bulk operations bypass pending state and act on committed data immediately,
        // mirroring EF's ExecuteUpdate behavior. Query filters apply unless the func ignores them.
        var matched = updateQueryableFunc(CreateRoot()).ToList();

        var toApply = PatchMemberCache<TEntity, TPatch>.Members
            .Where(m => m.IsSet(patch))
            .ToList();

        if (toApply.Count == 0)
        {
            return Task.FromResult(matched.Count);
        }

        var utcNow = _options.TimeProvider.GetUtcNow().UtcDateTime;
        var userId = _options.CurrentUserId();

        foreach (var entity in matched)
        {
            foreach (var member in toApply)
            {
                member.ApplyToEntity(entity, patch);
            }

            // The real executor stamps modification audit members alongside the patch.
            AuditStamper.StampModification(entity, utcNow, userId);
        }

        return Task.FromResult(matched.Count);
    }

    // --------- Remove ---------- //

    public void Remove(TEntity entity) => _store.StageRemove(entity, hard: false);

    public Task<int> ExecuteDeleteAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> deleteFunc,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ExecuteDeleteCore(deleteFunc, hard: false));

    public void HardDelete(TEntity entity) => _store.StageRemove(entity, hard: true);

    public Task<int> ExecuteHardDeleteAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> deleteFunc,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ExecuteDeleteCore(deleteFunc, hard: true));

    public void HardDeleteRange(List<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            HardDelete(entity);
        }
    }

    // --------- Bulk ---------- //

    public Task BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var entityList = entities as IReadOnlyList<TEntity> ?? entities.ToList();

        if (entityList.Count > 0)
        {
            _store.InsertDirect(entityList);
        }

        return Task.CompletedTask;
    }

    public void UpdateRange(IEnumerable<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            Update(entity);
        }
    }

    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            Remove(entity);
        }
    }

    // --------- Internals ---------- //

    private IQueryable<TEntity> CreateRoot()
        => new FakeAsyncQueryProvider<TEntity>(ignoreFilters => _store.Snapshot(ignoreFilters)).CreateRoot();

    private int ExecuteDeleteCore(Func<IQueryable<TEntity>, IQueryable<TEntity>> deleteFunc, bool hard)
    {
        // Bulk operations bypass pending state and act on committed data immediately,
        // mirroring EF's ExecuteDelete behavior. Query filters apply unless the func ignores them.
        var matched = deleteFunc(CreateRoot()).ToList();

        foreach (var entity in matched)
        {
            if (!hard && entity is ISoftDelete)
            {
                FakeSoftDelete.Apply(entity, _options);
            }
            else
            {
                _store.Committed.Remove(entity);
            }
        }

        return matched.Count;
    }

    private IRepositoryMapper RequireMapper()
        => _options.Mapper
            ?? throw new InvalidOperationException(
                "This operation requires a mapper. Set FakeUnitOfWorkOptions.Mapper to an " +
                "IRepositoryMapper (e.g. wrap a MapperConfiguration's mapper with " +
                "SynaxMapperRepositoryMapper from Synax.EntityFrameworkCore.Mapper).");
}
