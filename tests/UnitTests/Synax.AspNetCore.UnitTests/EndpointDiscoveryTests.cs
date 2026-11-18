using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.UnitTests.TestFixtures;

namespace Synax.AspNetCore.UnitTests;

public class EndpointDiscoveryTests
{
    [Test]
    public async Task AddSynaxEndpoints_ShouldRegisterAllEndpointsFromAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSynaxEndpoints(typeof(GetUsersEndpoint).Assembly);
        var provider = services.BuildServiceProvider();
        var endpoints = provider.GetServices<IEndpoint>().ToList();

        // Assert — should find all 5 test endpoints (3 non-versioned + 2 versioned)
        await Assert.That(endpoints.Count).IsEqualTo(5);
    }

    [Test]
    public async Task AddSynaxEndpoints_ShouldIncludeVersionedEndpoints()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSynaxEndpoints(typeof(GetUsersV2Endpoint).Assembly);
        var provider = services.BuildServiceProvider();
        var endpoints = provider.GetServices<IEndpoint>().ToList();
        var versioned = endpoints.OfType<IVersionedEndpoint>().ToList();

        // Assert
        await Assert.That(versioned.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MapSynaxEndpoints_ShouldSeparateVersionedFromNonVersioned()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxEndpoints(typeof(GetUsersEndpoint).Assembly);
        var provider = services.BuildServiceProvider();
        var endpoints = provider.GetServices<IEndpoint>().ToList();

        var nonVersioned = endpoints.Where(e => e is not IVersionedEndpoint).ToList();
        var versioned = endpoints.OfType<IVersionedEndpoint>().ToList();

        // Assert
        await Assert.That(nonVersioned.Count).IsEqualTo(3);
        await Assert.That(versioned.Count).IsEqualTo(2);
    }

    [Test]
    public async Task NonVersionedEndpoints_ShouldGroupByTag()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxEndpoints(typeof(GetUsersEndpoint).Assembly);
        var provider = services.BuildServiceProvider();
        var endpoints = provider.GetServices<IEndpoint>()
            .Where(e => e is not IVersionedEndpoint)
            .ToList();

        var groups = endpoints.GroupBy(e => e.Tag).ToList();

        // Assert — "Users" (2 endpoints) and "Orders" (1 endpoint)
        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups.First(g => g.Key == "Users").Count()).IsEqualTo(2);
        await Assert.That(groups.First(g => g.Key == "Orders").Count()).IsEqualTo(1);
    }

    [Test]
    public async Task AddSynaxEndpoints_WhenSameAssemblyScannedTwice_ShouldRegisterEachEndpointOnce()
    {
        var services = new ServiceCollection();

        services.AddSynaxEndpoints(typeof(GetUsersEndpoint).Assembly);
        services.AddSynaxEndpoints(typeof(GetUsersEndpoint).Assembly);
        var provider = services.BuildServiceProvider();
        var endpoints = provider.GetServices<IEndpoint>().ToList();

        await Assert.That(endpoints.Count).IsEqualTo(5);
    }

    [Test]
    public async Task MapSynaxEndpoints_WhenTagsDifferOnlyByCase_ShouldMapIntoSingleGroup()
    {
        // A struct endpoint is invisible to assembly scanning (IsClass filter), so these
        // fixtures don't disturb the count-based discovery tests above.
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IEndpoint>(new FakeEndpoint("Users", "/list"));
        builder.Services.AddSingleton<IEndpoint>(new FakeEndpoint("users", "/create"));
        await using var app = builder.Build();

        app.MapSynaxEndpoints();

        var mapped = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        await Assert.That(mapped.Count).IsEqualTo(2);

        // Both endpoints share one lowercased prefix and one tag group ("Users", first seen)
        foreach (var endpoint in mapped)
        {
            await Assert.That(endpoint.RoutePattern.RawText!).StartsWith("users/");
            var tags = endpoint.Metadata.GetMetadata<ITagsMetadata>()!.Tags;
            await Assert.That(tags.Single()).IsEqualTo("Users");
        }
    }

    private readonly struct FakeEndpoint(string tag, string pattern) : IEndpoint
    {
        public string Tag => tag;

        public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
            => app.MapGet(pattern, () => TypedResults.Ok());
    }

    [Test]
    public async Task MapSynaxEndpoints_WhenTagIsEmpty_ShouldThrowNamingTheEndpointType()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IEndpoint>(new FakeEndpoint("", "/list"));
        await using var app = builder.Build();

        InvalidOperationException? caught = null;
        try
        {
            app.MapSynaxEndpoints();
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains(nameof(FakeEndpoint));
    }

    [Test]
    public async Task MapSynaxEndpoints_ShouldAssignVersionedGroupName()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IEndpoint>(new FakeVersionedEndpoint("Users", "/list"));
        await using var app = builder.Build();

        app.MapSynaxEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();

        await Assert.That(endpoint.RoutePattern.RawText!).StartsWith("api/v1/users/");
        var groupName = endpoint.Metadata.GetMetadata<EndpointGroupNameAttribute>();
        await Assert.That(groupName).IsNotNull();
        await Assert.That(groupName!.EndpointGroupName).IsEqualTo("v1");
    }

    private readonly struct FakeVersionedEndpoint(string tag, string pattern) : IVersionedEndpoint
    {
        public string Tag => tag;
        public ApiVersion Version => new(1);

        public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
            => app.MapGet(pattern, () => TypedResults.Ok());
    }

    [Test]
    public async Task ApiVersion_ToString_ShouldOmitMinorWhenZero()
    {
        var version = new ApiVersion(2);

        await Assert.That(version.ToString()).IsEqualTo("2");
    }

    [Test]
    public async Task ApiVersion_ToString_ShouldIncludeMinorWhenNonZero()
    {
        var version = new ApiVersion(1, 1);

        await Assert.That(version.ToString()).IsEqualTo("1.1");
    }

    [Test]
    public async Task VersionedEndpoints_ShouldGroupByVersionAndTag()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynaxEndpoints(typeof(GetUsersV2Endpoint).Assembly);
        var provider = services.BuildServiceProvider();
        var versioned = provider.GetServices<IEndpoint>()
            .OfType<IVersionedEndpoint>()
            .ToList();

        var groups = versioned.GroupBy(e => (e.Version, e.Tag)).ToList();

        // Assert — v2/Users and v1.1/Users
        await Assert.That(groups.Count).IsEqualTo(2);
    }
}
