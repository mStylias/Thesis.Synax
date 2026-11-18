using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Publishes the service's shape: the CSDL <c>$metadata</c> document and the service document that
/// lists its entity sets. Both are what a generic OData consumer (Excel, Power BI, an OData client
/// library) reads before it queries anything.
/// </summary>
public static class ODataMetadataEndpoints
{
    /// <summary>
    /// Maps the OData service document at <paramref name="prefix"/> and the CSDL document at
    /// <c>{prefix}/$metadata</c>, describing every type declared with
    /// <see cref="SynaxODataOptions.Expose{T}"/>.
    /// </summary>
    /// <remarks>
    /// App-level and called once, like <c>MapSynaxEndpoints</c> — and therefore outside a Synax
    /// module, for the same reason. The prefix is the OData <em>service root</em>, which is why it is
    /// what the call takes rather than the <c>$metadata</c> path itself: the root is the thing every
    /// other URL in the protocol is relative to, including the <c>@odata.context</c> that
    /// <see cref="SynaxODataOptions.EnableAnnotations"/> emits. It is recorded on the options so
    /// those annotations can be rendered without an app repeating the path in
    /// <see cref="SynaxODataOptions.ServiceRoot"/> — which stays the lever for an absolute URL, as a
    /// reverse proxy needs.
    /// </remarks>
    /// <param name="app">The route builder to map onto.</param>
    /// <param name="prefix">The service root path. Defaults to <c>/odata</c>.</param>
    /// <exception cref="InvalidOperationException">No type has been exposed.</exception>
    public static IEndpointRouteBuilder MapSynaxODataMetadata(
        this IEndpointRouteBuilder app,
        string prefix = "/odata")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var settings = app.ServiceProvider.GetService<SynaxODataOptions>() ?? SynaxODataOptions.Default;
        var root = "/" + prefix.Trim('/');

        if (settings.Registry.IsEmpty)
            throw new InvalidOperationException(
                "MapSynaxODataMetadata() has nothing to describe: no type is exposed. Declare them " +
                "with SynaxODataOptions.Expose<T>(\"entitySetName\") inside AddSynaxOData, or drop " +
                "this call.");

        settings.MetadataPath = root;

        app.MapGet(root, (HttpContext context) => ServiceDocument(context, settings));
        app.MapGet($"{root}/$metadata", (HttpContext context) => Metadata(context, settings));

        return app;
    }

    /// <summary>
    /// The CSDL document, as XML by default and as JSON when the client asks for it — by
    /// <c>Accept: application/json</c> or the <c>$format=json</c> the OData protocol also allows here.
    /// Both are rendered once and cached, since the model cannot change after startup.
    /// </summary>
    private static Task Metadata(HttpContext context, SynaxODataOptions settings)
    {
        var json = WantsJson(context.Request);

        context.Response.ContentType = json ? "application/json; charset=utf-8" : "application/xml; charset=utf-8";

        var document = json ? settings.Registry.CsdlJson : settings.Registry.CsdlXml;

        return context.Response.Body.WriteAsync(document, context.RequestAborted).AsTask();
    }

    /// <summary>
    /// The service document: the entity sets a client may query, each named as it appears in
    /// <c>$metadata</c>. JSON only — the XML (AtomPub) form belongs to a protocol surface this
    /// package does not take on.
    /// </summary>
    private static Task ServiceDocument(HttpContext context, SynaxODataOptions settings)
    {
        var root = settings.ServiceRoot?.ToString().TrimEnd('/') ?? settings.MetadataPath ?? string.Empty;

        var document = new Dictionary<string, object?>
        {
            [ODataAnnotations.Context] = $"{root}/$metadata",
            ["value"] = settings.Registry.ExposedTypes
                .Select(exposed => new Dictionary<string, string>
                {
                    ["name"] = exposed.EntitySetName,
                    ["kind"] = "EntitySet",
                    ["url"] = exposed.EntitySetName
                })
                .ToList()
        };

        return context.Response.WriteAsJsonAsync(document, context.RequestAborted);
    }

    private static bool WantsJson(HttpRequest request)
    {
        if (request.Query.TryGetValue("$format", out var format) && format.Count > 0)
            return string.Equals(format[0], "json", StringComparison.OrdinalIgnoreCase);

        // Accept is a preference list; XML stays the default because that is what the CSDL consumers
        // in the wild (Excel, Power BI) ask for, and what "no opinion" should get.
        return request.Headers.Accept.Any(value =>
            value is not null && value.Contains("application/json", StringComparison.OrdinalIgnoreCase));
    }
}
