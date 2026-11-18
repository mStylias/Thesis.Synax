using System.Linq.Expressions;

namespace Synax.EntityFrameworkCore.Abstractions.Core;

/// <summary>
/// A composed query over <typeparamref name="T"/> exposing every EF Core async materialization
/// operator as a first-class interface member, so handlers can be unit tested with a fake
/// implementation — no database, no EF query provider required.
/// <para>
/// For operators not covered here (e.g. <c>GroupBy</c> with terminal aggregation, streaming into
/// custom shapes), <see cref="ExecuteAsync{TResult}"/> exposes the raw <see cref="IQueryable{T}"/>.
/// </para>
/// </summary>
public interface ISynaxQuery<T>
{
    // --------- Lists / sets / dictionaries ---------- //

    /// <summary>Materializes the query into a <see cref="List{T}"/>.</summary>
    Task<List<T>> ToListAsync(CancellationToken cancellationToken = default);

    /// <summary>Materializes the query into an array.</summary>
    Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default);

    /// <summary>Materializes the query into a <see cref="HashSet{T}"/>.</summary>
    Task<HashSet<T>> ToHashSetAsync(CancellationToken cancellationToken = default);

    /// <summary>Materializes the query into a <see cref="HashSet{T}"/> using the given comparer.</summary>
    Task<HashSet<T>> ToHashSetAsync(IEqualityComparer<T>? comparer, CancellationToken cancellationToken = default);

    /// <summary>Materializes the query into a dictionary keyed by <paramref name="keySelector"/>.</summary>
    Task<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(
        Func<T, TKey> keySelector,
        CancellationToken cancellationToken = default)
        where TKey : notnull;

    /// <summary>Materializes the query into a dictionary keyed by <paramref name="keySelector"/>.</summary>
    Task<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer,
        CancellationToken cancellationToken = default)
        where TKey : notnull;

    /// <summary>Materializes the query into a dictionary with projected elements.</summary>
    Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(
        Func<T, TKey> keySelector,
        Func<T, TElement> elementSelector,
        CancellationToken cancellationToken = default)
        where TKey : notnull;

    /// <summary>Materializes the query into a dictionary with projected elements.</summary>
    Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(
        Func<T, TKey> keySelector,
        Func<T, TElement> elementSelector,
        IEqualityComparer<TKey>? comparer,
        CancellationToken cancellationToken = default)
        where TKey : notnull;

    // --------- Single elements ---------- //

    /// <summary>Returns the first element; throws if the query is empty.</summary>
    Task<T> FirstAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the first element matching <paramref name="predicate"/>; throws if none match.</summary>
    Task<T> FirstAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns the first element, or <c>default</c> if the query is empty.</summary>
    Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the first element matching <paramref name="predicate"/>, or <c>default</c> if none match.</summary>
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns the only element; throws if the query is empty or has more than one element.</summary>
    Task<T> SingleAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the only element matching <paramref name="predicate"/>; throws if none or several match.</summary>
    Task<T> SingleAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns the only element, or <c>default</c> if empty; throws if more than one element.</summary>
    Task<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the only element matching <paramref name="predicate"/>, or <c>default</c>; throws if several match.</summary>
    Task<T?> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns the last element; requires an ordered query, throws if empty.</summary>
    Task<T> LastAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the last element matching <paramref name="predicate"/>; requires an ordered query.</summary>
    Task<T> LastAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns the last element or <c>default</c>; requires an ordered query.</summary>
    Task<T?> LastOrDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the last element matching <paramref name="predicate"/> or <c>default</c>; requires an ordered query.</summary>
    Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    // --------- Aggregations ---------- //

    /// <summary>Returns the number of elements.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the number of elements matching <paramref name="predicate"/>.</summary>
    Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns the number of elements as a 64-bit integer.</summary>
    Task<long> LongCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the number of elements matching <paramref name="predicate"/> as a 64-bit integer.</summary>
    Task<long> LongCountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns whether the query contains any elements.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns whether any element matches <paramref name="predicate"/>.</summary>
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns whether all elements match <paramref name="predicate"/>.</summary>
    Task<bool> AllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns whether the query contains <paramref name="item"/>.</summary>
    Task<bool> ContainsAsync(T item, CancellationToken cancellationToken = default);

    /// <summary>Returns the minimum element.</summary>
    Task<T> MinAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the minimum of <paramref name="selector"/> over the query.</summary>
    Task<TResult> MinAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the maximum element.</summary>
    Task<T> MaxAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the maximum of <paramref name="selector"/> over the query.</summary>
    Task<TResult> MaxAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default);

    // --------- Sum ---------- //

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<int> SumAsync(Expression<Func<T, int>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<int?> SumAsync(Expression<Func<T, int?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<long> SumAsync(Expression<Func<T, long>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<long?> SumAsync(Expression<Func<T, long?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<float> SumAsync(Expression<Func<T, float>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<float?> SumAsync(Expression<Func<T, float?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<double> SumAsync(Expression<Func<T, double>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<double?> SumAsync(Expression<Func<T, double?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<decimal> SumAsync(Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of <paramref name="selector"/> over the query.</summary>
    Task<decimal?> SumAsync(Expression<Func<T, decimal?>> selector, CancellationToken cancellationToken = default);

    // --------- Average ---------- //

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<double> AverageAsync(Expression<Func<T, int>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<double?> AverageAsync(Expression<Func<T, int?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<double> AverageAsync(Expression<Func<T, long>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<double?> AverageAsync(Expression<Func<T, long?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<float> AverageAsync(Expression<Func<T, float>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<float?> AverageAsync(Expression<Func<T, float?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<double> AverageAsync(Expression<Func<T, double>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<double?> AverageAsync(Expression<Func<T, double?>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<decimal> AverageAsync(Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default);

    /// <summary>Returns the average of <paramref name="selector"/> over the query.</summary>
    Task<decimal?> AverageAsync(Expression<Func<T, decimal?>> selector, CancellationToken cancellationToken = default);

    // --------- Pagination ---------- //

    /// <summary>
    /// Materializes one page using offset pagination: a total count plus <c>Skip/Take</c> in a
    /// single call. The query should be ordered (apply <c>OrderBy</c> in the query lambda) —
    /// unordered offset pagination returns rows in nondeterministic order.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The page size (at least 1).</param>
    Task<PagedResult<T>> ToPagedListAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes one page using keyset (cursor) pagination: orders by
    /// <paramref name="keySelector"/> and returns the items after the given cursor. Unlike offset
    /// pagination this is stable under concurrent inserts/deletes and stays fast on deep pages.
    /// The key should be unique (e.g. the primary key); with a non-unique key, rows sharing the
    /// boundary value can be skipped between pages.
    /// </summary>
    /// <param name="keySelector">Selects the paging key; also defines the ordering.</param>
    /// <param name="pageSize">The page size (at least 1).</param>
    /// <param name="after">
    /// The cursor: the <see cref="KeysetPage{T,TKey}.NextCursor"/> of the previous page, or
    /// <c>null</c> for the first page.
    /// </param>
    /// <param name="descending">Whether to page through the data in descending key order.</param>
    Task<KeysetPage<T, TKey>> ToKeysetPageAsync<TKey>(
        Expression<Func<T, TKey>> keySelector,
        int pageSize,
        object? after = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : IComparable<TKey>;

    // --------- Streaming ---------- //

    /// <summary>Streams the query results as an <see cref="IAsyncEnumerable{T}"/>.</summary>
    IAsyncEnumerable<T> AsAsyncEnumerable();

    /// <summary>Executes <paramref name="action"/> for each element as results stream in.</summary>
    Task ForEachAsync(Action<T> action, CancellationToken cancellationToken = default);

    // --------- Escape hatch ---------- //

    /// <summary>
    /// Materializes the query with any async LINQ operator not covered by the named members
    /// (e.g. <c>q => q.GroupBy(...).ToListAsync(ct)</c>). The full EF Core surface is available.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(Func<IQueryable<T>, Task<TResult>> executionFunc);
}
