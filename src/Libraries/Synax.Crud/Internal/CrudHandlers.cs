using Synax.AspNetCore.Errors;
using Synax.Crud.Internal;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.Crud.Internal;

// The generic handlers behind the Crud*Request bases. Closed per app-declared request type by
// the AddSynaxCrud scan and registered through MediatorOptions.AddHandler, so they run inside
// the full mediator pipeline (telemetry, result logging, scoped global behaviors). They contain
// deliberately zero extension points: an operation that outgrows them gets a handwritten
// handler for the same request type, which the registration automatically prefers.

internal sealed class CrudGetHandler<TRequest, TEntity, TKey, TResponse, TContext>(
    IUnitOfWork<TContext> unitOfWork)
    : IQueryHandler<TRequest, Result<TResponse, HttpError>>
    where TRequest : CrudGetRequest<TEntity, TKey, TResponse>
    where TEntity : class, IEntity
    where TKey : notnull
    where TResponse : class
    where TContext : ISynaxContext
{
    private readonly IRepository<TEntity, TContext> _repo = unitOfWork.GetRepository<TEntity>();

    public async Task<Result<TResponse, HttpError>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        var response = await _repo
            .QueryDto<TResponse>(q => q.Where(CrudEntityKey<TEntity>.Predicate(request.Id)))
            .FirstOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            return HttpError.NotFound(
                $"The specified {CrudErrorCodes<TEntity>.DisplayName} was not found.",
                errorCode: CrudErrorCodes<TEntity>.NotFound);
        }

        return response;
    }
}

internal sealed class CrudListHandler<TRequest, TEntity, TResponse, TContext>(
    IUnitOfWork<TContext> unitOfWork)
    : IQueryHandler<TRequest, Result<PagedResult<TResponse>, HttpError>>
    where TRequest : CrudListRequest<TEntity, TResponse>
    where TEntity : class, IEntity
    where TResponse : class
    where TContext : ISynaxContext
{
    private readonly IRepository<TEntity, TContext> _repo = unitOfWork.GetRepository<TEntity>();

    public async Task<Result<PagedResult<TResponse>, HttpError>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        return await _repo
            .QueryDto<TResponse>(q => CrudEntityKey<TEntity>.OrderByKey(q))
            .ToPagedListAsync(request.Page, request.PageSize, cancellationToken);
    }
}

internal sealed class CrudCreateHandler<TRequest, TEntity, TResponse, TContext>(
    IUnitOfWork<TContext> unitOfWork)
    : ICommandHandler<TRequest, Result<TResponse, HttpError>>
    where TRequest : CrudCreateRequest<TEntity, TResponse>
    where TEntity : class, IEntity, new()
    where TResponse : class
    where TContext : ISynaxContext
{
    private readonly IRepository<TEntity, TContext> _repo = unitOfWork.GetRepository<TEntity>();

    public async Task<Result<TResponse, HttpError>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        var created = await Result.TryAsync(
            async () =>
            {
                // Name-matched copy + client-side Guid key generation; ownership/audit
                // members are stamped by the interceptors on save.
                var entity = CrudCreateBinder<TEntity, TRequest>.Create(request);
                await _repo.AddAsync(entity, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return CrudEntityKey<TEntity>.GetKey(entity)!;
            },
            ex => HttpError.Internal(
                $"Failed to create the {CrudErrorCodes<TEntity>.DisplayName}.",
                errorCode: CrudErrorCodes<TEntity>.CreateFailed));

        if (created.IsError)
            return created.Error!;

        // Re-fetch through the projection so computed response members come from the store.
        return await _repo
            .QueryDto<TResponse>(q => q.Where(CrudEntityKey<TEntity>.Predicate(created.Data!)))
            .FirstAsync(cancellationToken);
    }
}

internal sealed class CrudPatchHandler<TRequest, TEntity, TKey, TResponse, TContext>(
    IUnitOfWork<TContext> unitOfWork)
    : ICommandHandler<TRequest, Result<TResponse, HttpError>>
    where TRequest : CrudPatchRequest<TEntity, TKey, TResponse>
    where TEntity : class, IEntity
    where TKey : notnull
    where TResponse : class
    where TContext : ISynaxContext
{
    private readonly IRepository<TEntity, TContext> _repo = unitOfWork.GetRepository<TEntity>();

    public async Task<Result<TResponse, HttpError>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        // The request itself is the patch DTO: only its set PatchValue<T> members are
        // written (single set-based UPDATE, modification audit stamped by the executor);
        // the base's plain Id property is naturally not part of the patch surface.
        var patched = await Result.TryAsync(
            () => _repo.ExecutePatchAsync(
                q => q.Where(CrudEntityKey<TEntity>.Predicate(request.Id)), request, cancellationToken),
            ex => HttpError.Internal(
                $"Failed to update the {CrudErrorCodes<TEntity>.DisplayName}.",
                errorCode: CrudErrorCodes<TEntity>.UpdateFailed));

        if (patched.IsError)
            return patched.Error!;

        if (patched.Data == 0)
        {
            return HttpError.NotFound(
                $"The specified {CrudErrorCodes<TEntity>.DisplayName} was not found.",
                errorCode: CrudErrorCodes<TEntity>.NotFound);
        }

        return await _repo
            .QueryDto<TResponse>(q => q.Where(CrudEntityKey<TEntity>.Predicate(request.Id)))
            .FirstAsync(cancellationToken);
    }
}

internal sealed class CrudDeleteHandler<TRequest, TEntity, TKey, TContext>(
    IUnitOfWork<TContext> unitOfWork)
    : ICommandHandler<TRequest, Result<HttpError>>
    where TRequest : CrudDeleteRequest<TEntity, TKey>
    where TEntity : class, IEntity
    where TKey : notnull
    where TContext : ISynaxContext
{
    private readonly IRepository<TEntity, TContext> _repo = unitOfWork.GetRepository<TEntity>();

    public async Task<Result<HttpError>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        var affected = 0;
        var deleted = await Result.TryAsync(
            async () =>
            {
                // Set-based; soft-delete entities are converted with their stamps.
                affected = await _repo.ExecuteDeleteAsync(
                    q => q.Where(CrudEntityKey<TEntity>.Predicate(request.Id)), cancellationToken);
            },
            ex => HttpError.Internal(
                $"Failed to delete the {CrudErrorCodes<TEntity>.DisplayName}.",
                errorCode: CrudErrorCodes<TEntity>.DeleteFailed));

        if (deleted.IsError)
            return deleted;

        if (affected == 0)
        {
            return HttpError.NotFound(
                $"The specified {CrudErrorCodes<TEntity>.DisplayName} was not found.",
                errorCode: CrudErrorCodes<TEntity>.NotFound);
        }

        return deleted;
    }
}
