using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Synax.AspNetCore.Endpoints;
using Synax.AspNetCore.Errors;
using Synax.AspNetCore.HealthChecks;
using Synax.Crud;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Modules;
using Synax.Results;

namespace Synax.Modules.UnitTests.TestFixtures;

// One fixture per capability. They all live in this assembly, which is therefore the "module
// assembly" every test module owns — so a test that omits a capability sees exactly the fixture
// below reported as unregistered.

public sealed class WidgetContext : ISynaxContext;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

public sealed class Widget : IEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class WidgetResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>The CRUD request with no handwritten handler — served by the generic one.</summary>
public sealed class GetWidgetRequest : CrudGetRequest<Widget, Guid, WidgetResponse>;

public sealed class Gadget : IEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class GadgetResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// The CRUD request that a handwritten handler takes over — the generic registration has to yield
/// to <see cref="GetGadgetHandler"/> no matter which module declared which.
/// </summary>
public sealed class GetGadgetRequest : CrudGetRequest<Gadget, Guid, GadgetResponse>;

public sealed class GetGadgetHandler : IQueryHandler<GetGadgetRequest, Result<GadgetResponse, HttpError>>
{
    public Task<Result<GadgetResponse, HttpError>> HandleAsync(
        GetGadgetRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<Result<GadgetResponse, HttpError>>(new GadgetResponse { Id = request.Id });
}

public sealed record PingQuery(string Value) : IQuery<string>;

public sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
{
    public Task<string> HandleAsync(PingQuery request, CancellationToken cancellationToken = default)
        => Task.FromResult(request.Value);
}

public sealed class WidgetProfile : MapperProfile
{
    public WidgetProfile() => CreateMap<Widget, WidgetResponse>();
}

public sealed class PingEndpoint : IEndpoint
{
    public string Tag => "Ping";

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Ok("pong"));
}

public sealed class PingHealthCheck : ISynaxHealthCheck
{
    public string Name => "Ping";

    public Task<SynaxHealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SynaxHealthCheckResult.Healthy(Name));
}

/// <summary>A module whose <c>Configure</c> is supplied by the test.</summary>
public sealed class DelegateModule(string name, Action<ISynaxModuleBuilder> configure) : ISynaxModule
{
    public string Name => name;

    public void Configure(ISynaxModuleBuilder builder) => configure(builder);
}
