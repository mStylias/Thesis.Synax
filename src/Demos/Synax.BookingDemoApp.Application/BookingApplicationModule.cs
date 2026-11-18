using System.Net;
using Synax.BookingDemoApp.Application.Behaviors;
using Synax.BookingDemoApp.Application.Features.Bookings;
using Synax.BookingDemoApp.Application.Features.Reviews;
using Synax.BookingDemoApp.Domain;
using Synax.Crud;
using Synax.Crud.Modules;
using Synax.EntityFrameworkCore.Mediator.Modules;
using Synax.Mapper.Modules;
using Synax.Mediator;
using Synax.Mediator.Modules;
using Synax.Modules;

namespace Synax.BookingDemoApp.Application;

/// <summary>
/// The application layer's surface: handlers, the behaviors that wrap them, the generic CRUD
/// requests, the mapper profiles, and the outbox.
/// <para>
/// Host-agnostic on purpose — it names the <c>BookingContext</c> tag from the domain and never the
/// concrete <c>DbContext</c>, so the API and the worker both depend on this module rather than one
/// of them depending on the other.
/// </para>
/// </summary>
public sealed class BookingApplicationModule : ISynaxModule
{
    public void Configure(ISynaxModuleBuilder builder)
    {
        builder.AddHandlers();

        // Time every command (evaluated once at startup) — queries stay quiet unless a handler
        // opts in with [Timed]; commands can opt out with [SkipBehavior].
        builder.AddBehavior(typeof(RequestTimingBehavior<,>),
            appliesTo: t => t.GetInterfaces().Any(i =>
                i == typeof(ICommand) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>))));

        // Failed/warning Results are logged automatically (messages, origin, exception) —
        // server-side diagnostics the HTTP bridge never exposes to clients.
        builder.AddResultLogging();

        // Generic CRUD: scans for Crud*Request subclasses (see Features/Cuisines/CuisineCrud.cs)
        // and registers the Synax.Crud handlers for them — handwritten handlers for the same
        // request type always win. Write ops are wrapped in transactions via the marker below.
        builder.AddBehavior(typeof(BookingTransactionBehavior<,>),
            appliesTo: t => typeof(ICrudWriteRequest).IsAssignableFrom(t));
        builder.AddCrud<BookingContext>();

        builder.AddProfiles();

        // Transactional outbox: rows are staged by IOutbox.EnqueueAsync, committed atomically
        // with the business change, and published post-commit by the hosted dispatcher. Only the
        // tag is named here; the interceptor and the table configuration live with the DbContext.
        builder.AddOutbox<BookingContext>(options =>
        {
            options.MaxAttempts = OutboxDemoDefaults.MaxAttempts;

            // Stable stored names decouple the table from CLR type names (refactor-safe).
            options.MapNotification<BookingCreatedNotification>("booking-created");
            options.MapNotification<ReviewSubmittedNotification>("review-submitted");

            // Central permanent-failure policy: a 4xx from a downstream API will never succeed
            // on retry — dead-letter immediately. (Handlers can also throw
            // OutboxDeadLetterException directly; both paths are demonstrated.)
            options.DeadLetterWhen = ex => ex is HttpRequestException
            {
                StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            };
        });
    }
}
