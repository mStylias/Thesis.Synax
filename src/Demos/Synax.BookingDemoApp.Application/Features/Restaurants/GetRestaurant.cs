using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Restaurants;

public class GetRestaurantRequest : IQuery<Result<RestaurantDetailResponse, HttpError>>
{
    public Guid Id { get; set; }
}

public class GetRestaurantHandler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<GetRestaurantRequest, Result<RestaurantDetailResponse, HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    public async Task<Result<RestaurantDetailResponse, HttpError>> HandleAsync(
        GetRestaurantRequest request, CancellationToken cancellationToken)
    {
        // The nested DTO (CuisineName + Tables) is projected entirely in SQL by the mapper —
        // no Includes needed; EnsureNotNullAsync turns a missing row into a typed 404.
        return await Result.TryAsync(
                () => _restaurantRepo
                    .QueryDto<RestaurantDetailResponse>(q => q.Where(r => r.Id == request.Id))
                    .FirstOrDefaultAsync(cancellationToken),
                ex => HttpError.Internal("Failed to fetch the restaurant.", errorCode: ErrorCodes.Restaurants.FetchFailed))
            .EnsureNotNullAsync(() => HttpError.NotFound(
                "The specified restaurant was not found.", errorCode: ErrorCodes.Restaurants.NotFound));
    }
}
