using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Synax.AspNetCore.Endpoints;

namespace Synax.AspNetCore.UnitTests.TestFixtures;

public class GetUsersEndpoint : IEndpoint
{
    public string Tag => "Users";

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/", () => TypedResults.Ok("users"));
}

public class CreateUserEndpoint : IEndpoint
{
    public string Tag => "Users";

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
        => app.MapPost("/", () => TypedResults.Created());
}

public class GetOrdersEndpoint : IEndpoint
{
    public string Tag => "Orders";

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/", () => TypedResults.Ok("orders"));
}

public class GetUsersV2Endpoint : IVersionedEndpoint
{
    public string Tag => "Users";
    public ApiVersion Version => new(2);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/", () => TypedResults.Ok("users-v2"));
}

public class GetUsersV1_1Endpoint : IVersionedEndpoint
{
    public string Tag => "Users";
    public ApiVersion Version => new(1, 1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/", () => TypedResults.Ok("users-v1.1"));
}
