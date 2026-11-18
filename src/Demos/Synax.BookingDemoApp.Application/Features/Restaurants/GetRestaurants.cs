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

public class GetRestaurantsRequest : IQuery<Result<PagedResult<RestaurantResponse>, HttpError>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class GetRestaurantsHandler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<GetRestaurantsRequest, Result<PagedResult<RestaurantResponse>, HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    public async Task<Result<PagedResult<RestaurantResponse>, HttpError>> HandleAsync(
        GetRestaurantsRequest request, CancellationToken cancellationToken)
    {
        // Offset pagination: the mapper projects Restaurant → RestaurantResponse in SQL
        // (QueryDto), and ToPagedListAsync adds the count + page window.
        return await Result.TryAsync(
            () => _restaurantRepo
                .QueryDto<RestaurantResponse>(q => q.OrderBy(r => r.Name))
                .ToPagedListAsync(request.Page, request.PageSize, cancellationToken),
            ex => HttpError.Internal("Failed to fetch restaurants.", errorCode: ErrorCodes.Restaurants.FetchFailed));
    }
}
