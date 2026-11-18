# Synax.EntityFrameworkCore.OData

Client-driven querying (`$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select`, `$expand`, plus opt-in `$search`, `$compute`, `$apply` and server-driven paging) over `ISynaxQuery<T>`, and the `$metadata`/service documents that let a generic OData client discover it — using `Microsoft.AspNetCore.OData` purely as a query library: no OData routing, no controllers, no `[EnableQuery]`. **Standalone**: this package depends only on `Synax.EntityFrameworkCore` and the OData package; it has no dependency on `Synax.Results` or `Synax.AspNetCore`. The optional `Synax.EntityFrameworkCore.OData.Results` add-on bridges it to the `Result<_, HttpError>` flow.

## Quick start — just OData

A minimal-API endpoint needs nothing but a repository query and the incoming request:

```csharp
app.MapGet("/tables/search", (IUnitOfWork<BookingDbContext> uow, HttpRequest request, CancellationToken ct) =>
    uow.GetRepository<Table>()
       .QueryDto<TableResponse>()            // base shape + filters stay YOUR decision
       .ToODataHttpResultAsync(request, cancellationToken: ct));
```

```
GET /tables/search?$filter=seats ge 4 and contains(location,'door')&$orderby=number&$top=20&$count=true
```

returns the OData JSON convention:

```json
{ "value": [ { "id": "...", "number": 2, "seats": 6, ... } ], "@odata.count": 7 }
```

- `ToODataHttpResultAsync` → `Results<Ok<ODataPage>, ProblemHttpResult>`: `200` with the page, or `400` Problem Details (title `INVALID_ODATA_QUERY`) for malformed/disallowed client queries.
- `ToODataPageAsync` is the HTTP-shape-agnostic core: returns `ODataPage`, throws `SynaxODataQueryException` for client errors — catch it or map it in your exception handler.
- Whatever the query already carries — ownership scoping, soft-delete filters, DTO projection (`QueryDto`/`QuerySelect`), base `Where`s — applies **before** the client's options. Clients can only query the shape you expose.

## Options apply to the exposed shape

The EDM model is built (and cached) from the query's element type with camelCased names, so clients query the same property names your JSON serializes. Property matching is case-insensitive. Prefer applying OData over a projected DTO query, not entities — then internals like `OwnerId` don't exist in the client-visible shape at all.

## Configuration

```csharp
services.AddSynaxOData(options =>
{
    options.MaxTop = 100;                              // default 100
    options.AllowedQueryOptions = AllowedQueryOptions.Filter | AllowedQueryOptions.OrderBy
        | AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.Count;
    options.MaxExpansionDepth = 2;                     // default 2
    options.MaxNodeCount = 100;                        // $filter AST size cap, default 100
    options.AllowedOrderByProperties.Add("createdAt"); // empty = all (names in camelCase)
    options.ConfigureModel = builder =>                // EDM escape hatch, e.g. keyless DTOs
        builder.EntityType<LegacyDto>().HasKey(d => d.Code);
});
```

Registration is optional — every helper falls back to these defaults — and any call can pass a `SynaxODataOptions` explicitly to override the registered one. Violations (too-large `$top`, disallowed option or orderby property, oversized filter) are client errors: `400`/`SynaxODataQueryException`, never a server error.

## Opt-in capabilities

`$filter/$orderby/$top/$skip/$count/$select/$expand` work out of the box. Five more are available, each **off by default and behind exactly one switch** — a client sending a query option nothing switched on gets a `400` naming it. Turning one on adds its flag to `AllowedQueryOptions`; you never maintain both.

```csharp
services.AddSynaxOData(options =>
{
    options.EnableCompute = true;                       // $compute
    options.AllowedComputeProperties.Add("maxCapacity");// optional: narrow what it may read

    options.PageSize = 25;                              // server-driven paging (@odata.nextLink)
    options.ServiceRoot = new Uri("https://api.example.com/"); // optional: absolute next links

    options.Searchable<RestaurantResponse>(             // $search — declaring the members IS the switch
        r => r.Name,
        r => r.Address);

    options.EnableApply = true;                         // $apply (aggregation)
    options.AllowedAggregations.Add("sum");             // optional: narrow the methods
    options.MaxAggregationDepth = 3;                    // optional: chain length, default 4

    options.Expose<RestaurantResponse>("restaurants");  // publish through $metadata
    options.EnableAnnotations = true;                   // @odata.context / @odata.id / @odata.type
});
```

Prefer a **DI-registered** (or otherwise long-lived) options instance: the EDM model and, for `$search`, an OData sub-container are cached on it per element type.

### `$compute`

`$compute=price mul quantity as total` defines a named value over the exposed shape that `$filter`, `$orderby` and `$select` can then use.

```
GET /restaurants/search?$compute=maxCapacity div 4 as tableCount&$filter=tableCount ge 10&$select=name,tableCount
```

- `$compute` **alone changes nothing about the response** — the computed value is invisible unless the client also `$select`s it. With `$select` the rows become the usual `IDictionary<string, object?>`, computed members included.
- Arithmetic, `concat` and date-part computes translate on Npgsql; the filter and the ordering land in the SQL statement.
- Guardrails: expression size is capped by `MaxNodeCount` (the OData validators cover `$filter` but leave `$compute` unbounded, so Synax walks it), and `AllowedComputeProperties` — when non-empty — restricts which properties an expression may read (camelCase EDM names, like `AllowedOrderByProperties`).
- A computed value cannot be a server-driven paging cursor; see below.

### `$search`

`Searchable<T>(...)` declares the string members `$search` looks in — and declaring them is what enables `$search` for that type. A `$search` against a type that declared none is a `400` naming the type and the API, **never** a silent match-everything (which is what the OData pipeline does when no binder is registered).

```
GET /restaurants/search?$search=harbour OR "sea view"
```

- A term matches when any declared member case-insensitively contains it; multiple terms combine with the client's `AND`/`OR`/`NOT` (space-separated is an implicit `AND`), and a quoted phrase is one term. `%` and `_` match literally.
- The OData search lexer will not start a bare term with a digit or punctuation — `$search="555-0100"` works, `$search=555-0100` is a parse error (`400`). Quote anything that isn't a word.
- Members are property/field access chains ending in a string — `x => x.Name`, `x => x.Category.Name`. Declaring the same member twice is idempotent, so two modules can declare a shared type independently.
- Translates to `lower(col) LIKE '%term%'` — correct, but a scan: it cannot use a btree index. Reach for `pg_trgm` or a `tsvector` column when a real workload needs one. The emitted shape behaves identically in LINQ-to-Objects, so the `Synax.EntityFrameworkCore.Testing` fakes stay honest.
- `$count` reports the searched total, not the unsearched one.

### Server-driven paging: `@odata.nextLink` + `$skiptoken`

Setting `PageSize` caps every response and adds an `@odata.nextLink` while more rows follow; the client just follows the link.

```json
{
  "value": [ … 25 rows … ],
  "@odata.count": 812,
  "@odata.nextLink": "/api/v1/restaurants/search?$orderby=name&$skiptoken=name-'Bistro',id-3f2c…"
}
```

- The cursor is **keyset-shaped** — it encodes the last row's sort key — so pages neither duplicate nor skip rows when the table is written to between requests, and deep pages stay fast. It is written in `DefaultSkipTokenHandler`'s own wire format, so generic OData clients round-trip it.
- Requires a deterministic ordering, which Synax derives by appending the EDM key to the client's `$orderby`. An `$orderby` over a **nested** (`category/name`) or **computed** property cannot be encoded in a cursor and is a `400` saying so — order by a top-level property, or leave paging client-driven.
- Interactions: `$skiptoken` applies before `$skip`; a `$top` at or below `PageSize` wins and suppresses the link, a larger one is reduced per page so following the links never exceeds it; `$count` still reports the full filtered total, not the page. A malformed or foreign `$skiptoken` is a `400`.
- With `$select`/`$expand`, the reshaping is applied to the materialized page rather than pushed into the database query — a reshaped row need not carry the sort keys the cursor is read from. The cost is a full-column read for a selecting client.
- Synax's own cursor pagination (`ISynaxQuery.ToKeysetPageAsync`) is unchanged and remains the option when you want `KeysetPage<T, TKey>` rather than the OData envelope.

### `$apply`

`EnableApply` lets clients aggregate over the exposed shape. The chain is a `/`-separated sequence of `filter`, `compute`, `groupby` and `aggregate` transformations, and the database answers it with a `GROUP BY`.

```
GET /restaurants/search?$apply=groupby((cuisineId),aggregate(maxCapacity with sum as seats,$count as venues))
```

```json
{ "value": [ { "cuisineId": "8f2c…", "seats": 320, "venues": 7 } ] }
```

- Aggregation methods: `sum`, `min`, `max`, `average`, `countdistinct`, `$count`. `AllowedAggregations` — when non-empty — restricts them; `MaxNodeCount` bounds the expressions and `MaxAggregationDepth` (default 4) the chain length.
- `groupby` may key on a nested property (`groupby((category/name))`), which comes back as a nested dictionary. It may **not** key on a navigation itself (`groupby((category))`) — that is a `400` from the parser.
- Rows produced by a `groupby`, `aggregate` or `compute` are no longer the queried type: they are `IDictionary<string, object?>` rows holding exactly the members the transformations named. `$filter` and `$orderby` bind against *those* names (`$filter=seats gt 100` works, `$filter=name eq 'x'` does not unless `name` was grouped) — and when the binder reports a missing property, Synax appends the list of members the rows actually have.
- **Rejected alongside a reshaping `$apply`** (`400`, not a silent no-op, which is what the OData pipeline does): `$select`, `$expand`, `$search`, `$skiptoken`, the `$compute` query option (use the `compute(...)` transformation instead), and `$orderby` while `AllowedOrderByProperties` is non-empty — that restriction cannot be checked against members the transformations invented. A `$apply` that only *filters* reshapes nothing, so it composes with all of them freely.
- `$count=true` reports the number of **groups**, per the OData specification — not the number of source rows.
- With server-driven paging on, an aggregation's `@odata.nextLink` carries a `$skip` offset rather than a `$skiptoken`: the rows have no key to encode. Offset paging shifts if the underlying rows change between pages, which is the usual trade and unavoidable here.
- The `expand(...)` transformation is rejected — it only filters expanded entities, which this bridge does not serialize as part of an aggregation, so honouring it would be a no-op. Use `$expand` (with a nested `$filter`) on a non-aggregating query, or group over the related property.
- Same caveat as `$filter`: a mapper enum→string projection cannot be grouped in SQL any more than it can be filtered. Group enums entity-rooted.

### `@odata.*` annotations

`EnableAnnotations` adds the OData control information: `@odata.context` on the envelope, `@odata.id` and `@odata.type` on each row. It needs the queried type to be `Expose`d and the service root to be known — a context URL pointing at a `$metadata` nobody serves is worse than none, so a missing declaration fails with the lever named rather than emitting a dead link.

```json
{
  "@odata.context": "https://api.example.com/odata/$metadata#restaurants(name,maxCapacity)",
  "value": [ { "name": "Harbour Grill", "maxCapacity": 80,
               "@odata.id": "https://api.example.com/odata/restaurants(8f2c…)",
               "@odata.type": "#MyApp.Contracts.RestaurantResponse" } ]
}
```

- **Rows become dictionaries.** An annotation is an extra member, and a member can only be added to a dictionary — so with this on, a plain typed row comes back in the same camel-cased dictionary form `$select` already produces. That is the one visible cost of turning it on, which is why it is off by default.
- A `$select` that drops the key leaves that row with no `@odata.id`: the key was projected away in SQL, and one the server never read would be a guess.
- Rows a `$apply` reshaped get the envelope's context and nothing else — an aggregation result is not an entity, so it has neither an id nor a type.
- `@odata.count` and `@odata.nextLink` are unaffected; they answer `$count` and paging, not this switch.

## Rewriting the built expression

An `IODataExpressionRewriter` registered in DI rewrites the expression the query options built, after `ApplyTo` and before the database sees it — to make `contains` case- and accent-insensitive through a database function, say:

```csharp
services.AddSingleton<IODataExpressionRewriter, FoldTextComparisons>();
```

- It applies to every OData query whose request carries the registration, **whatever `SynaxODataOptions` instance the endpoint passed**.
- It sees only what the query options added. The endpoint's own source query is swapped for an opaque `ParameterExpression` while the rewriters run, so LINQ written in a handler is never rewritten.
- The `$count` query is rewritten too. The in-memory `$select` of a server-driven page is not: it runs over materialized rows.

## `$metadata` and the service document

```csharp
services.AddSynaxOData(options =>
{
    options.Expose<RestaurantResponse>("restaurants");
    options.Expose<TableResponse>("tables");
});

app.MapSynaxODataMetadata();          // GET /odata and GET /odata/$metadata
```

The call takes the OData **service root** rather than the `$metadata` path, because the root is what every other URL in the protocol is relative to — including the `@odata.context` above, which is why mapping the endpoints is enough to make annotations work without repeating the path. It is app-level and called once, like `MapSynaxEndpoints`, and therefore lives outside a Synax module.

- `GET {prefix}/$metadata` serves CSDL **XML** by default and **JSON** on `Accept: application/json` or `$format=json`. Both are rendered once at first request and cached.
- `GET {prefix}` serves the JSON service document listing the entity sets.
- The set name is given, never derived: it is a published contract, and renaming a DTO must not rename a client's entity set. One type gets one name, one name belongs to one type, and repeating the same pair is a no-op so two modules may declare a shared type.
- **Exposing a type does not change how any query behaves.** Queries are still parsed and validated against the per-type model they always were; the composed multi-set model exists to be published. The trade is that `$expand` cannot cross between two separately-exposed DTOs.

### `$format`

`$format=json` is accepted (it asks for what these endpoints already return) and anything else is a `400` naming the format, rather than being silently ignored and answered with JSON.

### Not implemented

`$batch`, bound/unbound functions and actions, singletons, `$ref` and `$deltatoken` need OData routing/protocol machinery this package deliberately does not take on.

## `$select` / `$expand`

With `$select`/`$expand` the items in `ODataPage.Value` are `IDictionary<string, object?>` rows containing only the selected/expanded members (camelCase keys, nested dictionaries/lists for expansions) — they serialize naturally with System.Text.Json. Prerequisites and limits:

- `$expand` requires the element type to be an EDM **entity type with a key** (an `Id` property by convention, or declare one via `ConfigureModel`) and the expanded member to be another entity type — plain nested DTO classes with `Id`s satisfy this automatically.
- `$expand` works over mapper-projected queries (`QueryDto`) when the DTO exposes the nested collection (e.g. `RestaurantDetailResponse.Tables`), and over entity-rooted queries.
- **Enum→string projected members are not filterable in SQL**: a mapper binding like `Location = entity.Location.ToString()` cannot be translated by EF inside a `WHERE`. Filter enums over entity-rooted queries (the EDM models the enum; `$filter=location eq 'Indoor'` translates through `HasConversion<string>`), or project a natively-string member.

### Options nested inside `$expand`

A client may shape each expanded collection independently, and the database does the work:

```
GET /restaurants/search?$select=name&$expand=tables($filter=seats ge 4;$orderby=number desc;$top=5;$count=true)
```

```json
{ "value": [ { "name": "Harbour Grill", "tables": [ { "number": 9, "seats": 6 } ], "tables@odata.count": 12 } ] }
```

- Supported inside the parentheses: `$filter`, `$orderby`, `$top`, `$skip`, `$select`, `$count`, `$compute`, `$search`, a further `$expand`, and `$levels`.
- **They answer to the same settings as their top-level namesakes.** `AllowedQueryOptions`, `AllowedOrderByProperties`, `MaxTop`, `MaxNodeCount`, `MaxExpansionDepth` and the `EnableCompute` / `Searchable<T>` switches all apply inside an `$expand` — otherwise `$expand` would be a way around every one of them. Anything not permitted is a `400` naming the option.
- **`$count=true` comes back as `member@odata.count`**, the OData wire name, next to the member itself. It is the total the nested `$filter` left, *before* a nested `$top`/`$skip` windowed it.
- A `$filter` under a **single-valued** navigation empties the member when it does not match (`"cuisine": null`), rather than being ignored.
- A nested `$search` needs `Searchable<T>` declared for the *expanded* type; without it, it is the same `400` it is at the top level, never a silent match-everything.
- A nested `$apply` is a `400`: the expand binder materializes the expanded rows as-is, so an aggregation there would be quietly dropped. Aggregate the collection itself with a top-level `$apply`.

### `$levels`

`$expand=parent($levels=3)` walks a **self-referencing** navigation to a fixed depth; `$levels=max` walks as deep as `MaxExpansionDepth` allows, which is what "max" means in OData — a service-defined limit. Rejected with a `400`: a `$levels` on a navigation to a different type (OData does not allow it), a depth past `MaxExpansionDepth`, and `$levels=max` while `MaxExpansionDepth` is `0` (OData's "no limit"), which would leave the expansion nothing to stop it.

## Advanced

`SynaxQueryODataExtensions.CreateQueryOptions<T>(request, settings)` builds a ready-to-use `ODataQueryOptions<T>` (cached EDM model, all context capabilities enabled, parse errors surfaced as `SynaxODataQueryException`) if you need to inspect the options or apply them yourself via `ToODataPageAsync(query, options, ...)`.

An options instance carrying a `$apply` is **single-use**: applying it rewrites its query context's element type to the wrapper the transformations produce, so a second `ApplyTo` binds the client's grouping against that wrapper and throws. Build a fresh one per application (which is what `ToODataPageAsync(request, ...)` does, including for its own `$count` query).

## With Synax.Results (optional add-on)

Reference `Synax.EntityFrameworkCore.OData.Results` to use the mediator/endpoint-edge pattern: the handler returns the query *deferred* and the endpoint applies the client's options.

```csharp
// handler
public class SearchRestaurantsRequest : IQuery<Result<ISynaxQuery<RestaurantResponse>, HttpError>>;
// → return Result.Ok<ISynaxQuery<RestaurantResponse>, HttpError>(repo.QueryDto<RestaurantResponse>());

// endpoint
var result = await sender.SendAsync(new SearchRestaurantsRequest(), ct);
return await result.ToODataHttpResultAsync(request, cancellationToken: ct);
```

A failed `Result` returns its `HttpError` as Problem Details unchanged; invalid client queries become `HttpError.BadRequest` with code `INVALID_ODATA_QUERY`. Deferred queries materialize at the endpoint inside the same request scope, so the `DbContext` is still alive.

## Testing

Everything works over in-memory queryables: `SynaxQuery.From(list).ToODataPageAsync(request, ...)` evaluates `$filter/$orderby/$top/$skip/$count` (and `$select`/`$expand`, `$search`, `$compute`, `$apply`, and a full walk of `@odata.nextLink` pages) with LINQ-to-Objects — no database. Build the request with a `DefaultHttpContext` and a query string; to follow a next link, feed its query string back into the next request. Cover SQL translation of `$select`/`$expand` (including the nested options and the `member@odata.count` total), the `lower(...)` search comparison, computed arithmetic, `GROUP BY` and enum filters with integration tests.
