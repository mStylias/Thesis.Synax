using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.RestaurantsV2;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.RestaurantsV2;

public class GetRestaurantsV2Request : IQuery<Result<KeysetPage<RestaurantV2Response, Guid>, HttpError>>
{
    public int PageSize { get; set; } = 20;

    /// <summary>The previous page's <c>NextCursor</c>, or <c>null</c> for the first page.</summary>
    public Guid? After { get; set; }
}

public class GetRestaurantsV2Handler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<GetRestaurantsV2Request, Result<KeysetPage<RestaurantV2Response, Guid>, HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo =
        unitOfWork.GetRepository<Restaurant>();

    public async Task<Result<KeysetPage<RestaurantV2Response, Guid>, HttpError>> HandleAsync(
        GetRestaurantsV2Request request, CancellationToken cancellationToken)
    {
        // Inline QuerySelect projection — no DTO map or mapper registration needed; the shape
        // comes straight from the selector and translates to SQL. Keyset (cursor) pagination
        // stays stable under concurrent writes and fast on deep pages. Member-initializer
        // syntax (not a ctor call) so the keyset ordering can reach Id post-projection.
        return await _restaurantRepo
            .QuerySelect(
                r => new RestaurantV2Response
                {
                    Id = r.Id,
                    Name = r.Name,
                    Cuisine = r.Cuisine == null ? "" : r.Cuisine.Name,
                    TableCount = r.Tables.Count
                },
                q => q)
            .ToKeysetPageAsync(
                r => r.Id, request.PageSize, after: request.After, cancellationToken: cancellationToken);
    }
}
