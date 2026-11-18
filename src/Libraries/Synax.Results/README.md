# Synax.Results

Result types for explicit success/failure flow control — return errors instead of throwing exceptions.

```csharp
public async Task<Result<Booking, HttpError>> HandleAsync(CreateBooking request, CancellationToken ct)
{
    var table = await FindTableAsync(request.TableId, ct);
    if (table is null)
        return Result.Fail(HttpError.NotFound(errorCode: "TABLE_NOT_FOUND"));

    var booking = await CreateBookingAsync(table, request, ct);
    return booking; // implicit Ok
}
```

## The three result types

| Type | Success carries | Failure carries | Use when |
|---|---|---|---|
| `Result` | nothing | nothing (optionally an exception) | you only need pass/fail |
| `Result<TError>` | nothing | a typed error | void operations that can fail with a reason |
| `Result<TData, TError>` | data | a typed error | operations that produce a value |

`TError` is any type you choose — in this repo it's usually `HttpError` from Synax.AspNetCore.

## Creating results

All results are created through static factories on `Result`:

```csharp
Result ok            = Result.Ok();
Result<MyError> ok2  = Result.Ok();                     // converts to any result type
Result<User, MyError> user = Result.Ok(new User(...));  // with data

Result fail          = Result.Fail();                    // untyped failure
Result<User, MyError> notFound = Result.Fail(new MyError("not found")); // typed failure
```

The same `Fail(error)` converts implicitly to both `Result<TError>` and `Result<TData, TError>`, so one error helper works everywhere.

**Prefer the implicit conversions** — return the value for success, the typed error for failure:

```csharp
public Result<User, MyError> GetUser(int id)
{
    var user = _store.Find(id);
    if (user is null) return new MyError("not found");   // implicit Fail
    return user;                                         // implicit Ok
}
```

The failure conversion does **no stack walking**: when the error implements `IHasResultOrigin` (as `HttpError` does — its factories stamp the origin via caller info at creation, zero runtime cost), the origin transfers to the result; otherwise the result has no origin. Use `Result.Fail(error)` when you want the origin captured at the return site for an error type that doesn't carry one.

### Warnings

`Result.Warn()` / `Result.Warn(data)` create a result that counts as success (`IsSuccess == true`) but has `IsWarning == true` — "it worked, but note this".

### Attaching context to failures

```csharp
return Result.Fail(new MyError("import failed"))
    .WithException(caughtException)   // keep the original exception
    .WithMessage("row 42 was invalid"); // add a log message
```

Failures record **where they were created** (`result.Origin` — member, file, line): `Result.Fail(...)` captures the call site via caller info, and the implicit error conversion transfers the origin from `IHasResultOrigin` errors. No stack traces needed to find the source.

On an already-built result, use `AddMessage(...)` to append messages as it bubbles up through layers.

## Checking results

```csharp
var result = GetUser(id);

if (result.IsError)
{
    Console.WriteLine(result.Error.Message); // Error is known non-null here
    return;
}

var user = result.Data; // Data is known non-null when IsSuccess is true
```

`IsSuccess` / `IsError` are annotated so the compiler knows `Data` and `Error` are not null in the right branch — no `!` needed.

Or collapse both branches into one value with `Match`:

```csharp
string label = result.Match(
    user => $"Hello {user.Name}",
    error => $"Failed: {error.Message}");
```

## Wrapping exception-throwing code: `Try` / `TryAsync`

For third-party code that throws, convert exceptions into results at the boundary:

```csharp
Result parsed = Result.Try(() => File.Delete(path));

Result<Config, MyError> config = Result.Try(
    () => JsonSerializer.Deserialize<Config>(json)!,
    ex => new MyError($"Bad config: {ex.Message}"));

Result<byte[], MyError> download = await Result.TryAsync(
    () => httpClient.GetByteArrayAsync(url),
    ex => new MyError("download failed"));
```

The caught exception is attached to the result (`result.Exception`). `OperationCanceledException` is **not** caught — cancellation propagates normally.

## Chaining (combinators)

Instead of nested `if (result.IsError) return ...` blocks, chain steps. A failure short-circuits: later steps are skipped and the error passes through unchanged.

| Method | What it does |
|---|---|
| `Map(data => newData)` | transform the data of a success |
| `Bind(data => Result<...>)` | chain another operation that can itself fail |
| `Ensure(predicate, errorFactory)` | fail a success that doesn't meet a condition |
| `EnsureNotNull(errorFactory)` | turn `Result<T?, E>` into `Result<T, E>`, failing on null |
| `MapError(err => newErr)` | translate the error type (e.g. domain error → `HttpError`) |
| `Tap(action)` | side effect on success (logging etc.), result unchanged |
| `TapError(action)` | side effect on failure, result unchanged |
| `Match(onSuccess, onError)` | exit the result world with a plain value |

Every combinator has an `...Async` version (in `ResultAsyncExtensions`) that works on `Task<Result<...>>` and/or takes async continuations, so chains flow across `await`:

```csharp
return await GetTableAsync(id, ct)                            // Task<Result<Table?, HttpError>>
    .EnsureNotNullAsync(() => Errors.TableNotFound(id))
    .EnsureAsync(t => t.Seats >= partySize, t => Errors.PartyTooLarge(t))
    .BindAsync(t => ReserveAsync(t, ct))                      // returns Task<Result<Reservation, HttpError>>
    .TapAsync(r => logger.LogInformation("Reserved {Id}", r.Id))
    .MapAsync(r => mapper.Map<BookingResponse>(r));
```

`Result<TError>` (void) supports `Bind`, `MapError`, `Ensure`, `Tap`, `TapError`, and `Match` too; its `Bind` can move from void into a data-carrying result.

### Re-typing an error result

To return a failed `Result<TError>` from a method that returns `Result<TData, TError>`:

```csharp
Result<HttpError> validation = Validate(request);
if (validation.IsError)
    return Result<Booking, HttpError>.FromError(validation);
```

## Logging

`logger.LogResult(result)` (namespace `Synax.Results.Logging`) logs a result in one call, choosing the level from its status — Success → Information, Warning → Warning, Error → Error — and including messages, origin, and exception:

```csharp
logger.LogResult(result);
// Result Error: User not found [Origin: GetUser at /src/UserService.cs:42]

// Trim what gets logged:
logger.LogResult(result, new ResultLogOptions { IncludeOrigin = false, IncludeException = false });
```

## Returning results over HTTP

With Synax.AspNetCore, a handler's `Result<TData, HttpError>` converts straight to an endpoint response:

```csharp
var result = await sender.SendAsync(request, ct);
return result.ToHttpResult(); // Ok(data) on success, ProblemDetails on failure
```

## Rules of thumb

- **Never throw for flow control** — return `Result.Fail(...)` with a meaningful error.
- **Return data directly** for success — the implicit operator wraps it.
- Use `Try` / `TryAsync` at boundaries with throwing code; attach the exception rather than swallowing it.
- Add `.WithMessage(...)` / `.AddMessage(...)` breadcrumbs as a failure crosses layers, then `LogResult` once at the top.
