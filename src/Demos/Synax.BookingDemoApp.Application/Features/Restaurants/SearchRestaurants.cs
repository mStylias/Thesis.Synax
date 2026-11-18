using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Restaurants;

public class SearchRestaurantsRequest : IQuery<Result<ISynaxQuery<RestaurantResponse>, HttpError>>;

public class SearchRestaurantsHandler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<SearchRestaurantsRequest, Result<ISynaxQuery<RestaurantResponse>, HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    public Task<Result<ISynaxQuery<RestaurantResponse>, HttpError>> HandleAsync(
        SearchRestaurantsRequest request, CancellationToken cancellationToken)
    {
        // OData search: the handler stays in charge of the exposed shape (QueryDto projects to the
        // response DTO in SQL, ownership/soft-delete filters apply as always) and returns the query
        // deferred; the endpoint applies the client's $filter/$orderby/... and materializes.
        return Task.FromResult(
            Result.Ok<ISynaxQuery<RestaurantResponse>, HttpError>(
                _restaurantRepo.QueryDto<RestaurantResponse>()));
    }
}
