using Microsoft.EntityFrameworkCore;
using Synax.AspNetCore.Modules;
using Synax.BookingDemoApp.Application;
using Synax.BookingDemoApp.Application.Exceptions;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Infrastructure;
using Synax.Modules;

namespace Synax.BookingDemoApp;

/// <summary>
/// The API host's module: the HTTP surface, plus the two host-agnostic modules it needs.
/// <para>
/// The application and infrastructure layers are siblings — neither references the other — so a
/// host assembly is the only place that can name both. That is what <c>DependsOn</c> is for, and
/// it is why a second host (see <c>Synax.BookingDemoApp.Worker</c>) reuses those layers without
/// referencing this project or its endpoints.
/// </para>
/// </summary>
public sealed class BookingApiModule : ISynaxModule
{
    public void Configure(ISynaxModuleBuilder builder)
    {
        builder.DependsOn<BookingApplicationModule>();
        builder.DependsOn<BookingInfrastructureModule>();

        builder.AddEndpoints();

        builder.AddExceptionHandling(options =>
        {
            // Simple overload: exception -> result.
            options.MapException<DbUpdateConcurrencyException>(_ =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: ErrorCodes.Common.ConcurrencyConflict,
                    detail: "The record was modified by someone else. Reload and retry.",
                    statusCode: StatusCodes.Status409Conflict));

            // Context-aware overload: (HttpContext, exception) -> result.
            options.MapException<DemoTeapotException>((context, ex) =>
                Microsoft.AspNetCore.Http.Results.Problem(
                    title: "IAmATeapot",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status418ImATeapot,
                    instance: context.Request.Path));
        });
    }
}
