using System.Text.Json.Serialization;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// The response envelope for an OData-queried collection, following the OData JSON convention
/// (<c>value</c> array plus <c>@odata.count</c> when <c>$count=true</c>, and <c>@odata.context</c>
/// when annotations are on). Items are the query's
/// element type, except when <c>$select</c>/<c>$expand</c> reshape them — then each item is an
/// <c>IDictionary&lt;string, object?&gt;</c> holding only the selected/expanded members.
/// </summary>
public sealed record ODataPage
{
    /// <summary>
    /// The URL of the <c>$metadata</c> document and the fragment describing what this payload holds,
    /// present only when <see cref="SynaxODataOptions.EnableAnnotations"/> is on. First in the
    /// envelope, as the OData JSON format requires.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; init; }

    /// <summary>The items matching the query.</summary>
    [JsonPropertyName("value")]
    public required IReadOnlyList<object?> Value { get; init; }

    /// <summary>The total count across all pages; only set when the client sent <c>$count=true</c>.</summary>
    [JsonPropertyName("@odata.count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Count { get; init; }

    /// <summary>
    /// The URL of the next page, present only when server-driven paging is on
    /// (<see cref="SynaxODataOptions.PageSize"/>) and more rows follow. Clients page by following
    /// this link verbatim — it already carries the cursor and the client's own query options.
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; init; }
}
