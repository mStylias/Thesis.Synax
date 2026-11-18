namespace Synax.EntityFrameworkCore.Abstractions.Core;

/// <summary>
/// One page of an offset-paginated query, with the metadata clients need to render pagers.
/// Produced by <see cref="ISynaxQuery{T}.ToPagedListAsync"/>.
/// </summary>
public sealed record PagedResult<T>
{
    /// <summary>The items on this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>The 1-based page number this result represents.</summary>
    public required int Page { get; init; }

    /// <summary>The requested page size (the last page may contain fewer items).</summary>
    public required int PageSize { get; init; }

    /// <summary>The total number of items across all pages.</summary>
    public required long TotalCount { get; init; }

    /// <summary>The total number of pages.</summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>Whether a page exists before this one.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>Whether a page exists after this one.</summary>
    public bool HasNextPage => Page < TotalPages;
}

/// <summary>
/// One page of a keyset-paginated (cursor-based) query, produced by
/// <see cref="ISynaxQuery{T}.ToKeysetPageAsync{TKey}"/>. Keyset pagination stays fast and stable on
/// large or frequently-changing data: no rows are skipped or duplicated when items are inserted or
/// deleted between page requests, and the database never scans past rows the way OFFSET does.
/// </summary>
public sealed record KeysetPage<T, TKey>
{
    /// <summary>The items on this page, ordered by the paging key.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>The requested page size (the last page may contain fewer items).</summary>
    public required int PageSize { get; init; }

    /// <summary>Whether more items exist after this page.</summary>
    public required bool HasMore { get; init; }

    /// <summary>
    /// The paging key of the last item on this page — pass it as <c>after</c> to fetch the next
    /// page. <c>default</c> when the page is empty; only meaningful for continuation when
    /// <see cref="HasMore"/> is <c>true</c>.
    /// </summary>
    public required TKey? NextCursor { get; init; }
}
