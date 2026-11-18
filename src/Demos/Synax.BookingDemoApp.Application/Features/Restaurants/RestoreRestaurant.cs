using Microsoft.EntityFrameworkCore;
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

public class RestoreRestaurantRequest : ICommand<Result<RestaurantResponse, HttpError>>
{
    public Guid Id { get; set; }
}

public class RestoreRestaurantHandler(IUnitOfWork<BookingContext> unitOfWork, IMapper mapper)
    : ICommandHandler<RestoreRestaurantRequest, Result<RestaurantResponse, HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    public async Task<Result<RestaurantResponse, HttpError>> HandleAsync(
        RestoreRestaurantRequest request, CancellationToken cancellationToken)
    {
        // Selectively ignore ONLY the soft-delete filter (named filter) so deleted rows are
        // visible — the ownership filter stays active, so tenants can only restore their own.
        var restaurant = await _restaurantRepo
            .Query(
                q => q.IgnoreQueryFilters(["SynaxSoftDelete"]).Where(r => r.Id == request.Id),
                asNoTracking: false)
            .FirstOrDefaultAsync(cancellationToken);

        if (restaurant is null)
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.", errorCode: ErrorCodes.Restaurants.NotFound);
        }

        if (!restaurant.IsDeleted)
        {
            return HttpError.Conflict(
                "The restaurant is not deleted.", errorCode: ErrorCodes.Restaurants.NotDeleted);
        }

        return await Result.TryAsync(
                async () =>
                {
                    restaurant.IsDeleted = false;
                    restaurant.DeletedAt = null;
                    restaurant.DeletedById = null;
                    _restaurantRepo.Update(restaurant);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    return restaurant;
                },
                ex => HttpError.Internal("Failed to restore the restaurant.", errorCode: ErrorCodes.Restaurants.RestoreFailed))
            .MapAsync(restored => mapper.Map<RestaurantResponse>(restored));
    }
}
