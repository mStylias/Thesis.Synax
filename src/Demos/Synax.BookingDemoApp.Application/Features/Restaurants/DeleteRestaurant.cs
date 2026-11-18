using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Restaurants;

public class DeleteRestaurantRequest : ICommand<Result<HttpError>>
{
    public Guid Id { get; set; }
}

public class DeleteRestaurantHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<DeleteRestaurantRequest, Result<HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    public async Task<Result<HttpError>> HandleAsync(
        DeleteRestaurantRequest request, CancellationToken cancellationToken)
    {
        var restaurant = await _restaurantRepo
            .Query(q => q.Where(r => r.Id == request.Id), asNoTracking: false)
            .FirstOrDefaultAsync(cancellationToken);

        if (restaurant is null)
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.", errorCode: ErrorCodes.Restaurants.NotFound);
        }

        // Remove is converted to a soft delete by the interceptor, which also stamps
        // DeletedAt/DeletedById — the handler never touches those members.
        return await Result.TryAsync(
            async () =>
            {
                _restaurantRepo.Remove(restaurant);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            },
            ex => HttpError.Internal("Failed to delete the restaurant.", errorCode: ErrorCodes.Restaurants.DeleteFailed));
    }
}
