using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Restaurants;

public class CreateRestaurantRequest : ICommand<Result<RestaurantResponse, HttpError>>
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid CuisineId { get; set; }
    public int MaxCapacity { get; set; }
}

public class CreateRestaurantHandler(IUnitOfWork<BookingContext> unitOfWork, IMapper mapper)
    : ICommandHandler<CreateRestaurantRequest, Result<RestaurantResponse, HttpError>>,
      IWithValidation<CreateRestaurantRequest, Result<HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();
    private readonly IRepository<Cuisine, BookingContext> _cuisineRepo = unitOfWork.GetRepository<Cuisine>();

    /// <summary>
    /// Full aggregation: every rule runs — including the async cuisine lookup, awaited up front
    /// and fed into <see cref="Validation.Require"/> like any other bool — so a single 400
    /// reports all failures at once, each tied to its request property.
    /// </summary>
    public async Task<Result<HttpError>> ValidateAsync(
        CreateRestaurantRequest request, CancellationToken cancellationToken)
    {
        var cuisineExists = await _cuisineRepo
            .Query(q => q.Where(c => c.Id == request.CuisineId))
            .AnyAsync(cancellationToken);

        return new Validation("The restaurant request is invalid.")
            .Require(!string.IsNullOrWhiteSpace(request.Name),
                ErrorCodes.Restaurants.InvalidName, "Name is required.", nameof(request.Name))
            .Require(!string.IsNullOrWhiteSpace(request.Address),
                ErrorCodes.Restaurants.InvalidAddress, "Address is required.", nameof(request.Address))
            .Require(request.MaxCapacity > 0,
                ErrorCodes.Restaurants.InvalidCapacity, "Must be greater than zero.", nameof(request.MaxCapacity))
            .Require(cuisineExists,
                ErrorCodes.Restaurants.UnknownCuisine, "The specified cuisine does not exist.", nameof(request.CuisineId))
            .ToResult();
    }

    public async Task<Result<RestaurantResponse, HttpError>> HandleAsync(
        CreateRestaurantRequest request, CancellationToken cancellationToken)
    {
        return await Result.TryAsync(
                async () =>
                {
                    var entity = mapper.Map<Restaurant>(request);
                    await _restaurantRepo.AddAsync(entity, cancellationToken);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    return entity;
                },
                ex => HttpError.Internal("Failed to create restaurant.", errorCode: ErrorCodes.Restaurants.CreateFailed))
            .MapAsync(entity => mapper.Map<RestaurantResponse>(entity));
    }
}
