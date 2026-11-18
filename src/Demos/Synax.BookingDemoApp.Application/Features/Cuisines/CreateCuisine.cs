using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Cuisines;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Cuisines;

public class CreateCuisineRequest : ICommand<Result<CuisineResponse, HttpError>>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateCuisineHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper)
    : ICommandHandler<CreateCuisineRequest, Result<CuisineResponse, HttpError>>,
      IWithValidation<CreateCuisineRequest, Result<HttpError>>
{
    private readonly IRepository<Cuisine, BookingContext> _cuisineRepo = unitOfWork.GetRepository<Cuisine>();

    /// <summary>
    /// Pure short-circuit: each check owns its response — the field rule returns its 400 alone,
    /// the duplicate check its 409 — evaluated in order, first failure wins.
    /// </summary>
    public async Task<Result<HttpError>> ValidateAsync(
        CreateCuisineRequest request, CancellationToken cancellationToken)
    {
        var v = new Validation("The cuisine request is invalid.")
            .Require(!string.IsNullOrWhiteSpace(request.Name),
                ErrorCodes.Cuisines.InvalidName, "Must not be empty.", nameof(request.Name));
        if (v.HasErrors) return v.ToResult();

        // The ownership query filter scopes this check to the rows visible to the caller:
        // the global cuisines plus their own tenant's.
        if (await _cuisineRepo.Query(q => q.Where(c => c.Name == request.Name)).AnyAsync(cancellationToken))
        {
            return HttpError.Conflict(
                $"A cuisine named '{request.Name}' already exists.",
                errorCode: ErrorCodes.Cuisines.DuplicateName);
        }

        return Result.Ok();
    }

    public async Task<Result<CuisineResponse, HttpError>> HandleAsync(
        CreateCuisineRequest request, CancellationToken cancellationToken)
    {
        // OwnerId is never set here — the ownership interceptor stamps the caller's tenant on
        // save, which makes the new cuisine tenant-private (only global rows have a null owner).
        var entity = mapper.Map<Cuisine>(request);

        await _cuisineRepo.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return mapper.Map<CuisineResponse>(entity);
    }
}
