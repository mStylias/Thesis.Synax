using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>One property of the page cursor: its EDM name and the CLR member holding the value.</summary>
internal sealed record SkipTokenKey(string EdmName, PropertyInfo Property);

/// <summary>
/// Cursor mechanics for <c>@odata.nextLink</c> / <c>$skiptoken</c>. The token is written in
/// <c>DefaultSkipTokenHandler</c>'s own wire format (<c>name-literal</c> pairs joined by commas, one
/// per clause of the stabilized <c>$orderby</c>), so the handler that ships with
/// <c>Microsoft.AspNetCore.OData</c> parses it back and generic OData clients round-trip it. The
/// package's own generator is internal and needs formatter machinery we have no part of, hence
/// writing the token here rather than calling into it.
/// </summary>
internal static class ServerDrivenPaging
{
    private const char PairSeparator = ',';
    private const char ValueSeparator = '-';

    /// <summary>
    /// The cursor properties for a request: the client's <c>$orderby</c> with the EDM key appended
    /// (which is what makes the order — and therefore the cursor — deterministic).
    /// </summary>
    /// <exception cref="SynaxODataQueryException">
    /// The ordering cannot be turned into a cursor: it sorts by a nested, computed or otherwise
    /// non-column expression, or by a property with no matching CLR member.
    /// </exception>
    public static IReadOnlyList<SkipTokenKey> ResolveKeys<T>(ODataQueryOptions<T> options)
    {
        OrderByClause? clause;
        try
        {
            // Same stabilization ApplyTo performs, so the cursor describes the order rows come back in.
            clause = options.GenerateStableOrder()?.OrderByClause;
        }
        catch (ODataException ex)
        {
            throw new SynaxODataQueryException($"The OData query is invalid: {ex.Message}", ex);
        }

        var keys = new List<SkipTokenKey>();
        for (; clause is not null; clause = clause.ThenBy)
            keys.Add(ResolveKey<T>(clause));

        if (keys.Count == 0)
            throw new SynaxODataQueryException(
                $"Server-driven paging needs a deterministic ordering for '{typeof(T).Name}', and none " +
                "could be derived. Add a $orderby, or declare a key via SynaxODataOptions.ConfigureModel.");

        return keys;
    }

    private static SkipTokenKey ResolveKey<T>(OrderByClause clause)
    {
        var expression = Unwrap(clause.Expression);

        if (expression is not SingleValuePropertyAccessNode
            {
                Source: ResourceRangeVariableReferenceNode
            } access)
            throw new SynaxODataQueryException(
                "Server-driven paging can only page over a $orderby of the queried type's own " +
                $"properties. '{Describe(expression)}' is a nested, computed or expression-valued sort " +
                "key, which cannot be encoded in a $skiptoken — order by a top-level property, or " +
                "switch this endpoint to client-driven $top/$skip paging.");

        var property = typeof(T).GetProperty(
            access.Property.Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
            throw new SynaxODataQueryException(
                $"Server-driven paging cannot read the sort key '{access.Property.Name}' off " +
                $"'{typeof(T).Name}': no matching public property exists.");

        return new SkipTokenKey(access.Property.Name, property);
    }

    /// <summary>
    /// Checks an incoming <c>$skiptoken</c> against the cursor properties the current ordering
    /// implies. Without this, a client typo reaches <c>DefaultSkipTokenHandler</c>, which reports it
    /// as an <see cref="InvalidOperationException"/> — a 500 for what is plainly a bad request.
    /// </summary>
    /// <exception cref="SynaxODataQueryException">The token does not match the ordering.</exception>
    public static void ValidateToken(string token, IReadOnlyList<SkipTokenKey> keys)
    {
        var expected = string.Join(", ", keys.Select(key => key.EdmName));
        var names = SplitPairNames(token);

        if (names is null || names.Count != keys.Count ||
            names.Where((name, index) => !string.Equals(name, keys[index].EdmName, StringComparison.OrdinalIgnoreCase)).Any())
            throw new SynaxODataQueryException(
                $"The $skiptoken '{token}' does not match the current ordering. A cursor is only valid " +
                $"for the $orderby it was issued for, which here needs the keys: {expected}. Follow the " +
                "@odata.nextLink from the previous page instead of composing a token by hand.");
    }

    /// <summary>
    /// The property names of a token's <c>name-literal</c> pairs, or <c>null</c> when the token is not
    /// in that shape. Commas inside a quoted literal do not separate pairs, and only the first
    /// <c>-</c> separates a name from its value (a Guid literal is full of the rest).
    /// </summary>
    private static List<string>? SplitPairNames(string token)
    {
        var names = new List<string>();
        var quoted = false;
        var start = 0;

        for (var index = 0; index <= token.Length; index++)
        {
            if (index < token.Length)
            {
                if (token[index] == '\'')
                    quoted = !quoted;

                if (quoted || token[index] != PairSeparator)
                    continue;
            }

            var pair = token.AsSpan(start, index - start);
            var separator = pair.IndexOf(ValueSeparator);
            if (separator <= 0)
                return null;

            names.Add(pair[..separator].ToString());
            start = index + 1;
        }

        return quoted ? null : names;
    }

    /// <summary>Writes the cursor for the last row of a page.</summary>
    public static string CreateToken(object row, IReadOnlyList<SkipTokenKey> keys, IEdmModel model)
        => string.Join(
            PairSeparator,
            keys.Select(key => key.EdmName + ValueSeparator + Literal(key.Property.GetValue(row), model)));

    private static string Literal(object? value, IEdmModel model)
    {
        // Edm has no DateTime primitive; the parser reads the value back as DateTimeOffset. The query
        // settings pin the time zone to UTC, so an unspecified kind is treated as UTC too.
        var converted = value switch
        {
            DateTime { Kind: DateTimeKind.Local } local => new DateTimeOffset(local),
            DateTime other => new DateTimeOffset(DateTime.SpecifyKind(other, DateTimeKind.Utc)),
            _ => value
        };

        try
        {
            return ODataUriUtils.ConvertToUriLiteral(converted, ODataVersion.V4, model);
        }
        catch (ODataException ex)
        {
            throw new SynaxODataQueryException(
                $"Server-driven paging cannot encode a sort key of type " +
                $"'{value?.GetType().Name ?? "null"}' into a $skiptoken. Order by a primitive property.",
                ex);
        }
    }

    /// <summary>The cursor for the next keyset page: the <c>$skiptoken</c> the last row encodes.</summary>
    public static KeyValuePair<string, string> KeysetCursor(
        object row,
        IReadOnlyList<SkipTokenKey> keys,
        IEdmModel model)
        => new("$skiptoken", CreateToken(row, keys, model));

    /// <summary>
    /// The cursor for the next page of rows a <c>$apply</c> reshaped: an offset, because an
    /// aggregation result carries neither the queried type's key nor a stable sort value to encode.
    /// Ordering is whatever the transformations imply (the grouping properties, unless the client
    /// ordered explicitly), which is deterministic for a given set of rows but — unlike a keyset
    /// cursor — shifts if the underlying rows change between pages.
    /// </summary>
    public static KeyValuePair<string, string> OffsetCursor(int? requestedSkip, int pageSize)
        => new("$skip", ((requestedSkip ?? 0) + pageSize).ToString());

    /// <summary>
    /// The next page's URL: the client's own query string with the paging options dropped
    /// (<c>$skip</c> was already applied to page one, and a stale cursor must not survive),
    /// <c>$top</c> reduced by the page just served (so following the links never yields more rows
    /// than the client asked for), and <paramref name="cursor"/> pointing at the next window.
    /// Relative to the host unless <see cref="SynaxODataOptions.ServiceRoot"/> makes it absolute.
    /// </summary>
    /// <returns>The link, or <c>null</c> when the client's <c>$top</c> is already exhausted.</returns>
    public static string? CreateNextLink(
        HttpRequest request,
        KeyValuePair<string, string> cursor,
        int pageSize,
        SynaxODataOptions settings,
        int? requestedTop)
    {
        var remainingTop = requestedTop - pageSize;
        if (remainingTop is <= 0)
            return null;

        var parameters = new List<KeyValuePair<string, string?>>();
        foreach (var (key, values) in request.Query)
        {
            if (Is(key, "$skip") || Is(key, "$skiptoken"))
                continue;

            if (remainingTop is { } top && Is(key, "$top"))
            {
                parameters.Add(new KeyValuePair<string, string?>(key, top.ToString()));
                continue;
            }

            parameters.AddRange(values.Select(value => new KeyValuePair<string, string?>(key, value)));
        }

        parameters.Add(new KeyValuePair<string, string?>(cursor.Key, cursor.Value));

        var relative = request.PathBase.Add(request.Path) + QueryString.Create(parameters);

        return settings.ServiceRoot is null
            ? relative
            : new Uri(settings.ServiceRoot, relative).ToString();
    }

    private static bool Is(string key, string option)
        => string.Equals(key, option, StringComparison.OrdinalIgnoreCase);

    private static SingleValueNode Unwrap(SingleValueNode node)
        => node is ConvertNode convert ? Unwrap(convert.Source) : node;

    private static string Describe(SingleValueNode node) => node switch
    {
        SingleValuePropertyAccessNode access => access.Property.Name,
        SingleValueOpenPropertyAccessNode open => open.Name,
        _ => node.Kind.ToString()
    };
}
