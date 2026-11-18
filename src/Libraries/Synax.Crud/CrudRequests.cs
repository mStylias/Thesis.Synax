using System.Text.Json.Serialization;
using Synax.AspNetCore.Errors;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.Crud;

/// <summary>
/// Marker implemented by every generated CRUD request — target it in
/// <c>AddGlobalBehavior(..., appliesTo: ...)</c> predicates to scope behaviors to CRUD traffic.
/// </summary>
public interface ICrudRequest;

/// <summary>
/// Marker implemented by the write requests (create/patch/delete) — the intended target for
/// transaction behaviors: <c>AddGlobalBehavior(typeof(MyTransactionBehavior&lt;,&gt;),
/// appliesTo: t =&gt; typeof(ICrudWriteRequest).IsAssignableFrom(t))</c>.
/// </summary>
public interface ICrudWriteRequest : ICrudRequest;

/// <summary>
/// Fetch-by-id: derive with an empty one-liner class
/// (<c>public sealed class GetCuisineRequest : CrudGetRequest&lt;Cuisine, Guid, CuisineResponse&gt;;</c>)
/// and register via <c>AddSynaxCrud</c> — the generic handler projects the entity to
/// <typeparamref name="TResponse"/> in SQL (<c>QueryDto</c>) and returns 404 when the key
/// (matched by the entity's <c>Id</c> property) finds nothing.
/// </summary>
public abstract class CrudGetRequest<TEntity, TKey, TResponse> : IQuery<Result<TResponse, HttpError>>, ICrudRequest
    where TEntity : class, IEntity
    where TKey : notnull
    where TResponse : class
{
    public TKey Id { get; set; } = default!;
}

/// <summary>
/// Offset-paged list ordered by the entity key: derive with an empty one-liner class. The
/// handler projects to <typeparamref name="TResponse"/> in SQL and returns a
/// <see cref="PagedResult{T}"/>. For client-driven filtering/ordering use an OData
/// <c>/search</c> endpoint instead.
/// </summary>
public abstract class CrudListRequest<TEntity, TResponse> : IQuery<Result<PagedResult<TResponse>, HttpError>>, ICrudRequest
    where TEntity : class, IEntity
    where TResponse : class
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Create: the derived class declares the body properties, which are copied onto a new
/// entity <b>matched by name</b> (no mapper profile; a member without a compatible entity
/// property fails at startup). A default <c>Guid</c> key gets a v7 id; ownership and audit
/// members are stamped by the interceptors on save, exactly like a handwritten handler.
/// </summary>
public abstract class CrudCreateRequest<TEntity, TResponse> : ICommand<Result<TResponse, HttpError>>, ICrudWriteRequest
    where TEntity : class, IEntity
    where TResponse : class;

/// <summary>
/// Merge-patch: the derived class declares <c>PatchValue&lt;T&gt;</c> properties; only members
/// present in the request body are written, via a single set-based
/// <c>ExecutePatchAsync</c> (which also stamps modification audit members). The route-supplied
/// <see cref="Id"/> is body-invisible and never patched.
/// </summary>
public abstract class CrudPatchRequest<TEntity, TKey, TResponse> : ICommand<Result<TResponse, HttpError>>, ICrudWriteRequest
    where TEntity : class, IEntity
    where TKey : notnull
    where TResponse : class
{
    /// <summary>The target's key — stamped from the route by the endpoint, not part of the JSON body.</summary>
    [JsonIgnore]
    public TKey Id { get; set; } = default!;
}

/// <summary>
/// Delete-by-id: derive with an empty one-liner class. Uses <c>ExecuteDeleteAsync</c>, so
/// soft-delete entities are converted to soft deletes with their stamps; 404 when the key
/// matches nothing (ownership filters apply, so foreign tenants get 404, not 403).
/// </summary>
public abstract class CrudDeleteRequest<TEntity, TKey> : ICommand<Result<HttpError>>, ICrudWriteRequest
    where TEntity : class, IEntity
    where TKey : notnull
{
    public TKey Id { get; set; } = default!;
}
