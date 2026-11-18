# Synax.Mediator

A minimal mediator: commands, queries, and notifications dispatched to handlers through pre-built pipelines. All reflection happens once at startup — dispatch is a dictionary lookup.

```csharp
// Define
public record CreateBooking(Guid TableId, int PartySize) : ICommand<Result<BookingDto, HttpError>>;

public class CreateBookingHandler : ICommandHandler<CreateBooking, Result<BookingDto, HttpError>>
{
    public async Task<Result<BookingDto, HttpError>> HandleAsync(CreateBooking command, CancellationToken ct)
        => ...;
}

// Register (scans the assembly for handlers)
services.AddSynaxMediator(typeof(CreateBookingHandler).Assembly);

// Send
var result = await sender.SendAsync(new CreateBooking(tableId, 4), ct);
```

Everything lives in one namespace: `using Synax.Mediator;`.

## Requests and handlers

| Request marker | Handler interface | Returns |
|---|---|---|
| `ICommand` | `ICommandHandler<TCommand>` | nothing (`Task`) |
| `ICommand<TResponse>` | `ICommandHandler<TCommand, TResponse>` | `TResponse` |
| `IQuery<TResponse>` | `IQueryHandler<TQuery, TResponse>` | `TResponse` |
| `IRequest<TResponse>` | `IRequestHandler<TRequest, TResponse>` | `TResponse` |

`ICommand`/`IQuery` exist to state intent; they are aliases over `IRequest`. Void command handlers implement plain `Task HandleAsync(...)` — no `Unit` ceremony.

Each request type has **exactly one handler** — a second registration fails at startup. Extra inputs a caller wants to pass alongside the payload (flags, preloaded data) are just properties on the request:

```csharp
public record GetBooking(Guid Id) : IQuery<BookingDto>
{
    public QueryOptions Options { get; set; } = new();
}

await sender.SendAsync(new GetBooking(id) { Options = new() { IncludeHistory = true } }, ct);
```

## Notifications

A notification fans out to zero or more handlers:

```csharp
public record BookingCreated(Guid BookingId) : INotification;

public class SendConfirmationEmail : INotificationHandler<BookingCreated> { ... }
public class LogBookingCreated    : INotificationHandler<BookingCreated> { ... }

await publisher.PublishAsync(new BookingCreated(id), ct);                                   // default strategy
await publisher.PublishAsync(new BookingCreated(id), NotificationPublishStrategy.Parallel, ct);
```

`Sequential` (default) awaits handlers one at a time in registration order; `Parallel` starts all handlers and awaits them together (`Task.WhenAll`).

## Validation

A handler opts into validation by implementing `IWithValidation<TRequest, TValidation>`. If `ValidateAsync` returns an error result, the handler is skipped and the error becomes the response — no exceptions:

```csharp
public class CreateBookingHandler
    : ICommandHandler<CreateBooking, Result<BookingDto, HttpError>>,
      IWithValidation<CreateBooking, Result<HttpError>>
{
    public async Task<Result<HttpError>> ValidateAsync(CreateBooking cmd, CancellationToken ct)
        => cmd.PartySize > 0 ? Result.Ok() : Result.Fail(new HttpError(...));

    public async Task<Result<BookingDto, HttpError>> HandleAsync(CreateBooking cmd, CancellationToken ct)
        => ...; // only runs when validation passed
}
```

The validation error type must match the response's `TError`; mismatches fail at startup. Validation may also be declared against a base type of the request.

### Validation that produces data — "parse, don't validate"

Good validation is often the handler's first fetch: load the entity, check it, then *use it*. Declare `TValidation` as a data-carrying `Result<TState, TError>` and read the state in `HandleAsync` with `this.ValidationData()` — fetched once, typed, non-null, guaranteed validated:

```csharp
public class CreateBookingHandler(IUnitOfWork<BookingContext> uow)
    : ICommandHandler<CreateBooking, Result<BookingResponse, HttpError>>,
      IWithValidation<CreateBooking, Result<Table, HttpError>>
{
    public async Task<Result<Table, HttpError>> ValidateAsync(CreateBooking req, CancellationToken ct)
    {
        var table = await uow.GetRepository<Table>()
            .Query(q => q.Where(t => t.Id == req.TableId)).FirstOrDefaultAsync(ct);

        if (table is null) return HttpError.NotFound($"Table '{req.TableId}' was not found.");
        if (req.PartySize > table.Seats) return HttpError.Validation($"Max is {table.Seats} seats.");

        return table;   // implicit Ok — the state flows to HandleAsync
    }

    public Task<Result<BookingResponse, HttpError>> HandleAsync(CreateBooking req, CancellationToken ct)
    {
        var table = this.ValidationData();   // Table — no second fetch, no null checks
        ...
    }
}
```

Plain code with ifs — full DI, any repository access, no rules DSL. The terse `return HttpError.NotFound(...)` works because a typed error converts implicitly to a failed result (origin included — see the Synax.Results README).

When invoking a validated handler **directly** (unit tests with the Testing fakes, no pipeline), use `handler.ValidateAndHandleAsync(request, ct)` — it runs validate-then-handle in pipeline order, short-circuiting failures into the response type exactly like the real pipeline.

## Pipeline behaviors

Behaviors wrap the handler with cross-cutting logic (logging, transactions, caching):

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // before
        var response = await next();
        // after
        return response;
    }
}
```

Attach globally, per handler, or opt out per handler:

```csharp
// every request
services.AddSynaxMediator(opts => opts
    .AddHandlersFromAssemblyContaining<CreateBookingHandler>()
    .AddGlobalBehavior(typeof(LoggingBehavior<,>)));

// every request matching a predicate (evaluated once at startup) — e.g. commands only
opts.AddGlobalBehavior(typeof(TransactionBehavior<,>),
    appliesTo: t => t.GetInterfaces().Any(i =>
        i == typeof(ICommand) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>))));

// one handler
[MediatorBehavior(typeof(CachingBehavior<,>))]
public class GetBookingHandler : IQueryHandler<GetBooking, BookingDto> { ... }

// opt out of a global behavior
[SkipBehavior(typeof(LoggingBehavior<,>))]
public class HealthPingHandler : IQueryHandler<HealthPing, bool> { ... }
```

`MediatorBehaviorAttribute` can also be subclassed to create named attributes (e.g. `[Cached]`). Execution order: global behaviors (registration order) → attribute behaviors → validation → handler.

## Registration

```csharp
services.AddSynaxMediator(assembly1, assembly2);
// or
services.AddSynaxMediator(opts => opts
    .AddHandlersFromAssemblyContaining<SomeHandler>()
    .AddGlobalBehavior(typeof(LoggingBehavior<,>)));
```

- `IMediator` (= `ISender` + `IPublisher`) is registered scoped; inject the narrowest interface you need.
- Handlers and behaviors default to scoped (`MediatorOptions.HandlerLifetime`).
- Calling `AddSynaxMediator` more than once **merges**: later calls add their assemblies' handlers to the same registry; already-scanned assemblies are skipped. The first call's options define the defaults (e.g. `NotificationPublishStrategy`).
- Global behaviors are **application-wide, not call-scoped**. A behavior registered by one call wraps every handler, including those discovered by another call — before or after it — so a modular app can register each module (handlers, behaviors) with its own `AddSynaxMediator` in any order. Registering the same behavior type again widens its scope (predicates are OR-ed, an unconditional registration wins) rather than attaching it twice, and it keeps the pipeline position of its first registration.
- Configuration errors — duplicate handlers for one request, a behavior that doesn't implement `IPipelineBehavior<,>` for its request, validation/response `TError` mismatch — throw at startup, not at first send.

## Observability

The mediator emits an `ActivitySource` and a `Meter`, both named `MediatorTelemetry.SourceName` (`"Synax.Mediator"`). With no listener attached the overhead is a single boolean check per send/publish.

```csharp
services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(MediatorTelemetry.SourceName))
    .WithMetrics(m => m.AddMeter(MediatorTelemetry.SourceName));
```

- **Spans**: `send {RequestType}` / `publish {NotificationType}`, tagged with the request/notification type (and publish strategy). A handler returning a failed `Result` or throwing marks the span as an error.
- **Failed-result enrichment**: an error span carries the result's context as tags — `synax.error_code` (when the error implements `Synax.Results.IHasErrorCode`, as `HttpError` does), `synax.result_origin` (member/file/line where the failure was created), `synax.result_messages`, plus the attached exception as a span exception event. Warning results tag `synax.result_status = warning`.
- **Metrics**: `synax.mediator.requests` + `synax.mediator.request.duration` (seconds) and the notification equivalents, tagged with type and status (`ok` / `error` / `exception`).

### Result logging

Failed and warning `Result`s carry diagnostics (`.WithMessage`, `.WithException`, origin) that are server-side only — the HTTP bridge deliberately never exposes them to clients. `AddResultLogging()` makes sure they land in the logs instead of evaporating:

```csharp
services.AddSynaxMediator(opts => opts
    .AddHandlersFromAssembly(...)
    .AddResultLogging());              // optionally pass ResultLogOptions to trim fields

// log (category = request full name, level from result status):
// "Result Error: row 42 was invalid [Origin: HandleAsync at /src/...cs:42]" + exception
```

Successful results are not logged. Under the hood it's a global `ResultLoggingBehavior<,>` calling `ILogger.LogResult` from Synax.Results, so `ResultLogOptions` (include/exclude status, messages, origin, exception) applies as-is.

## Transactions (companion package)

`Synax.EntityFrameworkCore.Mediator` ships `TransactionBehavior<TContext, TRequest, TResponse>`: it wraps the handler in a unit-of-work transaction, commits on success, rolls back when the handler returns a failed `Result` or throws (rethrowing). Subclass it with your context to get an attachable 2-arity behavior:

```csharp
public sealed class AppTransactionBehavior<TRequest, TResponse>(IUnitOfWork<AppContext> uow)
    : TransactionBehavior<AppContext, TRequest, TResponse>(uow)
    where TRequest : IRequest<TResponse>;

[MediatorBehavior(typeof(AppTransactionBehavior<,>))]   // per handler
public class CreateBookingHandler : ... { }
```

or scope it to all commands with `AddGlobalBehavior(typeof(AppTransactionBehavior<,>), appliesTo: ...)`. Nesting is safe — an inner scope joins the active transaction and only the outermost commit counts.

## Transactional outbox (companion package)

`Synax.EntityFrameworkCore.Mediator` also ships a transactional outbox: publish notifications reliably *after* the unit of work commits, with an in-process hot path — the database is only read back for retries and crash recovery.

```csharp
// three explicit registrations:
optionsBuilder.AddOutboxInterceptor<BookingContext, BookingDbContext>();  // DbContext options
modelBuilder.AddSynaxOutbox<BookingContext>();            // OnModelCreating (owns the migration)
services.AddSynaxOutbox<BookingContext>(o =>
{
    o.MaxAttempts = 5;
    o.MapNotification<BookingCreated>("booking-created");  // stable stored names (optional)
});
```

```csharp
// in a handler — staged with the business change, atomic by construction:
await outbox.EnqueueAsync(new BookingCreated(booking.Id), ct);
await unitOfWork.SaveChangesAsync(ct);
```

How it flows: `EnqueueAsync` stages a row through the same `SaveChangesAsync` as the business change. On commit (deferred past the outermost transaction commit — composes with `TransactionBehavior`), the interceptor hands the row id plus the already-materialized notification to a bounded in-process channel; the dispatcher publishes it via `IPublisher` — so the handler is a plain notification handler — and deletes the row. **Zero database reads on the happy path.**

An outbox event's handler is an ordinary `INotificationHandler<T>` — no outbox-specific interface, same discovery, same telemetry:

```csharp
public sealed record BookingCreated(Guid BookingId, string CustomerEmail) : INotification;

public sealed class SendBookingConfirmationHandler(IEmailService email)
    : INotificationHandler<BookingCreated>
{
    public async Task HandleAsync(BookingCreated notification, CancellationToken ct)
    {
        // Delivery is at-least-once — make this idempotent
        // (e.g. skip when a confirmation for this booking was already sent).
        await email.SendBookingConfirmationAsync(
            notification.CustomerEmail, notification.BookingId, ct);
    }
}
```

A thrown exception means "failed, retry later". To fail **without** retry, see *Permanent failures* below.

Failure and recovery: a failed dispatch schedules a retry (exponential backoff, `Backoff` configurable); a periodic sweep (default 30s) picks up due retries and orphaned rows (lost signal, crash, another instance's death) with optimistic claiming (`WHERE Attempts = seen`), so multiple instances never double-dispatch. The channel is drop-on-full — the row is the source of truth, so backpressure can never block a save. Rows exhausting `MaxAttempts` stay in the `SynaxOutbox` table as dead letters with their last error.

**Permanent failures — fail without retry.** When retrying is pointless (invalid recipient, malformed data, a 4xx from a downstream API), dead-letter the message immediately instead:

```csharp
// At the failure site, for failures your handler recognizes:
public async Task HandleAsync(BookingCreated notification, CancellationToken ct)
{
    if (!EmailAddress.IsValid(notification.CustomerEmail))
        throw new OutboxDeadLetterException(
            $"Recipient '{notification.CustomerEmail}' is permanently invalid.");
    ...
}

// Or centrally, for exception types you don't control:
services.AddSynaxOutbox<BookingContext>(o =>
{
    o.DeadLetterWhen = ex => ex is HttpRequestException
    {
        StatusCode: >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
    };
});
```

Both routes mark the row dead immediately (kept with its error, skipped by the sweep — same fate as retry exhaustion, inspectable and manually requeueable). `OutboxDeadLetterException` is always honored, wherever it sits in the exception chain (including aggregates from parallel publish); the predicate is checked against every exception in the chain.

Contracts: delivery is **at-least-once — handlers must be idempotent**; ordering is best-effort, not guaranteed. Prefer `MapNotification<T>("stable-name")` so renaming a CLR type doesn't strand stored payloads.

Provider note: the sweep query compares `DateTimeOffset` columns, which Postgres/SQL Server translate natively but Sqlite does not — on Sqlite, map `CreatedAt`/`NextAttemptAt` with `DateTimeOffsetToBinaryConverter` (safe here: outbox timestamps are always UTC).

## Design limits (deliberate)

Kept out to keep the library small — know them up front:

- **No polymorphic notification dispatch.** Handlers match the notification's exact runtime type; publishing a notification with zero handlers is a silent no-op.
- **No open-generic requests or handlers.** Pipelines are built at startup from closed types found by scanning.
- **Void commands can't declare validation.** Result-based short-circuiting needs a `Result`-typed response to carry the error; give the command a `Result<TError>` response instead.
- **Notification failure semantics follow the strategy.** `Sequential` stops at the first throwing handler; `Parallel` runs all handlers and surfaces the first exception after all complete. Handlers that shouldn't abort siblings should catch their own exceptions.
- **Requests are invariant.** `IRequest<TResponse>` has no `out` variance, so a request can only be sent as its declared response type — this is what makes dispatch a single lookup.
