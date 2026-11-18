using Synax.AspNetCore.Endpoints;
using Synax.BookingDemoApp.Application.Exceptions;

namespace Synax.BookingDemoApp.Endpoints.V1.Admin;

/// <summary>
/// No mediator, no Result — this endpoint exists purely to demonstrate the global exception
/// handler's <c>MapException&lt;DemoTeapotException&gt;((httpContext, ex) =&gt; ...)</c> overload
/// mapping to HTTP 418 (see <c>AddPresentation</c>). Throwing for flow control is banned
/// everywhere else in the app; this teapot is the one deliberate exception. Anonymous on
/// purpose so it is easy to poke at.
/// </summary>
public class BrewEndpoint : IVersionedEndpoint
{
    public string Tag => "Admin";
    public ApiVersion Version => new(1);

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/brew", Handler);

    private static string Handler(string drink = "coffee") =>
        throw new DemoTeapotException(drink);
}
