using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Synax.EntityFrameworkCore.Abstractions.Core;

/// <summary>
/// Generic repository for <typeparamref name="TEntity"/> within <typeparamref name="TContext"/>.
/// </summary>
public interface IRepository<TEntity, TContext> where TEntity : class, IEntity
    where TContext : ISynaxContext
{
    /// <summary>
    /// Builds a query over <typeparamref name="TEntity"/> and returns an <see cref="ISynaxQuery{T}"/>
    /// exposing all EF Core async materializers (e.g. <c>repo.Query(q => q.Where(...)).ToListAsync(ct)</c>).
    /// </summary>
    /// <param name="query">A function that applies filters, ordering, includes, etc. <c>null</c> queries the whole set.</param>
    /// <param name="asNoTracking">Whether to use no-tracking (default <c>true</c>).</param>
    public ISynaxQuery<TEntity> Query(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null,
        bool asNoTracking = true);

    /// <summary>
    /// Builds a query that projects <typeparamref name="TEntity"/> to <typeparamref name="TDto"/>
    /// using the configured mapper. Projections are never tracked.
    /// </summary>
    /// <param name="query">A function that applies filters, ordering, includes, etc. <c>null</c> queries the whole set.</param>
    /// <param name="parameters">
    /// The per-call values the mapping declares (Synax.Mapper's <c>MapParameters</c>). Required exactly when
    /// the mapping reads one; each reaches SQL as a parameter.
    /// </param>
    public ISynaxQuery<TDto> QueryDto<TDto>(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null,
        IReadOnlyDictionary<string, object?>? parameters = null)
        where TDto : class;

    /// <summary>
    /// Builds a query that selects specific properties from <typeparamref name="TEntity"/> using the
    /// provided <paramref name="selector"/> expression. No DTO class or mapper configuration is
    /// required — the result type is inferred from the selector. Projections are never tracked.
    /// </summary>
    /// <param name="selector">An expression that selects the desired properties (e.g. <c>e => new { e.Id, e.Name }</c>).</param>
    /// <param name="query">A function that applies filters, ordering, includes, etc. <c>null</c> queries the whole set.</param>
    public ISynaxQuery<TResult> QuerySelect<TResult>(
        Expression<Func<TEntity, TResult>> selector,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? query = null);

    /// <summary>
    /// Gets all records of the repository with no filters and adds them to a list
    /// </summary>
    /// <returns></returns>
    public Task<List<TEntity>> GetListAsync(bool asNoTracking = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an entity by its primary key, checking the change tracker before querying the database.
    /// </summary>
    public Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an entity by its (composite) primary key values, checking the change tracker before
    /// querying the database.
    /// </summary>
    public Task<TEntity?> GetByIdAsync(object[] keyValues, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the repository contains any entity (no filtering — use
    /// <see cref="Query"/> for that, e.g. <c>repo.Query(q => q.Where(...)).AnyAsync(ct)</c>).
    /// </summary>
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of entities in the repository (no filtering — use
    /// <see cref="Query"/> for that, e.g. <c>repo.Query(q => q.Where(...)).CountAsync(ct)</c>).
    /// </summary>
    public Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the entity to the context (call <c>SaveChangesAsync</c> separately).
    /// </summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps <typeparamref name="TDto"/> to <typeparamref name="TEntity"/> using the registered
    /// <see cref="IRepositoryMapper"/>, adds it, and returns the entity. Throws
    /// <see cref="ArgumentNullException"/> for a <c>null</c> DTO.
    /// </summary>
    Task<TEntity> AddDtoAsync<TDto>(TDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the entity as modified (call <c>SaveChangesAsync</c> separately).
    /// </summary>
    void Update(TEntity entity);

    /// <summary>
    /// Executes a bulk update using <c>ExecuteUpdateAsync</c>, bypassing the change tracker.
    /// </summary>
    Task<int> ExecuteUpdateAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> updateQueryableFunc,
        Action<UpdateSettersBuilder<TEntity>> setPropertyCalls,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a partial update (JSON merge-patch semantics) with a single set-based
    /// <c>ExecuteUpdateAsync</c>: every <c>PatchValue&lt;T&gt;</c> property of
    /// <typeparamref name="TPatch"/> that is <b>set</b> is written to the entity property of the
    /// same name; unset members leave their columns untouched. No mapper or profile
    /// configuration is involved — matching is by name and fails on first use with a
    /// descriptive error when a member has no compatible entity property.
    /// <para>
    /// Modification audit members (<c>UpdatedAt</c> / <c>ModifiedById</c>) are stamped when the
    /// entity implements the audit interfaces, since <c>ExecuteUpdate</c> bypasses the auditing
    /// interceptor. Acts immediately (no <c>SaveChangesAsync</c>), bypassing the change tracker.
    /// </para>
    /// Returns the number of rows the patch addressed — <c>0</c> means the query matched
    /// nothing (a patch with no set members performs no update and returns the matched count).
    /// </summary>
    Task<int> ExecutePatchAsync<TPatch>(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> updateQueryableFunc,
        TPatch patch,
        CancellationToken cancellationToken = default)
        where TPatch : class;

    /// <summary>
    /// Marks the entity for deletion (call <c>SaveChangesAsync</c> separately).
    /// If the entity implements soft delete, the interceptor converts this to a soft delete.
    /// </summary>
    void Remove(TEntity entity);

    /// <summary>
    /// Executes a bulk delete. For soft-delete entities, this is automatically converted to an update.
    /// </summary>
    Task<int> ExecuteDeleteAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> deleteFunc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes the entity, bypassing the soft delete interceptor.
    /// </summary>
    void HardDelete(TEntity entity);

    /// <summary>
    /// Permanently removes matching entities in bulk, bypassing soft delete conversion.
    /// </summary>
    Task<int> ExecuteHardDeleteAsync(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> deleteFunc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes the entities, bypassing the soft delete interceptor.
    /// </summary>
    void HardDeleteRange(List<TEntity> entities);

    /// <summary>
    /// Inserts entities in bulk using provider-optimized strategies (COPY for PostgreSQL, SqlBulkCopy for SQL Server).
    /// </summary>
    Task BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks multiple entities as modified.
    /// </summary>
    void UpdateRange(IEnumerable<TEntity> entities);

    /// <summary>
    /// Marks multiple entities for deletion.
    /// </summary>
    void RemoveRange(IEnumerable<TEntity> entities);
}
