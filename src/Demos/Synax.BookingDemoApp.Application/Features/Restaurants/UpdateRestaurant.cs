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

public class UpdateRestaurantRequest : ICommand<Result<RestaurantResponse, HttpError>>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid CuisineId { get; set; }
    public int MaxCapacity { get; set; }
}

public class UpdateRestaurantHandler(IUnitOfWork<BookingContext> unitOfWork, IMapper mapper)
    : ICommandHandler<UpdateRestaurantRequest, Result<RestaurantResponse, HttpError>>,
      IWithValidation<UpdateRestaurantRequest, Result<Restaurant, HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    /// <summary>
    /// Data-carrying validation: the TRACKED entity is fetched and checked once here, then read
    /// back in <see cref="HandleAsync"/> via <c>this.ValidationData()</c> — typed and non-null.
    /// </summary>
    public async Task<Result<Restaurant, HttpError>> ValidateAsync(
        UpdateRestaurantRequest request, CancellationToken cancellationToken)
    {
        var restaurant = await _restaurantRepo
            .Query(q => q.Where(r => r.Id == request.Id), asNoTracking: false)
            .FirstOrDefaultAsync(cancellationToken);

        if (restaurant is null)
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.", errorCode: ErrorCodes.Restaurants.NotFound);
        }

        return restaurant;
    }

    public async Task<Result<RestaurantResponse, HttpError>> HandleAsync(
        UpdateRestaurantRequest request, CancellationToken cancellationToken)
    {
        var entity = this.ValidationData();   // tracked entity, fetched once in ValidateAsync

        return await Result.TryAsync(
                async () =>
                {
                    // The update overload of Map: copies the request's editable fields onto the
                    // existing tracked instance (Id and interceptor-stamped members are Ignored
                    // in the profile) — the PUT pattern, no manual property assignments.
                    mapper.Map(request, entity);
                    _restaurantRepo.Update(entity);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    return entity;
                },
                ex => HttpError.Internal("Failed to update the restaurant.", errorCode: ErrorCodes.Restaurants.UpdateFailed))
            .MapAsync(updated => mapper.Map<RestaurantResponse>(updated));
    }
}
