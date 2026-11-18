# Synax.AspNetCore

The bridge between Synax libraries and ASP.NET Core minimal APIs — typed `Result` → HTTP translation, endpoint organization, a global exception handler, and health check infrastructure.

```csharp
private static async Task<Results<Ok<BookingResponse>, NoContent, ProblemHttpResult>> Handler(
    GetBookingRequest request, ISender sender, CancellationToken ct)
{
    var result = await sender.SendAsync(request, ct);
    return result.ToHttpResult();
}
```

Depends only on `Synax.Results`.

## Result → HTTP

`ToHttpResult()` converts a `Result<TData, HttpError>` into a **typed result union**, so OpenAPI/Scalar documents the exact response shapes without extra annotations.

| Overload | Success | Failure |
|---|---|---|
| `Result<TData, HttpError>.ToHttpResult()` | `200 Ok(data)`, or `204 NoContent` when data is null | RFC 9457 Problem Details |
| `Result<TData, HttpError>.ToHttpResult(onSuccess)` | your shape, e.g. `TypedResults.Created(...)`; `204` when data is null (factory not invoked) | RFC 9457 Problem Details |
| `Result<HttpError>.ToHttpResult()` | `204 NoContent` | RFC 9457 Problem Details |

```csharp
// POST endpoints — custom success shape:
return result.ToHttpResult(booking =>
    TypedResults.Created($"/api/v1/bookings/{booking.Id}", booking));
```

## HttpError

The error type handlers put in their `Result`s. It carries an `ErrorCode` (machine-readable), an `HttpCode`, an optional `Description`, and optional field-level `ValidationErrors`.

Prefer the static factories over object initializers:

```csharp
return Result.Fail(HttpError.NotFound($"Restaurant '{id}' was not found."));
// => ErrorCode "NOT_FOUND", HttpCode 404

// Domain-specific code:
return Result.Fail(HttpError.NotFound($"...", errorCode: "RESTAURANT_NOT_FOUND"));

// Validation failure with field errors (PropertyName tells the client which field to mark):
return HttpError.Validation("Request is invalid.",
    validationErrors: [new ValidationError("REQUIRED", "Name is required", nameof(req.Name))]);

// Field-shape validation — every rule evaluates, all failures report together:
var v = new Validation("The booking request is invalid.")
    .Require(req.PartySize > 0, "PARTY_SIZE_INVALID", "Must be positive.", nameof(req.PartySize))
    .Require(req.Date > now, "DATE_IN_PAST", "Must be in the future.", nameof(req.Date));
if (v.HasErrors) return v.ToResult();

// Entity/state checks (DB-backed) return factory errors directly, each keeping its own status:
return HttpError.NotFound($"Restaurant '{id}' was not found.");
```

Factories: `BadRequest` (400), `Unauthorized` (401), `Forbidden` (403), `NotFound` (404), `Conflict` (409), `UnprocessableEntity` (422), `Internal` (500), `Validation` (400 + errors).

On failure, `ToHttpResult` produces Problem Details with `title` = `ErrorCode`, `detail` = `Description`, `status` = `HttpCode` (an unset code below 100 falls back to 500), and validation errors under the `errors` extension. `ValidationError`s can nest via `InnerErrors`.

Author `PropertyName` with `nameof(...)` (nested paths as dotted strings, e.g. `"Address.Street"`); the wire form camelCases each segment (`"address.street"`) so clients can match it against the request JSON they sent, and null members are omitted:

```json
"errors": [{ "code": "PARTY_TOO_LARGE", "description": "Max is 8 seats.", "propertyName": "partySize" }]
```

What `HttpError` carries is everything the client is meant to see. A `Result`'s richer diagnostics — `Messages`, `Exception`, `Origin` — are deliberately **not** serialized by `ToHttpResult`; they are server-side observability data. Route them to logs and traces with the mediator's `AddResultLogging()` and its span enrichment (`HttpError` implements `IHasErrorCode`, so failed spans get a `synax.error_code` tag).

## Endpoints

Implement `IEndpoint` (or `IVersionedEndpoint` for versioned routes), register with an assembly scan, then map:

```csharp
// DI
services.AddSynaxEndpoints(typeof(Program).Assembly);

// Pipeline
app.MapSynaxEndpoints();
```

```csharp
public class GetBookingsEndpoint : IVersionedEndpoint
{
    public string Tag => "Bookings";
    public ApiVersion Version => new(1);          // routes under api/v1/bookings/

    public RouteHandlerBuilder AddEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/", Handler);
}
```

Conventions and guarantees:

- Non-versioned endpoints group under `/{tag}/`, versioned ones under `/api/v{version}/{tag}/` (tag lowercased in the route, original casing kept as the OpenAPI tag; grouping is case-insensitive).
- Versioned groups get an OpenAPI group name of `v{version}`, so documents can be split per version.
- Scanning the same assembly twice is a no-op — duplicate endpoint types are skipped.
- A null or empty `Tag` fails at `MapSynaxEndpoints`, not at first request.
- `AddEndpoint` returns the `RouteHandlerBuilder`, so all standard minimal-API conventions (`RequireAuthorization`, `WithName`, ...) compose normally.

## Current user

`ICurrentUser` / `ICurrentUser<TKey>` expose the authenticated user from the ambient `ClaimsPrincipal`. **Synax consumes identity, it never establishes it**: configure authentication (Keycloak OIDC, JWT bearer, HttpOnly cookies, ...) exactly as you would without Synax — every scheme and transport converges on `HttpContext.User` before this abstraction reads it.

```csharp
services.AddAuthentication().AddJwtBearer(...);   // the app's own business
services.AddSynaxCurrentUser<Guid>();             // default id claim: NameIdentifier ?? "sub"
```

```csharp
public async Task<Result<Booking, HttpError>> HandleAsync(CreateBooking request, CancellationToken ct)
{
    if (!currentUser.TryGetId(out var userId))
        return Result.Fail(HttpError.Unauthorized());
    ...
}
```

- `TryGetId` (rather than a nullable property) because an unconstrained `TKey?` would silently yield `Guid.Empty` for value-type keys. Supported key types: `string`, `Guid`, `int`, `long`; other types get a descriptive error at first use — implement your own `ICurrentUser<TKey>` (registration uses `TryAddScoped`, so yours wins).
- The id claim is configurable: `AddSynaxCurrentUser<Guid>(o => o.UserIdSelector = p => p.FindFirst("oid")?.Value)`.
- Outside an HTTP request (background work, migrations) there is no principal: `IsAuthenticated` is false, ids are null — a first-class state, not an error.
- `Principal` is the escape hatch for claims this deliberately doesn't model (roles, tenant, email). Authorization stays with ASP.NET's policies.

**EF bridge** (`Synax.EntityFrameworkCore.AspNetCore`): one registration implements the EF layer's `IAuditUserProvider`, `ISoftDeleteUserProvider`, and `IOwnerProvider<TKey>` from the current user, so audit stamps, delete stamps, and ownership scoping follow the authenticated user automatically:

```csharp
services.AddSynaxEfCoreIdentityBridge<Guid>();                          // owner = user
services.AddSynaxEfCoreIdentityBridge<Guid>(o => o.OwnerClaim = "tenant_id"); // multi-tenant
```

Ownership is deliberately independent of identity — in multi-tenant apps the owner is the tenant while audit stamps are the user.

### Acting as someone else (`IUserContextScope`)

`IUserContextScope` lives in **Synax.Identity** (`using Synax.Identity;`), a root package with no ASP.NET Core dependency — a worker, an application layer or a seeder can override the ambient identity without referencing the web framework. `AddSynaxCurrentUser` registers it for you.

Inject it to run a block as a specific owner, user, across every owner, or as "system" (explicitly nobody). Key types are inferred from the argument:

```csharp
using (userContext.BecomeOwner(tenantId))        // owner = tenant, no user
    await RecalculateStatsAsync(ct);             // queries scoped to tenant, audit stamps null

using (userContext.BecomeUser(userId))           // user = userId, owner follows
    await sender.SendAsync(new CancelBooking { Id = id }, ct);

using (userContext.BecomeUser(userId, tenantId)) // user and owner independently
    ...

using (userContext.BecomeAllOwners())            // every owner's rows, user left alone
    await ReportAcrossTenantsAsync(ct);

using (userContext.BecomeSystem())               // explicitly nobody — even mid-request
    await SeedSharedDataAsync(ct);               // sees only shared rows, stamps nothing
```

- An active scope **fully replaces** the ambient principal: `Principal` is null inside it, claim-based configuration (id selector, owner claim) doesn't apply, and `BecomeSystem()` beats a real authenticated request. `BecomeAllOwners()` is the one exception — it changes owner scoping only and leaves the principal (or an enclosing scope's user) to stamp audit columns, which is what an admin acting across tenants wants.
- Scopes nest (innermost wins entirely), restore on dispose, and flow across `await` — parallel branches can each hold a different scope.
- A key-type mismatch between the scope and the consumer (`BecomeUser(42)` read as `ICurrentUser<Guid>`) throws a descriptive error.

## Exception handling

A last-resort `IExceptionHandler` producing Problem Details. Handlers should return failing `Result`s — this exists for what slips through.

```csharp
services.AddSynaxExceptionHandler(opts =>
{
    opts.MapException<InsufficientFundsException>(ex =>
        Results.Problem(title: "InsufficientFunds", detail: ex.Message, statusCode: 422));

    // Need the request? There's an overload with HttpContext:
    opts.MapException<TenantNotFoundException>((ctx, ex) =>
        Results.Problem(title: "UnknownTenant", detail: ex.Message,
            statusCode: 404, instance: ctx.Request.Path));
});

app.UseExceptionHandler();   // activates it in the pipeline
```

- Mappings match derived exception types too; the most-derived registered type wins.
- Built-in: `OperationCanceledException` → 499; everything else → 500 with the exception message in Development and a generic message otherwise.
- If the response has already started, the handler logs and writes nothing.

## Health checks

```csharp
services.AddSynaxHealthChecks(opts => opts
    .AddCheck<DatabaseHealthCheck>()
    .AddCheck<CacheHealthCheck>());

app.MapSynaxHealthChecks();                                  // GET /health
app.MapSynaxHealthChecks("/ready", TimeSpan.FromSeconds(5)); // cached variant
```

```csharp
public class DatabaseHealthCheck(BookingDbContext db) : ISynaxHealthCheck
{
    public string Name => "Database";

    public async Task<SynaxHealthCheckResult> CheckAsync(CancellationToken ct)
        => await db.Database.CanConnectAsync(ct)
            ? SynaxHealthCheckResult.Healthy(Name)
            : SynaxHealthCheckResult.Unhealthy(Name, "Cannot connect");
}
```

- Checks run **concurrently**; total latency is the slowest check.
- Response: per-check name/status/description/duration plus an overall status. Statuses serialize as strings (`"Healthy"`). `Unhealthy` → HTTP 503, otherwise 200. Degraded results keep 200 but are visible in the payload.
- `cacheDuration` caches the whole computed response: probes within the window get the cached payload; on expiry a single caller recomputes (single-flight) while concurrent probes wait. Uses `TimeProvider` from DI when registered.
- Registering the same check type twice is a no-op.

## Registration summary

```csharp
// Program.cs
builder.Services
    .AddSynaxEndpoints(typeof(Program).Assembly)
    .AddSynaxExceptionHandler()
    .AddSynaxHealthChecks(opts => opts.AddCheck<DatabaseHealthCheck>());

var app = builder.Build();
app.UseExceptionHandler();
app.MapSynaxEndpoints();
app.MapSynaxHealthChecks();
```
