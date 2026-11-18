# Synax.EntityFrameworkCore

Repository and unit-of-work layer over EF Core, plus opt-in interceptors for soft delete, auditing, and ownership (multi-tenancy). Handlers depend on `IUnitOfWork<TContext>` instead of the `DbContext`, and queries return `ISynaxQuery<T>` — an interface, so handlers are unit-testable without a database.

```csharp
public class GetBookingHandler(IUnitOfWork<BookingContext> unitOfWork)
{
    public async Task<Booking?> HandleAsync(Guid id, CancellationToken ct)
    {
        var repo = unitOfWork.GetRepository<Booking>();
        return await repo.Query(q => q.Where(b => b.Id == id)).FirstOrDefaultAsync(ct);
    }
}
```

## Setup

Entities are plain classes marked with the `IEntity` interface. Register repositories per DbContext by scanning the namespace your entities live in:

```csharp
services.AddDbContext<BookingDbContext>(options => options
    .UseNpgsql(connectionString)
    .AddSoftDeleteInterceptor()     // optional features, see below
    .AddAuditingInterceptor());

services.AddSynaxEfCore(options =>
{
    options.ForDbContext<BookingDbContext>()
        .As<BookingContext>()          // the context tag services are keyed by — see below
        .ScanNamespaceOf<Booking>();   // finds all IEntity classes in that namespace + children
        // or: .ScanNamespace(assembly, "My.App.Entities")
});
```

This registers `IUnitOfWork<BookingContext>`, `IRepository<TEntity, BookingContext>` for every discovered entity, and `IDatabaseManager<BookingContext>` (all scoped by default; lifetimes are configurable on the options).

### Context tags

Services are keyed by a **context tag** — any type implementing `ISynaxContext` — rather than by the `DbContext`. The type argument is unique per context either way, so one module still cannot resolve another module's repositories; what changes is who has to name the `DbContext`.

The simplest form is to let the context be its own tag, which is the right default for an application with a single database:

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), ISynaxContext;

services.AddSynaxEfCore(o => o.ForDbContext<AppDbContext>().ScanNamespaceOf<Order>());

IUnitOfWork<AppDbContext> unitOfWork;   // no tag to write, nothing else to do
```

A **standalone tag** is what you reach for in a modular monolith. Declare it in a project the consuming code already references — the module's domain or contracts assembly — and bind it in infrastructure:

```csharp
// Billing.Contracts — no EF dependency
public sealed class BillingContext : ISynaxContext;

// Billing.Infrastructure — the one place the tag and the context meet
options.ForDbContext<BillingDbContext>().As<BillingContext>().ScanNamespaceOf<Invoice>();

// Billing.Application — names a type its own module owns
public class GetInvoiceHandler(IUnitOfWork<BillingContext> unitOfWork) { ... }
```

The module's application layer then needs no reference to the assembly that defines `BillingDbContext`, and no database driver. Omitting `.As<T>()` means the `DbContext` is its own tag and must implement `ISynaxContext`; forgetting that fails at registration with a message telling you which of the two to do.

## Querying

`repo.Query(...)` composes a query with normal LINQ and returns `ISynaxQuery<T>`, which exposes all EF async materializers as interface members:

```csharp
var repo = unitOfWork.GetRepository<Booking>();

var upcoming = await repo
    .Query(q => q.Where(b => b.Date >= today).OrderBy(b => b.Date))
    .ToListAsync(ct);

var count  = await repo.Query(q => q.Where(b => b.TableId == id)).CountAsync(ct);
var exists = await repo.Query(q => q.Where(b => b.Id == id)).AnyAsync(ct);
var byId   = await repo.GetByIdAsync(id, ct);                    // checks change tracker first
```

`repo.AnyAsync(ct)` / `repo.CountAsync(ct)` are unfiltered shortcuts for "does this repository have any rows" / "how many rows total" — for anything filtered, go through `Query(...)`.

Queries are **no-tracking by default**; pass `asNoTracking: false` when you intend to modify and save the results.

Available materializers: `ToListAsync`, `ToArrayAsync`, `ToHashSetAsync`, `ToDictionaryAsync`, `First/Single/Last[OrDefault]Async`, `Count/LongCount/Any/All/ContainsAsync`, `Min/Max/Sum/AverageAsync`, `AsAsyncEnumerable`, `ForEachAsync`. For anything else (e.g. `GroupBy` with aggregation), use the escape hatch:

```csharp
var perDay = await repo.Query().ExecuteAsync(q =>
    q.GroupBy(b => b.Date).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct));
```

### Projections

Two ways to select only what you need — both translate to SQL and are never tracked:

```csharp
// Inline selector — no mapper needed, result type inferred:
var names = await repo
    .QuerySelect(b => new { b.Id, b.GuestName }, q => q.Where(b => b.Date == today))
    .ToListAsync(ct);

// DTO via the configured mapper:
var dtos = await repo
    .QueryDto<BookingDto>(q => q.Where(b => b.Date == today))
    .ToListAsync(ct);
```

`QueryDto` (and `AddDtoAsync`) go through the `IRepositoryMapper` abstraction. With Synax.Mapper, register the bridge from **Synax.EntityFrameworkCore.Mapper**:

```csharp
services.AddSynaxMapper(assemblies);
services.AddSynaxRepositoryMapper();
```

No registration is needed if you never use the DTO features; forgetting one fails with a descriptive error at first use. Other mappers plug in by implementing `IRepositoryMapper` (two methods).

### Pagination

```csharp
// Offset pagination — order the query first:
PagedResult<BookingDto> page = await repo
    .QueryDto<BookingDto>(q => q.OrderBy(b => b.Date))
    .ToPagedListAsync(page: 2, pageSize: 20, ct);
// page.Items, page.TotalCount, page.TotalPages, page.HasNextPage, ...

// Keyset (cursor) pagination — stable under concurrent writes, fast on deep pages:
KeysetPage<Booking, Guid> first = await repo.Query()
    .ToKeysetPageAsync(b => b.Id, pageSize: 20, ct: ct);
var next = await repo.Query()
    .ToKeysetPageAsync(b => b.Id, 20, after: first.NextCursor, ct: ct);
```

Keyset pagination orders by the key you select — use a unique key (typically the primary key) or rows sharing a boundary value can be skipped.

## Writing

Add/update/remove stage changes; save explicitly:

```csharp
await repo.AddAsync(booking, ct);
repo.Update(booking);
repo.Remove(booking);
await unitOfWork.SaveChangesAsync(ct);
```

Repositories only stage changes — **saving is exclusively the unit of work's job** (`unitOfWork.SaveChangesAsync`). This keeps the save boundary explicit: one save per unit of work, no hidden context-wide flushes from repository calls.

`AddDtoAsync(dto)` maps a DTO to the entity through the registered mapper, stages it, and returns the entity.

### Bulk operations

```csharp
// Set-based update/delete, bypassing the change tracker:
await repo.ExecuteUpdateAsync(
    q => q.Where(b => b.Date < cutoff),
    set => set.SetProperty(b => b.Status, BookingStatus.Archived), ct);

await repo.ExecuteDeleteAsync(q => q.Where(b => b.Status == BookingStatus.Draft), ct);

// Provider-optimized mass insert (COPY on PostgreSQL, SqlBulkCopy on SQL Server):
await repo.BulkInsertAsync(bookings, ct);
```

`BulkInsertAsync` runs client-side value generators (e.g. Guid keys) and applies creation-audit and ownership stamping, mirroring the tracked path.

### Partial updates (PATCH)

Declare a patch DTO whose patchable members are `PatchValue<T>` properties (a three-state optional: **unset** when the JSON property was absent, **set** when present — including an explicit `null`, which clears the value when `T` is nullable). Deserialization tracks presence through the converter on the struct itself — no serializer registration:

```csharp
public class PatchBookingRequest
{
    public Guid Id { get; set; }                       // plain members are not patched
    public PatchValue<string> Notes { get; set; }
    public PatchValue<int> PartySize { get; set; }
}

var affected = await repo.ExecutePatchAsync(
    q => q.Where(b => b.Id == request.Id), request, ct);
```

`ExecutePatchAsync` writes only the **set** members with a single set-based `ExecuteUpdateAsync`, matched to entity properties **by name** — no mapper or profile configuration; a member without a compatible entity property fails on first use with a descriptive error. Because `ExecuteUpdate` bypasses the auditing interceptor, the executor stamps `UpdatedAt`/`ModifiedById` itself when the entity implements the audit interfaces. Query filters (ownership, soft delete) scope the statement like any query. Returns the number of rows addressed — `0` means the query matched nothing; a patch with no set members performs no update and returns the matched count. Unlike raw `ExecuteUpdateAsync`, the Testing fakes emulate `ExecutePatchAsync` fully in-memory.

### Transactions

```csharp
await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
try
{
    // ... multiple saves ...
    await unitOfWork.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
}
catch
{
    await transaction.RollbackAsync(ct);
    throw;
}
```

Nested `BeginTransactionAsync` calls are safe: an inner call returns a non-owning handle whose commit/dispose are no-ops, so only the outermost caller completes the transaction. `unitOfWork.CreateExecutionStrategy()` is available for retry-on-failure providers.

## Soft delete

Implement an interface on the entity and add one registration — deletes become updates, and queries hide deleted rows automatically:

```csharp
public class Booking : IEntity, ISoftDelete            // just IsDeleted
public class Booking : IEntity, ISoftDeleteEntity<User, Guid>  // + DeletedAt, DeletedById/DeletedBy

optionsBuilder.AddSoftDeleteInterceptor();
```

That single call adds the delete-conversion interceptor **and** applies the `!IsDeleted` query filter to every `ISoftDelete` entity — no `OnModelCreating` change needed (a manual `modelBuilder.ApplySoftDeleteQueryFilters()` still works and takes precedence).

- `repo.Remove(...)` and `repo.ExecuteDeleteAsync(...)` are converted to soft deletes. Only the soft-delete columns are written — other columns are never overwritten with stale in-memory values.
- `DeletedAt` comes from an optional DI-registered `TimeProvider`; `DeletedById` from an `ISoftDeleteUserProvider` registered in DI (or passed to the interceptor explicitly).
- To delete for real: `repo.HardDelete(...)`, `HardDeleteRange`, or `ExecuteHardDeleteAsync`.
- To see deleted rows: `repo.Query(q => q.IgnoreQueryFilters(["SynaxSoftDelete"]))`.
- To **restore** a row: read it past the soft-delete filter (tracked), clear the soft-delete columns, and save. There's no dedicated "undelete" call — a restore is a normal update:

```csharp
var entity = await repo
    .Query(q => q.IgnoreQueryFilters(["SynaxSoftDelete"]).Where(x => x.Id == id), asNoTracking: false)
    .FirstOrDefaultAsync(ct);

entity.IsDeleted = false;
entity.DeletedAt = null;          // on the ISoftDeleteEntity<,> variant
entity.DeletedById = null;
repo.Update(entity);
await unitOfWork.SaveChangesAsync(ct);
```

Ignore **only** the soft-delete filter by name — other filters stay active, so ownership still applies and a tenant can only restore its own rows.

## Auditing

Implement the interfaces you need; add one registration:

```csharp
public class Booking : IEntity, IHasCreationTime               // CreatedAt
public class Booking : IEntity, IHasAuditTimes                 // CreatedAt + UpdatedAt
public class Booking : IEntity, IAuditedEntity<User, Guid>     // + CreatedBy(Id), ModifiedBy(Id)

optionsBuilder.AddAuditingInterceptor();
```

- `CreatedAt`/`CreatedById` are stamped on insert — **explicit non-default values win**, so backdating and imports are supported.
- `UpdatedAt`/`ModifiedById` are stamped on every tracked update. Soft deletes are *not* stamped as modifications (`DeletedAt` records those).
- The user id comes from an `IAuditUserProvider` in DI (or an instance passed to `AddAuditingInterceptor`); time from a DI-registered `TimeProvider`. Neither is required — without a user provider only timestamps are written.

## Ownership (multi-tenancy)

Scopes rows to an owner (tenant, company, user) on both write and read:

```csharp
public class Booking : IEntity, IHasOwner<int>          // OwnerId required; only the owner sees it
public class Template : IEntity, IHasOptionalOwner<int?> // null OwnerId = shared, visible to all
```

Three pieces to wire:

```csharp
// 1. A provider that supplies the ambient owner (e.g. from the current HTTP user):
options.ForDbContext<BookingDbContext>()
    .ScanNamespaceOf<Booking>()
    .WithOwnership<int>(o => o.UseProvider<CurrentCompanyProvider>());

// 2. The interceptor — stamps OwnerId on insert, validates filters at startup:
optionsBuilder.AddOwnershipInterceptor<int>();

// 3. The query filters — must stay in OnModelCreating:
public class BookingDbContext(DbContextOptions options, IOwnerProvider<int> ownerProvider) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ... entity configurations ...
        modelBuilder.ApplyOwnershipQueryFilters<int>(() => ownerProvider.GetCurrentOwnerId());
    }
}
```

The accessor lambda **must reference a DbContext member** (field or primary-constructor parameter) — EF re-evaluates such references on every query, which is what makes per-request owners work. A lambda capturing a local variable gets baked into the cached SQL as a constant.

Behavior:

- On insert, entities whose `OwnerId` is still `default` get the ambient owner stamped; a pre-set non-default value is kept. Non-default is the rule: `0`/`Guid.Empty` means "not set".
- `IHasOwner<TKey>` queries return only the current owner's rows; when no owner is ambient, none. `IHasOptionalOwner<TKey?>` also returns shared (`null`-owner) rows.
- Startup validation: model building fails if an ownership-scoped entity lacks the filter, so a forgotten `ApplyOwnershipQueryFilters` is an error, not a silent data leak.
- The filter is named `"SynaxOwnership"` and can be selectively ignored like the soft-delete one.

Acting for a **different** owner — cross-tenant admin operations, background jobs with no ambient owner, or "system" (shared-only) access — is not done by editing the provider. Wrap the calls in an `IUserContextScope` (`Synax.Identity`), which temporarily overrides the ambient owner for every query and stamp inside the block:

```csharp
using (userContext.BecomeOwner(tenantId))        // queries + stamps scoped to this tenant
    await RecalculateStatsAsync(ct);
```

A host with no HTTP principal registers `services.AddSynaxAmbientOwnership<TKey>()`: ownership is then driven purely by the scope, with no ASP.NET Core dependency anywhere. A web host uses `AddSynaxEfCoreIdentityBridge<TKey>` instead, which derives the owner from the principal and consults the same scope on top.

### Reading across every owner

Passing a second accessor to `ApplyOwnershipQueryFilters` adds a cross-owner term to each filter, which `BecomeAllOwners()` switches on:

```csharp
modelBuilder.ApplyOwnershipQueryFilters<Guid>(
    () => ownerProvider.GetCurrentOwnerId(),
    () => ownerProvider.IsCrossOwner());         // same DbContext-member rule applies

using (userContext.BecomeAllOwners())            // one query instead of a loop over tenants
    await ReportAcrossTenantsAsync(ct);
```

- **Reads only.** There is no ambient owner to stamp, so inserting an entity with a required owner inside the scope throws unless `OwnerId` is set explicitly — cross-tenant reporting must not silently produce mis-owned rows.
- It widens *one* filter. Soft-delete filters, base filters and `Where` clauses still apply.
- Omit the second accessor and nothing changes: same filters, same SQL, and `BecomeAllOwners()` affects reads not at all.
- Per-query instead of ambient: `repo.Query(q => q.IgnoreQueryFilters(["SynaxOwnership"]))` remains available.

See the **Synax.AspNetCore** README (`AddSynaxEfCoreIdentityBridge` / `IUserContextScope`) for the identity side.

## Database manager

`IDatabaseManager<TContext>` exposes database-level operations without handing out the context: `EnsureCreated/DeletedAsync`, `CanConnectAsync`, `ProviderName`, `Model`, `ChangeTracker` — useful for health checks and test setup.

## Testing

Because handlers see only interfaces, unit tests need no EF or Docker:

```csharp
// Stub a query result in one line:
var query = SynaxQuery.From(new[] { booking1, booking2 });
```

For full handler tests, the **Synax.EntityFrameworkCore.Testing** package provides `FakeUnitOfWork<TContext>` over plain lists: `uow.Seed(...)` seeds data, `uow.Entities<T>()` asserts final state, and soft-delete/ownership/auditing semantics are emulated with the library's own logic. One known limit: `ExecuteUpdateAsync` throws in the fakes — cover those paths with integration tests.

## Rules of thumb

- Inject `IUnitOfWork<TContext>` into handlers, never the `DbContext`.
- Compose with `Query(...)` and the interface materializers; reach for `ExecuteAsync` only when an operator isn't on the interface.
- Order before paginating; prefer keyset pagination for large or hot tables.
- Save through `unitOfWork.SaveChangesAsync(ct)` — repositories stage changes but never save.
- Enable features where they belong: interceptors on the DbContext options, providers in DI, ownership filters in `OnModelCreating`.
