using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// The OData control information a response carries when
/// <see cref="SynaxODataOptions.EnableAnnotations"/> is on: <c>@odata.context</c> on the envelope,
/// and <c>@odata.id</c>/<c>@odata.type</c> on each row.
/// </summary>
/// <remarks>
/// Both need a service root and an entity set name, which is why annotations require the queried
/// type to be <see cref="SynaxODataOptions.Expose{T}"/>d: a context URL that points at a
/// <c>$metadata</c> nobody serves, or at a set nobody publishes, is worse than none at all, so a
/// missing declaration fails loudly rather than emitting a link that resolves to nothing.
/// </remarks>
internal static class ODataAnnotations
{
    public const string Context = "@odata.context";
    public const string Id = "@odata.id";
    public const string Type = "@odata.type";

    /// <summary>
    /// The <c>@odata.context</c> URL for one response: the service's <c>$metadata</c> document plus
    /// the fragment naming what the payload holds.
    /// </summary>
    /// <remarks>
    /// A <c>$select</c> narrows the fragment to the members the client asked for
    /// (<c>#restaurants(name,maxCapacity)</c>), per the OData JSON format. A <c>$expand</c> without a
    /// <c>$select</c> leaves the entity fragment as it is — the rows are still entities of the set,
    /// with more of them filled in. Rows a <c>$apply</c> reshaped are neither, so they get the bare
    /// set fragment and no row annotations at all.
    /// </remarks>
    public static string ContextUrl<T>(ODataQueryOptions<T> options, SynaxODataOptions settings)
    {
        var url = $"{MetadataUrl(settings)}#{EntitySetName(typeof(T), settings)}";

        if (options.Apply is not null)
            return url;

        return options.RawValues.Select is { Length: > 0 } select
            ? $"{url}({select})"
            : url;
    }

    /// <summary>
    /// The rows with <c>@odata.id</c> and <c>@odata.type</c> added. Adding members means the row has
    /// to be a dictionary, so a typed row is projected to the same camel-cased dictionary form
    /// <c>$select</c> already produces — the one visible cost of turning annotations on.
    /// </summary>
    public static IReadOnlyList<object?> Annotate<T>(
        IReadOnlyList<object?> rows,
        ODataQueryOptions<T> options,
        SynaxODataOptions settings)
    {
        // An aggregation row is not an entity: it has no key to build an id from and no type in the
        // model to name. It keeps the envelope's context and nothing else.
        if (options.Apply is not null)
            return rows;

        var model = settings.Registry.Model;
        var entitySet = EntitySetName(typeof(T), settings);
        var entityType = EntityType(model, typeof(T), settings);
        var typeName = $"#{entityType.FullTypeName()}";
        var keyName = entityType.Key().FirstOrDefault()?.Name;
        var serviceRoot = ServiceRootUrl(settings);

        return rows.Select(row => Annotate(row, serviceRoot, entitySet, typeName, keyName)).ToList();
    }

    private static object? Annotate(
        object? row,
        string serviceRoot,
        string entitySet,
        string typeName,
        string? keyName)
    {
        if (row is null)
            return null;

        // A $select/$expand row is already a dictionary; a typed row becomes one, keyed the way the
        // EDM names its members so both shapes annotate identically.
        var members = row is IDictionary<string, object?> dictionary
            ? new Dictionary<string, object?>(dictionary)
            : ToDictionary(row);

        // Read the key off the members rather than off the row, because a $select row is no longer
        // the queried type. A $select that dropped the key leaves the row with no id — the server
        // projected it away in SQL, and inventing one would be a guess.
        if (keyName is not null && members.TryGetValue(keyName, out var identity) && identity is not null)
            members[Id] = $"{serviceRoot}/{entitySet}({Literal(identity)})";

        members[Type] = typeName;

        return members;
    }

    /// <summary>
    /// A typed row as EDM-named members. Uses the same normalization the wrapper rows go through, so
    /// a nested object or collection lands in the payload the same way either route.
    /// </summary>
    private static Dictionary<string, object?> ToDictionary(object row)
    {
        var members = new Dictionary<string, object?>();

        foreach (var (name, read) in Members(row.GetType()))
            members[name] = SynaxQueryODataExtensions.NormalizeValue(read(row));

        return members;
    }

    /// <summary>
    /// The OData literal form of a key value, as it appears inside <c>set(key)</c>: strings are
    /// quoted, everything else is invariant-formatted (a <see cref="Guid"/> is bare, which is the
    /// OData v4 form).
    /// </summary>
    private static string Literal(object value) => value switch
    {
        string text => $"'{text.Replace("'", "''")}'",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string MetadataUrl(SynaxODataOptions settings) => $"{ServiceRootUrl(settings)}/$metadata";

    /// <summary>
    /// The service root the annotations are written against: the configured
    /// <see cref="SynaxODataOptions.ServiceRoot"/>, else the host-relative path
    /// <c>app.MapSynaxODataMetadata()</c> recorded. One of the two has to be there.
    /// </summary>
    private static string ServiceRootUrl(SynaxODataOptions settings)
    {
        if (settings.ServiceRoot is { } root)
            return root.ToString().TrimEnd('/');

        if (settings.MetadataPath is { Length: > 0 } path)
            return path.TrimEnd('/');

        throw new InvalidOperationException(
            "EnableAnnotations is on, but the service root is unknown, so @odata.context would point " +
            "nowhere. Map the metadata endpoints with app.MapSynaxODataMetadata(prefix), or set " +
            "SynaxODataOptions.ServiceRoot for an absolute URL (which is what a reverse proxy needs).");
    }

    private static string EntitySetName(Type elementType, SynaxODataOptions settings)
        => settings.Registry.EntitySetNameFor(elementType)
           ?? throw new InvalidOperationException(
               $"EnableAnnotations is on, but '{elementType.Name}' is not exposed, so there is no " +
               "entity set for @odata.context to name. Declare it with " +
               $"SynaxODataOptions.Expose<{elementType.Name}>(\"entitySetName\"), or turn annotations off.");

    private static IEdmEntityType EntityType(IEdmModel model, Type elementType, SynaxODataOptions settings)
        => model.EntityContainer?.FindEntitySet(EntitySetName(elementType, settings))?.EntityType
           ?? throw new InvalidOperationException(
               $"The composed EDM model has no entity set for '{elementType.Name}'. This is a Synax " +
               "bug: the type is registered but the model does not describe it.");

    /// <summary>
    /// A type's public readable properties, keyed by their EDM (camel-cased) name and turned into a
    /// delegate each. Cached per type — the annotation path runs per row, and reflecting on every
    /// one of them is the kind of cost that only shows up under load.
    /// </summary>
    private static Dictionary<string, Func<object, object?>> Members(Type type)
        => MemberReaders.GetOrAdd(type, static clrType => clrType
            .GetProperties()
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => CamelCase(property.Name),
                property => new Func<object, object?>(property.GetValue)));

    private static readonly ConcurrentDictionary<Type, Dictionary<string, Func<object, object?>>> MemberReaders = new();

    /// <summary>Matches <c>ODataConventionModelBuilder.EnableLowerCamelCase</c>'s naming.</summary>
    private static string CamelCase(string name)
        => name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
