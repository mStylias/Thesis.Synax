using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Abstractions.Patching;
using Synax.EntityFrameworkCore.AspNetCore;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Restaurants;

/// <summary>
/// Partial update (JSON merge-patch semantics): only the properties present in the request body
/// are applied. <see cref="PatchValue{T}"/> tracks presence at deserialization (absent = unset,
/// explicit <c>null</c> = clear), and <c>ExecutePatchAsync</c> writes the set members with a
/// single set-based <c>ExecuteUpdate</c> — matched to entity properties by name, no mapper
/// profile, with <c>UpdatedAt</c>/<c>ModifiedById</c> stamped by the executor.
/// </summary>
public class PatchRestaurantRequest : ICommand<Result<RestaurantResponse, HttpError>>
{
    public Guid Id { get; set; }
    public PatchValue<string> Name { get; set; }
    public PatchValue<string> Address { get; set; }
    public PatchValue<string> PhoneNumber { get; set; }
    public PatchValue<Guid> CuisineId { get; set; }
    public PatchValue<int> MaxCapacity { get; set; }
}

public class PatchRestaurantHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<PatchRestaurantRequest, Result<RestaurantResponse, HttpError>>,
      IWithValidation<PatchRestaurantRequest, Result<HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();
    private readonly IRepository<Cuisine, BookingContext> _cuisineRepo = unitOfWork.GetRepository<Cuisine>();

    public async Task<Result<HttpError>> ValidateAsync(
        PatchRestaurantRequest request, CancellationToken cancellationToken)
    {
        // Field-shape rules run only for provided members — an absent property is not an error,
        // which is what RequireIfSet expresses. The predicate never runs for an absent member, so
        // rules are written against the value itself and cannot trip over an unset default.
        var v = new Validation("The restaurant patch is invalid.")
            .RequireIfSet(request.Name, name => !string.IsNullOrWhiteSpace(name),
                ErrorCodes.Restaurants.InvalidName, "Name must not be empty.", nameof(request.Name))
            .RequireIfSet(request.Address, address => !string.IsNullOrWhiteSpace(address),
                ErrorCodes.Restaurants.InvalidAddress, "Address must not be empty.", nameof(request.Address))
            .RequireIfSet(request.MaxCapacity, capacity => capacity > 0,
                ErrorCodes.Restaurants.InvalidCapacity, "Must be greater than zero.", nameof(request.MaxCapacity));

        if (v.HasErrors)
            return v.ToResult();

        if (request.CuisineId.IsSet)
        {
            var cuisineExists = await _cuisineRepo
                .Query(q => q.Where(c => c.Id == request.CuisineId.Value))
                .AnyAsync(cancellationToken);

            v.Require(cuisineExists,
                ErrorCodes.Restaurants.UnknownCuisine, "The specified cuisine does not exist.",
                nameof(request.CuisineId));
        }

        return v.ToResult();
    }

    public async Task<Result<RestaurantResponse, HttpError>> HandleAsync(
        PatchRestaurantRequest request, CancellationToken cancellationToken)
    {
        // Set-based: one UPDATE for the provided members (audit stamps included), no entity
        // fetched or tracked. The ownership query filter scopes the WHERE like any query.
        var patched = await Result.TryAsync(
            () => _restaurantRepo.ExecutePatchAsync(
                q => q.Where(r => r.Id == request.Id), request, cancellationToken),
            ex => HttpError.Internal("Failed to patch the restaurant.", errorCode: ErrorCodes.Restaurants.UpdateFailed));

        if (patched.IsError)
            return patched.Error!;

        if (patched.Data == 0)
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.", errorCode: ErrorCodes.Restaurants.NotFound);
        }

        return await Result.TryAsync(
            () => _restaurantRepo
                .QueryDto<RestaurantResponse>(q => q.Where(r => r.Id == request.Id))
                .FirstAsync(cancellationToken),
            ex => HttpError.Internal("Failed to fetch the patched restaurant.", errorCode: ErrorCodes.Restaurants.FetchFailed));
    }
}
