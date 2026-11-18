using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using Microsoft.OData.ModelBuilder;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>One exposed type and the entity set name clients see it under.</summary>
internal sealed record ExposedType(Type ClrType, string EntitySetName);

/// <summary>
/// The types an app declared with <see cref="SynaxODataOptions.Expose{T}"/>, and the single composed
/// EDM model that describes them as one service.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ODataElementContextCache"/>, which builds a <em>one set per
/// element type</em> model for each queried type. That per-type model stays the one queries are
/// parsed and validated against, so exposing a type cannot change how any existing query behaves;
/// this composed model exists only to be published — <c>$metadata</c>, the service document, and the
/// <c>@odata.context</c> URLs that point into them.
/// </remarks>
internal sealed class ODataModelRegistry
{
    private readonly SynaxODataOptions _options;
    private readonly ConcurrentDictionary<Type, ExposedType> _byType = new();
    private readonly ConcurrentDictionary<string, Type> _bySetName = new(StringComparer.Ordinal);

    private readonly Lazy<IEdmModel> _model;
    private readonly Lazy<byte[]> _xml;
    private readonly Lazy<byte[]> _json;

    public ODataModelRegistry(SynaxODataOptions options)
    {
        _options = options;
        _model = new Lazy<IEdmModel>(BuildModel);
        _xml = new Lazy<byte[]>(BuildCsdlXml);
        _json = new Lazy<byte[]>(BuildCsdlJson);
    }

    public bool IsEmpty => _byType.IsEmpty;

    /// <summary>The exposed types, ordered by set name so the published document is stable.</summary>
    public IReadOnlyList<ExposedType> ExposedTypes
        => _byType.Values.OrderBy(exposed => exposed.EntitySetName, StringComparer.Ordinal).ToList();

    public IEdmModel Model => _model.Value;

    /// <summary>The CSDL XML document (<c>application/xml</c>), built once.</summary>
    public byte[] CsdlXml => _xml.Value;

    /// <summary>The CSDL JSON document (<c>application/json</c>), built once.</summary>
    public byte[] CsdlJson => _json.Value;

    public void Add(Type clrType, string entitySetName)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetName);

        if (_model.IsValueCreated)
            throw new InvalidOperationException(
                $"Cannot expose '{clrType.Name}' as '{entitySetName}': the composed EDM model has " +
                "already been published. Declare every exposed type in AddSynaxOData, before the " +
                "first request.");

        var existing = _byType.GetOrAdd(clrType, new ExposedType(clrType, entitySetName));

        // Idempotent for an identical declaration, so two modules may declare a shared type.
        if (existing.EntitySetName != entitySetName)
            throw new InvalidOperationException(
                $"'{clrType.Name}' is already exposed as '{existing.EntitySetName}', so it cannot " +
                $"also be '{entitySetName}'. The entity set name is part of the published contract: " +
                "one type, one name.");

        var owner = _bySetName.GetOrAdd(entitySetName, clrType);

        if (owner != clrType)
            throw new InvalidOperationException(
                $"The entity set name '{entitySetName}' is already taken by '{owner.Name}', so " +
                $"'{clrType.Name}' cannot use it too.");
    }

    /// <summary>
    /// The entity set an exposed type is published under, or <c>null</c> when nothing exposed it.
    /// </summary>
    public string? EntitySetNameFor(Type clrType)
        => _byType.TryGetValue(clrType, out var exposed) ? exposed.EntitySetName : null;

    /// <summary>
    /// One model holding every exposed set. Built with the same camel-casing and the app's own
    /// <see cref="SynaxODataOptions.ConfigureModel"/> as the per-type models, so a client reading
    /// <c>$metadata</c> sees the names it will get back in JSON.
    /// </summary>
    /// <remarks>
    /// Unlike the per-type models, this one is <em>not</em> marked filterable throughout: that flag
    /// exists to let a query's nested <c>$filter</c> past the validator, and no query is ever
    /// validated against this model. Leaving it off keeps the published CSDL free of capability
    /// annotations describing an implementation detail rather than the service.
    /// </remarks>
    private IEdmModel BuildModel()
    {
        if (IsEmpty)
            throw new InvalidOperationException(
                "No types are exposed, so there is no OData service to describe. Declare them with " +
                "SynaxODataOptions.Expose<T>(\"entitySetName\") inside AddSynaxOData.");

        var builder = new ODataConventionModelBuilder();
        builder.EnableLowerCamelCase();

        foreach (var exposed in ExposedTypes)
            builder.AddEntitySet(exposed.EntitySetName, builder.AddEntityType(exposed.ClrType));

        _options.ConfigureModel?.Invoke(builder);

        try
        {
            return builder.GetEdmModel();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to build the composed EDM model for the exposed types (" +
                string.Join(", ", ExposedTypes.Select(exposed => exposed.ClrType.Name)) +
                "). Every exposed type needs an entity key — a conventional 'Id'/'‹Type›Id' property, " +
                "or one declared via SynaxODataOptions.ConfigureModel.",
                ex);
        }
    }

    private byte[] BuildCsdlXml()
    {
        using var stream = new MemoryStream();

        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
        {
            if (!CsdlWriter.TryWriteCsdl(Model, writer, CsdlTarget.OData, out var errors))
                throw Failed("XML", errors);
        }

        return stream.ToArray();
    }

    private byte[] BuildCsdlJson()
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            if (!CsdlWriter.TryWriteCsdl(Model, writer, new CsdlJsonWriterSettings(), out var errors))
                throw Failed("JSON", errors);
        }

        return stream.ToArray();
    }

    private static InvalidOperationException Failed(string format, IEnumerable<EdmError> errors)
        => new($"Failed to write the CSDL {format} document for the exposed types: " +
               string.Join("; ", errors.Select(error => error.ToString())));
}
