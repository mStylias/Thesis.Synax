# Synax.Mapper

Expression-based object mapper. One mapping definition works both **in memory** (`mapper.Map<TDest>(entity)`) and as an **EF Core projection** (`query.ProjectTo<TDest>(mapper)` translates to a SQL `SELECT`).

```csharp
public class OrderProfile : MapperProfile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderDto>()
            .For(d => d.Total, s => s.Lines.Sum(l => l.Price * l.Qty));
        // Id, CustomerName, etc. map automatically by name
    }
}
```

## Setup

Scan assemblies for `MapperProfile` subclasses (the usual way):

```csharp
services.AddSynaxMapper(typeof(OrderProfile).Assembly); // params — pass several assemblies if needed
```

Or register profiles explicitly:

```csharp
services.AddSynaxMapper(cfg =>
{
    cfg.AddProfile<OrderProfile>();
    cfg.AddProfile<CustomerProfile>();
});
```

Both register `IMapper` as a singleton — inject it wherever you map.

`AddSynaxMapper` **merges** across calls: every call contributes its profiles to one shared configuration, which is compiled once when `IMapper` or `MapperConfiguration` is first resolved. Modules can therefore register their own profiles without erasing anyone else's, and an assembly two modules both scan is only scanned once. Duplicate mappings are reported at registration; errors that need the maps compiled (cyclic mappings, invalid bindings) surface on first resolve — call `AssertConfigurationIsValid()` at startup to pull them forward.

Without DI, build a configuration directly:

```csharp
var config = new MapperConfiguration(cfg => cfg.AddProfile<OrderProfile>());
IMapper mapper = config.CreateMapper();
```

## Defining mappings

Subclass `MapperProfile` and call `CreateMap<TSource, TDest>()` in the constructor for each pair. The builder has three methods:

```csharp
CreateMap<Order, OrderDto>()
    .For(d => d.FullName, s => s.First + " " + s.Last)  // explicit binding, any expression
    .For(d => d.Address, s => s.ShippingAddress)        // nested — uses the registered Address map
    .For(d => d.Items, s => s.OrderLines)               // collection — maps each element
    .Ignore(d => d.InternalNotes);                      // leave at default, on purpose
```

**Automapping is on by default** — after your explicit `For` bindings, remaining destination properties bind to same-named source properties. Don't call `.AutoMap()` explicitly; call `.AutoMap(false)` only to opt out and map everything by hand.

### What's handled automatically

- **Nested objects** — if a map is registered for the property types, it's inlined (with a null guard when the source can be null). Works for `For` bindings and automapped properties alike.
- **Collections** — when both sides are `IEnumerable<T>` and the element types have a registered map, each element is projected. Materializes to the destination's declared type (`List<T>`, `T[]`, `HashSet<T>`, ...).
- **Safe conversions** — numeric widening (`int` → `long`), `int` → `int?`, enum → enum, enum → string, and user-defined implicit operators. Narrowing conversions are only applied in explicit `For` bindings, never by automapping.
- **Records and immutable types** — destinations without a parameterless constructor are built through their constructor, matching bindings to parameters by name. Positional records just work.

### Global converters

`ConvertUsing` registers a conversion applied **everywhere** a member of that type pair is bound — explicit bindings, automapped properties, and collection elements. It wins over every other strategy:

```csharp
public class CommonProfile : MapperProfile
{
    public CommonProfile()
    {
        ConvertUsing<DateTime, string>(d => d.ToString("O"));
    }
}
```

The conversion is an expression; keep it EF-translatable if the mapping is used in queries. Nullable sources (`DateTime?`) get a null guard automatically.

## Using the mapper

```csharp
OrderDto dto = mapper.Map<OrderDto>(order);           // new instance; null in → null out
List<OrderDto> dtos = mapper.MapList<OrderDto>(orders); // null sequence → empty list

mapper.Map(request, existingOrder);                   // update pattern (PUT): assigns onto
                                                      // the existing instance and returns it
```

The source type is resolved at runtime, so one call site handles derived types and EF lazy-loading proxies. The update overload replaces nested objects and collections — it does not merge them.

### Querying with EF Core

Project inside the query — only the DTO's columns are selected:

```csharp
var dtos = await dbContext.Orders
    .Where(o => o.IsActive)
    .ProjectTo<OrderDto>(mapper)   // Synax.Mapper.Core.Extensions
    .ToListAsync(ct);
```

Or grab the expression yourself:

```csharp
var expr = mapper.GetExpression<Order, OrderDto>();
var dtos = await dbContext.Orders.Select(expr).ToListAsync(ct);
```

Both produce the same SQL as a hand-written `Select(s => new OrderDto { ... })`.

> Using Synax.EntityFrameworkCore repositories? Add `services.AddSynaxRepositoryMapper()` (from Synax.EntityFrameworkCore.Mapper) next to `AddSynaxMapper(...)` and use `repo.QueryDto<TDto>(...)` — no manual `ProjectTo` needed.

### Per-call values (map parameters)

A profile is built once at startup, so a value that changes per request — the caller's language, say — cannot be captured in it. Declare it as a parameter and read `.Value` inside the expression:

```csharp
public class ServiceProfile : MapperProfile
{
    public ServiceProfile()
    {
        var languageId = Parameter<Guid?>("languageId");

        CreateMap<Service, ServiceDto>()
            .For(d => d.Name, s => s.Translations
                .OrderBy(t => t.LanguageId == languageId.Value ? 0 : 1)
                .Select(t => t.Name)
                .FirstOrDefault() ?? "");
    }
}
```

Supply the value on each projection:

```csharp
query.ProjectTo<ServiceDto>(mapper, new MapParameters { ["languageId"] = languageId });
mapper.GetExpression<Service, ServiceDto>(new MapParameters { ["languageId"] = languageId });
repo.QueryDto<ServiceDto>(parameters: new MapParameters { ["languageId"] = languageId });
```

- The value is bound as a field read on a fresh closure, so EF Core sends it as a SQL parameter and **one cached plan serves every value**.
- A map that inlines another (a nested object or a collection) inherits its parameters; `TypeMap.ParameterNames` lists them all.
- **Fails loudly**: a parameter the map reads but was not supplied, a name it does not read, a value of the wrong type, and `null` for a value type all throw when the projection is built. Projecting a parameterized map without parameters throws too.
- In-memory `Map`/`MapList` cannot supply a value, so mapping through a map that reads one throws.
- `AssertConfigurationIsValid()` treats a member bound through a parameter like any other.

## Validating the configuration

Unmapped destination members are silently left at their default value. Catch them at startup or in one unit test:

```csharp
var config = serviceProvider.GetRequiredService<MapperConfiguration>();
config.AssertConfigurationIsValid();
// throws MappingValidationException listing every map and member nothing binds to
```

Members excluded with `.Ignore(...)` count as intentional and pass.

## Rules and limits

- **One map per source → destination pair.** Duplicates (also across profiles) fail at startup.
- **Maps must be acyclic.** Nested maps are inlined, so `A → B → A` cannot compile; configuration fails with the cycle path. Break it with `.Ignore(...)` on the member that closes the cycle.
- Mapping a pair with no registered map throws `MappingNotFoundException` — all maps are declared up front, nothing is created on the fly.
- Configuration is built once and immutable; `IMapper` is thread-safe to share.
