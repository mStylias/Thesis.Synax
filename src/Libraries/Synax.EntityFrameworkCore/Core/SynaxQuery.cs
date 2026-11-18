using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Synax.EntityFrameworkCore.Abstractions.Core;

namespace Synax.EntityFrameworkCore.Core;

/// <summary>
/// Creates canned <see cref="ISynaxQuery{T}"/> instances for tests and manual composition.
/// </summary>
public static class SynaxQuery
{
    /// <summary>
    /// Wraps an in-memory sequence as an <see cref="ISynaxQuery{T}"/>. All named materializers
    /// evaluate synchronously with LINQ to Objects — no EF provider involved — making this the
    /// one-liner for stubbing query results in unit tests.
    /// </summary>
    public static ISynaxQuery<T> From<T>(IEnumerable<T> source) => new SynaxQuery<T>(source.AsQueryable());

    /// <summary>Wraps an existing queryable as an <see cref="ISynaxQuery{T}"/>.</summary>
    public static ISynaxQuery<T> From<T>(IQueryable<T> source) => new SynaxQuery<T>(source);
}

/// <summary>
/// Default <see cref="ISynaxQuery{T}"/> over an <see cref="IQueryable{T}"/>. When the underlying
/// provider is an EF Core async provider, materializers delegate to EF's async operators; otherwise
/// (in-memory sources) they evaluate synchronously with LINQ to Objects, so canned instances built
/// via <see cref="SynaxQuery.From{T}(IEnumerable{T})"/> work without any EF infrastructure.
/// </summary>
public sealed class SynaxQuery<T> : ISynaxQuery<T>
{
    private readonly IQueryable<T> _queryable;

    public SynaxQuery(IQueryable<T> queryable)
    {
        _queryable = queryable;
    }

    private bool IsAsyncProvider => _queryable.Provider is IAsyncQueryProvider;

    // --------- Lists / sets / dictionaries ---------- //

    public Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.ToListAsync(cancellationToken)
            : Task.FromResult(_queryable.ToList());

    public Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.ToArrayAsync(cancellationToken)
            : Task.FromResult(_queryable.ToArray());

    public Task<HashSet<T>> ToHashSetAsync(CancellationToken cancellationToken = default)
        => ToHashSetAsync(comparer: null, cancellationToken);

    public async Task<HashSet<T>> ToHashSetAsync(IEqualityComparer<T>? comparer, CancellationToken cancellationToken = default)
        => new HashSet<T>(await ToListAsync(cancellationToken), comparer);

    public Task<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(
        Func<T, TKey> keySelector,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        => ToDictionaryAsync(keySelector, comparer: null, cancellationToken);

    public async Task<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(
        Func<T, TKey> keySelector,
        IEqualityComparer<TKey>? comparer,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        => (await ToListAsync(cancellationToken)).ToDictionary(keySelector, comparer);

    public Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(
        Func<T, TKey> keySelector,
        Func<T, TElement> elementSelector,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        => ToDictionaryAsync(keySelector, elementSelector, comparer: null, cancellationToken);

    public async Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(
        Func<T, TKey> keySelector,
        Func<T, TElement> elementSelector,
        IEqualityComparer<TKey>? comparer,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        => (await ToListAsync(cancellationToken)).ToDictionary(keySelector, elementSelector, comparer);

    // --------- Single elements ---------- //

    public Task<T> FirstAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.FirstAsync(cancellationToken)
            : Task.FromResult(_queryable.First());

    public Task<T> FirstAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.FirstAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.First(predicate));

    public Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.FirstOrDefaultAsync(cancellationToken)
            : Task.FromResult(_queryable.FirstOrDefault());

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.FirstOrDefaultAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.FirstOrDefault(predicate));

    public Task<T> SingleAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.SingleAsync(cancellationToken)
            : Task.FromResult(_queryable.Single());

    public Task<T> SingleAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.SingleAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.Single(predicate));

    public Task<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.SingleOrDefaultAsync(cancellationToken)
            : Task.FromResult(_queryable.SingleOrDefault());

    public Task<T?> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.SingleOrDefaultAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.SingleOrDefault(predicate));

    public Task<T> LastAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.LastAsync(cancellationToken)
            : Task.FromResult(_queryable.Last());

    public Task<T> LastAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.LastAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.Last(predicate));

    public Task<T?> LastOrDefaultAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.LastOrDefaultAsync(cancellationToken)
            : Task.FromResult(_queryable.LastOrDefault());

    public Task<T?> LastOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.LastOrDefaultAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.LastOrDefault(predicate));

    // --------- Aggregations ---------- //

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.CountAsync(cancellationToken)
            : Task.FromResult(_queryable.Count());

    public Task<int> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.CountAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.Count(predicate));

    public Task<long> LongCountAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.LongCountAsync(cancellationToken)
            : Task.FromResult(_queryable.LongCount());

    public Task<long> LongCountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.LongCountAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.LongCount(predicate));

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.AnyAsync(cancellationToken)
            : Task.FromResult(_queryable.Any());

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.AnyAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.Any(predicate));

    public Task<bool> AllAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.AllAsync(predicate, cancellationToken)
            : Task.FromResult(_queryable.All(predicate));

    public Task<bool> ContainsAsync(T item, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.ContainsAsync(item, cancellationToken)
            : Task.FromResult(_queryable.Contains(item));

    public Task<T> MinAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.MinAsync(cancellationToken)
            : Task.FromResult(_queryable.Min()!);

    public Task<TResult> MinAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.MinAsync(selector, cancellationToken)
            : Task.FromResult(_queryable.Min(selector)!);

    public Task<T> MaxAsync(CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.MaxAsync(cancellationToken)
            : Task.FromResult(_queryable.Max()!);

    public Task<TResult> MaxAsync<TResult>(Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider
            ? _queryable.MaxAsync(selector, cancellationToken)
            : Task.FromResult(_queryable.Max(selector)!);

    // --------- Sum ---------- //

    public Task<int> SumAsync(Expression<Func<T, int>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<int?> SumAsync(Expression<Func<T, int?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<long> SumAsync(Expression<Func<T, long>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<long?> SumAsync(Expression<Func<T, long?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<float> SumAsync(Expression<Func<T, float>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<float?> SumAsync(Expression<Func<T, float?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<double> SumAsync(Expression<Func<T, double>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<double?> SumAsync(Expression<Func<T, double?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<decimal> SumAsync(Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    public Task<decimal?> SumAsync(Expression<Func<T, decimal?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.SumAsync(selector, cancellationToken) : Task.FromResult(_queryable.Sum(selector));

    // --------- Average ---------- //

    public Task<double> AverageAsync(Expression<Func<T, int>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<double?> AverageAsync(Expression<Func<T, int?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<double> AverageAsync(Expression<Func<T, long>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<double?> AverageAsync(Expression<Func<T, long?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<float> AverageAsync(Expression<Func<T, float>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<float?> AverageAsync(Expression<Func<T, float?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<double> AverageAsync(Expression<Func<T, double>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<double?> AverageAsync(Expression<Func<T, double?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<decimal> AverageAsync(Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    public Task<decimal?> AverageAsync(Expression<Func<T, decimal?>> selector, CancellationToken cancellationToken = default)
        => IsAsyncProvider ? _queryable.AverageAsync(selector, cancellationToken) : Task.FromResult(_queryable.Average(selector));

    // --------- Pagination ---------- //

    public async Task<PagedResult<T>> ToPagedListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var totalCount = await LongCountAsync(cancellationToken);
        var items = await new SynaxQuery<T>(_queryable.Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<KeysetPage<T, TKey>> ToKeysetPageAsync<TKey>(
        Expression<Func<T, TKey>> keySelector,
        int pageSize,
        object? after = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : IComparable<TKey>
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var query = descending
            ? _queryable.OrderByDescending(keySelector)
            : _queryable.OrderBy(keySelector);

        IQueryable<T> window = query;

        if (after is not null)
        {
            var cursor = after is TKey typed
                ? typed
                : throw new ArgumentException(
                    $"The cursor must be a {typeof(TKey).Name} (the NextCursor of the previous page); got {after.GetType().Name}.",
                    nameof(after));

            window = window.Where(BuildAfterPredicate(keySelector, cursor, descending));
        }

        // Fetch one extra row to learn whether more pages exist without a second query.
        var items = await new SynaxQuery<T>(window.Take(pageSize + 1)).ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(pageSize);
        }

        return new KeysetPage<T, TKey>
        {
            Items = items,
            PageSize = pageSize,
            HasMore = hasMore,
            NextCursor = items.Count > 0 ? keySelector.Compile()(items[^1]) : default
        };
    }

    /// <summary>
    /// Builds <c>e => key(e) &gt; cursor</c> (or <c>&lt;</c> for descending). Types without
    /// comparison operators (string, Guid, ...) fall back to <c>key.CompareTo(cursor) &gt; 0</c>,
    /// which EF Core translates to the same SQL comparison.
    /// </summary>
    private static Expression<Func<T, bool>> BuildAfterPredicate<TKey>(
        Expression<Func<T, TKey>> keySelector,
        TKey cursor,
        bool descending)
        where TKey : IComparable<TKey>
    {
        var key = keySelector.Body;
        var cursorExpression = Expression.Constant(cursor, typeof(TKey));

        Expression comparison;
        try
        {
            comparison = descending
                ? Expression.LessThan(key, cursorExpression)
                : Expression.GreaterThan(key, cursorExpression);
        }
        catch (InvalidOperationException)
        {
            var compareTo = typeof(TKey).GetMethod(nameof(IComparable<TKey>.CompareTo), [typeof(TKey)])!;
            var compared = Expression.Call(key, compareTo, cursorExpression);
            var zero = Expression.Constant(0);
            comparison = descending
                ? Expression.LessThan(compared, zero)
                : Expression.GreaterThan(compared, zero);
        }

        return Expression.Lambda<Func<T, bool>>(comparison, keySelector.Parameters[0]);
    }

    // --------- Streaming ---------- //

    public IAsyncEnumerable<T> AsAsyncEnumerable()
        => _queryable is IAsyncEnumerable<T> asyncEnumerable
            ? asyncEnumerable
            : WrapAsAsyncEnumerable(_queryable);

    public async Task ForEachAsync(Action<T> action, CancellationToken cancellationToken = default)
    {
        if (IsAsyncProvider)
        {
            await _queryable.ForEachAsync(action, cancellationToken);
            return;
        }

        foreach (var item in _queryable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action(item);
        }
    }

    // --------- Escape hatch ---------- //

    public Task<TResult> ExecuteAsync<TResult>(Func<IQueryable<T>, Task<TResult>> executionFunc)
        => executionFunc(_queryable);

    private static async IAsyncEnumerable<T> WrapAsAsyncEnumerable(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
