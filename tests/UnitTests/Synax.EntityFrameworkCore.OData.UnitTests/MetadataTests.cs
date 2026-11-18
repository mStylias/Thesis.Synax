using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;

namespace Synax.EntityFrameworkCore.OData.UnitTests;

/// <summary>
/// Phase 6 — what the service publishes about itself: the types an app declares with
/// <see cref="SynaxODataOptions.Expose{T}"/>, the CSDL <c>$metadata</c> document and service
/// document served from them, the <c>@odata.*</c> annotations that point back at those documents,
/// and <c>$format</c>.
/// </summary>
/// <remarks>
/// The endpoint tests run against a real Kestrel server on a loopback port rather than a test host:
/// routing, content negotiation and the recorded service-root prefix are the parts worth proving,
/// and none of them is exercised by calling the handlers directly.
/// </remarks>
public class MetadataTests
{
    private static SynaxODataOptions Exposed(Action<SynaxODataOptions>? configure = null)
    {
        var settings = new SynaxODataOptions();
        settings.Expose<ProductDto>("products");
        settings.Expose<CategoryDto>("categories");
        configure?.Invoke(settings);

        return settings;
    }

    // ---------- Expose ----------

    [Test]
    public async Task Expose_DeclaringTheSameTypeAndNameTwice_IsANoOp()
    {
        var settings = new SynaxODataOptions();
        settings.Expose<ProductDto>("products");
        settings.Expose<ProductDto>("products");

        await Assert.That(settings.Registry.ExposedTypes).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Expose_TheSameTypeUnderTwoNames_IsAnError()
    {
        var settings = new SynaxODataOptions();
        settings.Expose<ProductDto>("products");

        Func<Task> act = () => Task.FromResult(settings.Expose<ProductDto>("items"));

        var exception = await Assert.That(act).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("products");
    }

    [Test]
    public async Task Expose_TwoTypesUnderOneName_IsAnError()
    {
        var settings = new SynaxODataOptions();
        settings.Expose<ProductDto>("things");

        Func<Task> act = () => Task.FromResult(settings.Expose<CategoryDto>("things"));

        var exception = await Assert.That(act).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains(nameof(ProductDto));
    }

    [Test]
    public async Task Expose_DoesNotChangeHowAQueryBehaves()
    {
        // The composed model is published, never queried: the same request against exposed and
        // unexposed settings has to come back identical.
        const string query = "?$filter=stock gt 5&$orderby=name&$select=name,stock&$count=true";

        var plain = await TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(query));
        var exposed = await TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(query), Exposed());

        await Assert.That(Serialize(exposed)).IsEqualTo(Serialize(plain));
    }

    // ---------- The composed model ----------

    [Test]
    public async Task Metadata_DescribesEveryExposedSet()
    {
        var model = ParseXml(Exposed().Registry.CsdlXml);

        var sets = model.EntityContainer.EntitySets().Select(set => set.Name).ToList();
        await Assert.That(sets).IsEquivalentTo(["categories", "products"]);
    }

    [Test]
    public async Task Metadata_CarriesTheKeysAndNavigationsClientsNeed()
    {
        var model = ParseXml(Exposed().Registry.CsdlXml);
        var products = model.EntityContainer.FindEntitySet("products").EntityType;

        // Camel-cased, because that is the JSON a client will actually receive.
        await Assert.That(products.Key().Select(key => key.Name).ToList()).IsEquivalentTo(["id"]);
        await Assert.That(products.DeclaredProperties.Select(property => property.Name).ToList())
            .Contains("stock");

        var navigations = products.NavigationProperties().Select(navigation => navigation.Name).ToList();
        await Assert.That(navigations).Contains("category");
        await Assert.That(navigations).Contains("tags");
    }

    [Test]
    public async Task Metadata_JsonAndXmlDescribeTheSameModel()
    {
        var settings = Exposed();

        var fromXml = ParseXml(settings.Registry.CsdlXml);
        var fromJson = ParseJson(settings.Registry.CsdlJson);

        await Assert.That(fromJson.EntityContainer.EntitySets().Select(set => set.Name).ToList())
            .IsEquivalentTo(fromXml.EntityContainer.EntitySets().Select(set => set.Name).ToList());
    }

    [Test]
    public async Task Metadata_WithNothingExposed_IsARegistrationError()
    {
        Func<Task> act = () => Task.FromResult(new SynaxODataOptions().Registry.Model);

        var exception = await Assert.That(act).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains(nameof(SynaxODataOptions.Expose));
    }

    // ---------- The endpoints ----------

    [Test]
    public async Task MetadataEndpoint_ServesCsdlXmlByDefault()
    {
        await using var service = await ODataService.StartAsync(Exposed());

        var response = await service.Client.GetAsync("/odata/$metadata");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/xml");
        await Assert.That(body).Contains("<EntitySet Name=\"products\"");
    }

    [Test]
    [Arguments("/odata/$metadata?$format=json")]
    [Arguments("/odata/$metadata")]
    public async Task MetadataEndpoint_ServesCsdlJsonWhenTheClientAsks(string url)
    {
        await using var service = await ODataService.StartAsync(Exposed());

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!url.Contains("$format"))
            request.Headers.Add("Accept", "application/json");

        var response = await service.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");

        using var document = JsonDocument.Parse(body);
        await Assert.That(document.RootElement.TryGetProperty("$Version", out _)).IsTrue();
    }

    [Test]
    public async Task ServiceDocument_ListsTheEntitySets()
    {
        await using var service = await ODataService.StartAsync(Exposed());

        using var document = JsonDocument.Parse(await service.Client.GetStringAsync("/odata"));

        await Assert.That(document.RootElement.GetProperty("@odata.context").GetString())
            .IsEqualTo("/odata/$metadata");

        var sets = document.RootElement.GetProperty("value").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString())
            .ToList();
        await Assert.That(sets).IsEquivalentTo(["categories", "products"]);

        var kinds = document.RootElement.GetProperty("value").EnumerateArray()
            .Select(entry => entry.GetProperty("kind").GetString())
            .Distinct()
            .ToList();
        await Assert.That(kinds).IsEquivalentTo(["EntitySet"]);
    }

    [Test]
    public async Task MetadataEndpoint_AtACustomPrefix_MovesBothDocuments()
    {
        await using var service = await ODataService.StartAsync(Exposed(), prefix: "/api/data");

        var metadata = await service.Client.GetAsync("/api/data/$metadata");
        var serviceDocument = await service.Client.GetAsync("/api/data");
        var oldRoot = await service.Client.GetAsync("/odata");

        await Assert.That(metadata.IsSuccessStatusCode).IsTrue();
        await Assert.That(serviceDocument.IsSuccessStatusCode).IsTrue();
        await Assert.That((int)oldRoot.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task MapMetadata_WithNothingExposed_FailsAtStartup()
    {
        Func<Task> act = () => ODataService.StartAsync(new SynaxODataOptions());

        var exception = await Assert.That(act).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains(nameof(SynaxODataOptions.Expose));
    }

    // ---------- Annotations ----------

    private static Task<ODataPage> PageAsync(string queryString, SynaxODataOptions settings)
        => TestData.ProductQuery().ToODataPageAsync(TestData.CreateRequest(queryString), settings);

    private static SynaxODataOptions Annotated(Action<SynaxODataOptions>? configure = null)
        => Exposed(settings =>
        {
            settings.EnableAnnotations = true;
            settings.MetadataPath = "/odata";
            configure?.Invoke(settings);
        });

    [Test]
    public async Task Annotations_AreAbsentUntilSwitchedOn()
    {
        var page = await PageAsync("?$top=1&$orderby=name", Exposed());

        await Assert.That(page.Context).IsNull();
        await Assert.That(page.Value[0]).IsTypeOf<ProductDto>();
    }

    [Test]
    public async Task Context_NamesTheEntitySetTheRowsCameFrom()
    {
        var page = await PageAsync("?$top=1&$orderby=name", Annotated());

        await Assert.That(page.Context).IsEqualTo("/odata/$metadata#products");
    }

    [Test]
    public async Task Context_WithSelect_NarrowsTheFragmentToTheSelectedMembers()
    {
        var page = await PageAsync("?$select=name,stock&$top=1&$orderby=name", Annotated());

        await Assert.That(page.Context).IsEqualTo("/odata/$metadata#products(name,stock)");
    }

    [Test]
    public async Task ServiceRoot_MakesTheAnnotationsAbsolute()
    {
        var settings = Annotated(options => options.ServiceRoot = new Uri("https://api.example.com/odata"));

        var page = await PageAsync("?$filter=name eq 'Laptop'", settings);

        await Assert.That(page.Context).IsEqualTo("https://api.example.com/odata/$metadata#products");

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row[ODataAnnotations.Id])
            .IsEqualTo($"https://api.example.com/odata/products({TestData.Id(1)})");
    }

    [Test]
    public async Task Rows_CarryTheirIdAndType()
    {
        var page = await PageAsync("?$filter=name eq 'Laptop'", Annotated());

        var row = (IDictionary<string, object?>)page.Value[0]!;

        await Assert.That(row[ODataAnnotations.Id]).IsEqualTo($"/odata/products({TestData.Id(1)})");
        await Assert.That(row[ODataAnnotations.Type]).IsEqualTo($"#{typeof(ProductDto).FullName}");

        // The row is the whole entity, camel-cased the way the EDM names it.
        await Assert.That(row["name"]).IsEqualTo("Laptop");
        await Assert.That(row["stock"]).IsEqualTo(3);
    }

    [Test]
    public async Task Rows_ReshapedBySelect_KeepTheirMembersAndGainTheAnnotations()
    {
        var withKey = await PageAsync("?$select=id,name&$filter=name eq 'Laptop'", Annotated());
        var withKeyRow = (IDictionary<string, object?>)withKey.Value[0]!;

        await Assert.That(withKeyRow.Keys)
            .IsEquivalentTo(["id", "name", ODataAnnotations.Id, ODataAnnotations.Type]);
        await Assert.That(withKeyRow[ODataAnnotations.Id]).IsEqualTo($"/odata/products({TestData.Id(1)})");

        // A $select that drops the key leaves the row with no @odata.id: the server projected the
        // key away in SQL, and an id it did not read would be a guess.
        var withoutKey = await PageAsync("?$select=name&$filter=name eq 'Laptop'", Annotated());
        var withoutKeyRow = (IDictionary<string, object?>)withoutKey.Value[0]!;

        await Assert.That(withoutKeyRow.Keys).IsEquivalentTo(["name", ODataAnnotations.Type]);
    }

    [Test]
    public async Task ApplyRows_KeepTheSetFragmentAndCarryNoEntityAnnotations()
    {
        var settings = Annotated(options => options.EnableApply = true);

        var page = await PageAsync("?$apply=aggregate(stock with sum as total)", settings);

        // An aggregation result is not an entity, so it has neither an id nor a type to name.
        await Assert.That(page.Context).IsEqualTo("/odata/$metadata#products");

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row.Keys).IsEquivalentTo(["total"]);
    }

    [Test]
    public async Task Annotations_ForATypeNobodyExposed_FailDescriptively()
    {
        var settings = new SynaxODataOptions { EnableAnnotations = true, MetadataPath = "/odata" };
        settings.Expose<CategoryDto>("categories");

        Func<Task> act = () => PageAsync("?$top=1", settings);

        var exception = await Assert.That(act).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains(nameof(ProductDto));
        await Assert.That(exception.Message).Contains(nameof(SynaxODataOptions.Expose));
    }

    [Test]
    public async Task Annotations_WithNoServiceRootAnywhere_FailDescriptively()
    {
        // Nothing mapped the metadata endpoints and no ServiceRoot was set, so a context URL would
        // point at a $metadata that is not served.
        var settings = Exposed(options => options.EnableAnnotations = true);

        Func<Task> act = () => PageAsync("?$top=1", settings);

        var exception = await Assert.That(act).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains(nameof(SynaxODataOptions.ServiceRoot));
        await Assert.That(exception.Message).Contains("MapSynaxODataMetadata");
    }

    [Test]
    public async Task MapMetadata_RecordsTheServiceRootTheAnnotationsUse()
    {
        var settings = Exposed(options => options.EnableAnnotations = true);

        await using var service = await ODataService.StartAsync(settings, prefix: "/api/data");

        var page = await PageAsync("?$top=1&$orderby=name", settings);
        await Assert.That(page.Context).IsEqualTo("/api/data/$metadata#products");
    }

    // ---------- $format ----------

    [Test]
    [Arguments("json")]
    [Arguments("JSON")]
    [Arguments("application/json")]
    public async Task Format_AskingForJson_IsAccepted(string format)
    {
        var page = await PageAsync($"?$format={Uri.EscapeDataString(format)}&$top=1&$orderby=name", Exposed());

        await Assert.That(page.Value).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("xml")]
    [Arguments("atom")]
    public async Task Format_AskingForAnythingElse_IsAClientError(string format)
    {
        Func<Task> act = () => PageAsync($"?$format={format}", Exposed());

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();

        await Assert.That(exception!.Message).Contains(format);
    }

    // ---------- Helpers ----------

    private static IEdmModel ParseXml(byte[] csdl)
    {
        using var reader = XmlReader.Create(new MemoryStream(csdl));

        return CsdlReader.TryParse(reader, out var model, out var errors)
            ? model
            : throw new InvalidOperationException(string.Join("; ", errors));
    }

    private static IEdmModel ParseJson(byte[] csdl)
    {
        var reader = new Utf8JsonReader(csdl);

        return CsdlReader.TryParse(ref reader, out var model, out var errors)
            ? model
            : throw new InvalidOperationException(string.Join("; ", errors));
    }

    private static string Serialize(ODataPage page) => JsonSerializer.Serialize(page);

    /// <summary>
    /// A real host on a loopback port, serving nothing but the metadata endpoints — enough to prove
    /// routing and content negotiation without a test-host package.
    /// </summary>
    private sealed class ODataService : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ODataService(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ODataService> StartAsync(SynaxODataOptions settings, string prefix = "/odata")
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            builder.Services.AddSingleton(settings);

            var app = builder.Build();

            try
            {
                app.MapSynaxODataMetadata(prefix);
                await app.StartAsync();
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();

            return new ODataService(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
