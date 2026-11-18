# Synax.Crud

One-liner CRUD (+ merge-patch) mediator operations for entities that need **no custom logic**. Bridges `Synax.Mediator`, `Synax.EntityFrameworkCore`, and `Synax.AspNetCore` (for `HttpError`): the app declares tiny request classes, and generic handlers serve them through the full mediator pipeline — telemetry, result logging, and scoped global behaviors included.

## Declaring operations

Derive one class per operation from the CRUD request bases. Empty derivations are literal one-liners; create/patch requests carry their body members:

```csharp
public sealed class GetCuisineRequest    : CrudGetRequest<Cuisine, Guid, CuisineResponse>;
public sealed class ListCuisinesRequest  : CrudListRequest<Cuisine, CuisineResponse>;
public sealed class DeleteCuisineRequest : CrudDeleteRequest<Cuisine, Guid>;

public sealed class CreateCuisineRequest : CrudCreateRequest<Cuisine, CuisineResponse>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class PatchCuisineRequest : CrudPatchRequest<Cuisine, Guid, CuisineResponse>
{
    public PatchValue<string> Name { get; set; }
    public PatchValue<string?> Description { get; set; }
}
```

The request class stays the HTTP body DTO — endpoints look exactly like handwritten ones (`sender.SendAsync(request)` → `result.ToHttpResult()`), and telemetry spans carry the real request names.

## Registration

Inside the app's `AddSynaxMediator` call, so CRUD pipelines get the same global behaviors:

```csharp
services.AddSynaxMediator(o =>
{
    o.AddHandlersFromAssembly(applicationAssembly);
    o.AddGlobalBehavior(typeof(MyTransactionBehavior<,>),
        appliesTo: t => typeof(ICrudWriteRequest).IsAssignableFrom(t));   // write ops in transactions
    o.AddSynaxCrud<MyContext>(applicationAssembly);                        // or explicit: (typeof(GetCuisineRequest), ...)
});
```

**A handwritten handler for the same request type always wins** — when an operation outgrows the generic handler, write a normal handler; the generic registration silently steps aside. Nothing else (request, endpoint, clients) changes.

## What the handlers do

| Operation | Mechanism |
|---|---|
| Get | `QueryDto<TResponse>` projection by key → `404 {ENTITY}_NOT_FOUND` or `200` |
| List | key-ordered `QueryDto` → `ToPagedListAsync(Page, PageSize)` |
| Create | **name-matched copy** of the request's members onto a new entity (no mapper profile), v7 Guid for a default key, `AddAsync` + `SaveChangesAsync`, response re-fetched via projection |
| Patch | `ExecutePatchAsync` — one set-based UPDATE of the set `PatchValue<T>` members, modification audit stamped by the executor |
| Delete | `ExecuteDeleteAsync` — soft-delete entities are converted with their stamps |

Ownership scoping, auditing, and soft delete come from the existing interceptors and query filters — foreign tenants get `404`, not `403`. Error codes are convention-derived from the entity CLR name (`MenuItem` → `MENU_ITEM_NOT_FOUND`, `..._CREATE_FAILED`, `..._UPDATE_FAILED`, `..._DELETE_FAILED`).

## Conventions and startup validation

`AddSynaxCrud` validates everything it can at startup, not first request: the entity must expose a public `Id` property matching the declared key type, every create member must copy onto a same-named entity property, and a patch request must declare at least one `PatchValue<T>` member that matches an entity property. Violations throw descriptive `InvalidOperationException`s during registration.

Requirements: an `IRepositoryMapper` registration (for the `QueryDto` projections) and an entity→response mapper profile — the same pair any read endpoint already needs.

## Deliberate non-features

No validation hooks, no outbox events, no generic PUT, no extension points. The moment an operation needs a business rule, an event, or a curated error, promote it to a handwritten handler — that is the intended escape hatch, not configuration.
