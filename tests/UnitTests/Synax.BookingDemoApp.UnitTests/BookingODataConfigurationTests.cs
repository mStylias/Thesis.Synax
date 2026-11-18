using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Microsoft.Extensions.Logging;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Synax.EntityFrameworkCore.OData;

namespace Synax.BookingDemoApp.UnitTests;

/// <summary>
/// The API host opts into <c>$compute</c>, <c>$search</c>, <c>$apply</c> and server-driven paging in
/// <c>AddPresentation</c>. Those are declarations validated at registration and at first query, so
/// this exercises the app's *real* <see cref="SynaxODataOptions"/> instance against canned data —
/// a broken searchable member or a mis-scoped guardrail fails here rather than on a live request.
/// The nested <c>$expand</c> options need no opting in, but they are gated by the same settings, so
/// they are exercised against the same instance. The exposed types are declarations too — the entity
/// set names in <c>$metadata</c> are a published contract, and a renamed DTO must not quietly rename
/// a client's entity set.
/// </summary>
public class BookingODataConfigurationTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "synax-demo",
            ["Jwt:Audience"] = "synax-demo",
            ["Jwt:SigningKey"] = "demo-signing-key-that-is-long-enough-for-hmac-sha256"
        })
        .Build();

    private static SynaxODataOptions AppSettings()
    {
        var services = new ServiceCollection();
        services.AddPresentation(Configuration(), new FakeEnvironment());

        return services.BuildServiceProvider().GetRequiredService<SynaxODataOptions>();
    }

    private static ISynaxQuery<RestaurantResponse> Restaurants() => SynaxQuery.From(
    [
        new RestaurantResponse { Id = Guid.Parse("00000001-0000-0000-0000-000000000000"), Name = "Harbour Grill", Address = "1 Quay Street", PhoneNumber = "555-0100", MaxCapacity = 80 },
        new RestaurantResponse { Id = Guid.Parse("00000002-0000-0000-0000-000000000000"), Name = "Mountain View", Address = "9 Summit Road", PhoneNumber = "555-0200", MaxCapacity = 40 },
        new RestaurantResponse { Id = Guid.Parse("00000003-0000-0000-0000-000000000000"), Name = "Corner Cafe", Address = "3 Harbour Lane", PhoneNumber = "555-0300", MaxCapacity = 12 }
    ]);

    /// <summary>The detail shape, which is the one the app exposes with a navigation to expand.</summary>
    private static ISynaxQuery<RestaurantDetailResponse> Detail() => SynaxQuery.From(
    [
        new RestaurantDetailResponse
        {
            Id = Guid.Parse("00000001-0000-0000-0000-000000000000"),
            Name = "Harbour Grill",
            Address = "1 Quay Street",
            PhoneNumber = "555-0100",
            MaxCapacity = 80,
            CuisineName = "Seafood",
            Tables =
            [
                new TableResponse { Id = Guid.Parse("00000011-0000-0000-0000-000000000000"), Number = 1, Seats = 2, Location = "Indoor" },
                new TableResponse { Id = Guid.Parse("00000012-0000-0000-0000-000000000000"), Number = 2, Seats = 4, Location = "Indoor" },
                new TableResponse { Id = Guid.Parse("00000013-0000-0000-0000-000000000000"), Number = 3, Seats = 6, Location = "Outdoor" }
            ]
        }
    ]);

    [Test]
    public async Task Search_LooksInNameAddressAndPhone()
    {
        var settings = AppSettings();

        var byName = await Restaurants().ToODataPageAsync(Request("?$search=grill"), settings);
        await Assert.That(Names(byName)).IsEquivalentTo(["Harbour Grill"]);

        // "harbour" is in one name and a different restaurant's address — both come back.
        var byAddress = await Restaurants().ToODataPageAsync(Request("?$search=harbour&$orderby=name"), settings);
        await Assert.That(Names(byAddress)).IsEquivalentTo(["Corner Cafe", "Harbour Grill"]);

        // Quoted because the OData search lexer will not start a bare term with a digit.
        var byPhone = await Restaurants().ToODataPageAsync(Request("?$search=%22555-0200%22"), settings);
        await Assert.That(Names(byPhone)).IsEquivalentTo(["Mountain View"]);
    }

    [Test]
    public async Task Search_AgainstTablesIsADeliberateClientError()
    {
        // TableResponse declares no searchable members — its only string is an enum projection.
        var tables = SynaxQuery.From(new List<TableResponse>
        {
            new() { Id = Guid.NewGuid(), Number = 1, Seats = 4, Location = "Indoor" }
        });

        Func<Task> act = () => tables.ToODataPageAsync(Request("?$search=indoor"), AppSettings());

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(TableResponse));
    }

    [Test]
    public async Task Compute_IsAllowedOverTheDeclaredNumericMembersOnly()
    {
        var settings = AppSettings();

        var page = await Restaurants().ToODataPageAsync(
            Request("?$compute=maxCapacity div 4 as tableCount&$filter=tableCount ge 10&$orderby=name"),
            settings);
        await Assert.That(Names(page)).IsEquivalentTo(["Harbour Grill", "Mountain View"]);

        // 'name' is not in AllowedComputeProperties.
        Func<Task> overAnotherMember = () => Restaurants().ToODataPageAsync(
            Request("?$compute=concat(name,'!') as shout&$select=shout"), settings);
        await Assert.That(overAnotherMember).Throws<SynaxODataQueryException>();
    }

    [Test]
    public async Task Apply_IsOnAndRestrictedToTheReportingAggregations()
    {
        var settings = AppSettings();

        var page = await Restaurants().ToODataPageAsync(
            Request("?$apply=aggregate(maxCapacity with sum as seats,$count as venues)"),
            settings);

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row["seats"]).IsEqualTo(132);
        await Assert.That(row["venues"]).IsEqualTo(3L);

        // countdistinct is deliberately not in AllowedAggregations.
        Func<Task> distinct = () => Restaurants().ToODataPageAsync(
            Request("?$apply=aggregate(name with countdistinct as names)"), settings);

        var exception = await Assert.That(distinct).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("countdistinct");
    }

    [Test]
    public async Task Paging_IsOnAndCapsResponsesWithANextLink()
    {
        var settings = AppSettings();
        await Assert.That(settings.PageSize).IsEqualTo(25);

        // Only three rows here, so verify the mechanics with an explicitly smaller page.
        var page = await Restaurants().ToODataPageAsync(
            Request("?$orderby=name"),
            new SynaxODataOptions { PageSize = 2 });

        await Assert.That(page.Value).Count().IsEqualTo(2);
        await Assert.That(page.NextLink).IsNotNull();
    }

    [Test]
    public async Task Expand_CarriesTheClientsNestedOptionsIntoTheExpandedCollection()
    {
        var settings = AppSettings();

        var page = await Detail().ToODataPageAsync(
            Request("?$select=name&$expand=tables($filter=seats ge 4;$orderby=number desc;$top=1;$count=true)"),
            settings);

        var row = (IDictionary<string, object?>)page.Value[0]!;
        var tables = ((List<object?>)row["tables"]!).Cast<IDictionary<string, object?>>().ToList();

        await Assert.That(tables).Count().IsEqualTo(1);
        await Assert.That(tables[0]["number"]).IsEqualTo(3);

        // The count is the whole filtered collection, not the one row $top left.
        await Assert.That(row["tables@odata.count"]).IsEqualTo(2L);
    }

    [Test]
    public async Task Expand_UsesTheSameSwitchesAsTheTopLevelOptions()
    {
        var settings = AppSettings();

        // $compute is on for this app, so it is on inside $expand too...
        var computed = await Detail().ToODataPageAsync(
            Request("?$select=name&$expand=tables($compute=seats div 2 as pairs;$select=number,pairs;$orderby=number)"),
            settings);

        var first = (IDictionary<string, object?>)
            ((List<object?>)((IDictionary<string, object?>)computed.Value[0]!)["tables"]!)[0]!;
        await Assert.That(first["pairs"]).IsEqualTo(1);

        // ...and $search against TableResponse stays the same deliberate 400 nested as it is at the
        // top level, rather than quietly expanding every table.
        Func<Task> nestedSearch = () => Detail().ToODataPageAsync(
            Request("?$select=name&$expand=tables($search=indoor)"), settings);

        var exception = await Assert.That(nestedSearch).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(TableResponse));
    }

    [Test]
    public async Task Metadata_PublishesTheSearchShapesAsOneService()
    {
        // Served by the app's own registrations, over HTTP, the way a client would fetch it.
        await using var service = await MetadataHost.StartAsync();

        using var reader = XmlReader.Create(await service.Client.GetStreamAsync("/odata/$metadata"));

        // Round-tripped through the OData package's own reader, so this is a document a generic
        // client can consume, not merely well-formed XML.
        var parsed = CsdlReader.TryParse(reader, out var model, out var errors);
        await Assert.That(parsed).IsTrue().Because(string.Join("; ", errors));

        var sets = model.EntityContainer.EntitySets().Select(set => set.Name).ToList();
        await Assert.That(sets).IsEquivalentTo(new[] { "restaurants", "tables" });

        var restaurants = model.EntityContainer.FindEntitySet("restaurants").EntityType;
        await Assert.That(restaurants.Key().Select(key => key.Name).ToList())
            .IsEquivalentTo(new[] { "id" });
        await Assert.That(restaurants.DeclaredProperties.Select(property => property.Name).ToList())
            .Contains("maxCapacity");
    }

    [Test]
    public async Task ServiceDocument_ListsWhatAClientMayQuery()
    {
        await using var service = await MetadataHost.StartAsync();

        using var document = JsonDocument.Parse(await service.Client.GetStringAsync("/odata"));

        var sets = document.RootElement.GetProperty("value").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString()!)
            .ToList();

        await Assert.That(sets).IsEquivalentTo(new[] { "restaurants", "tables" });
    }

    [Test]
    public async Task Annotations_AreOffForTheDemo_ButTheDeclarationsBehindThemAreRight()
    {
        // Left off in AddPresentation because the endpoints return typed DTOs; turning them on here
        // proves the exposed names would produce the right context and id URLs if an app wanted them.
        var settings = AppSettings();
        await Assert.That(settings.EnableAnnotations).IsFalse();

        settings.EnableAnnotations = true;
        settings.ServiceRoot = new Uri("https://booking.example.com/odata");

        var page = await Restaurants().ToODataPageAsync(Request("?$filter=maxCapacity eq 80"), settings);

        await Assert.That(page.Context)
            .IsEqualTo("https://booking.example.com/odata/$metadata#restaurants");

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row["@odata.id"])
            .IsEqualTo("https://booking.example.com/odata/restaurants(00000001-0000-0000-0000-000000000000)");
        await Assert.That(row["name"]).IsEqualTo("Harbour Grill");
    }

    [Test]
    public async Task Format_JsonIsTheOnlyOneTheEndpointsAnswer()
    {
        var settings = AppSettings();

        var json = await Restaurants().ToODataPageAsync(Request("?$format=json&$orderby=name"), settings);
        await Assert.That(json.Value).Count().IsEqualTo(3);

        Func<Task> xml = () => Restaurants().ToODataPageAsync(Request("?$format=xml"), settings);
        var exception = await Assert.That(xml).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains("xml");
    }

    private static HttpRequest Request(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/api/v1/restaurants/search";
        context.Request.QueryString = new QueryString(queryString);

        return context.Request;
    }

    private static List<string> Names(ODataPage page)
        => page.Value.Cast<RestaurantResponse>().Select(restaurant => restaurant.Name).ToList();

    /// <summary>
    /// The API's real OData registrations behind a loopback Kestrel, so the published documents are
    /// the ones <c>AddPresentation</c> + <c>MapSynaxODataMetadata</c> actually serve.
    /// </summary>
    private sealed class MetadataHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private MetadataHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<MetadataHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            // The options AddPresentation produces, rather than AddPresentation itself: starting the
            // full API host would run the demo seeder, which wants a database. The declarations
            // under test are the same object either way.
            builder.Services.AddSingleton(AppSettings());

            var app = builder.Build();

            try
            {
                app.MapSynaxODataMetadata();
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

            return new MetadataHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Synax.BookingDemoApp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
