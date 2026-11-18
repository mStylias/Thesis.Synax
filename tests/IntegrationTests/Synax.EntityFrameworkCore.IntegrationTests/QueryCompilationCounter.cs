using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Synax.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Counts EF Core query compilations, which happen only on a query-cache miss. Keyed by a marker the
/// test puts in its query as a literal, so tests running in parallel do not count each other.
/// </summary>
public sealed class QueryCompilationCounter : IQueryExpressionInterceptor
{
    public static readonly QueryCompilationCounter Instance = new();

    private readonly ConcurrentDictionary<string, int> _compilationsByMarker = new();

    public int GetCompilations(string marker) => _compilationsByMarker.GetValueOrDefault(marker);

    public void Track(string marker) => _compilationsByMarker.TryAdd(marker, 0);

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        var printed = queryExpression.ToString();
        foreach (var marker in _compilationsByMarker.Keys)
        {
            if (printed.Contains(marker, StringComparison.Ordinal))
                _compilationsByMarker.AddOrUpdate(marker, 1, (_, count) => count + 1);
        }

        return queryExpression;
    }
}
