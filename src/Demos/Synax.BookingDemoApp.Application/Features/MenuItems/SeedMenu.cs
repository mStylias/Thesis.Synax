using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.MenuItems;

public class SeedMenuRequest : ICommand<Result<int, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public List<MenuItemInput> Items { get; set; } = [];
}

public class SeedMenuHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper)
    : ICommandHandler<SeedMenuRequest, Result<int, HttpError>>,
      IWithValidation<SeedMenuRequest, Result<HttpError>>
{
    private readonly IRepository<MenuItem, BookingContext> _menuRepo = unitOfWork.GetRepository<MenuItem>();
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    /// <summary>
    /// Partial aggregation over a collection: every item's field failures accumulate into one
    /// 400 with indexed property paths (<c>items[2].price</c>) so clients can pin each error to
    /// the exact list element, then the restaurant check short-circuits with its own 404.
    /// </summary>
    public async Task<Result<HttpError>> ValidateAsync(
        SeedMenuRequest request, CancellationToken cancellationToken)
    {
        var v = new Validation("The menu seed request is invalid.");
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];

            v
                .Require(!string.IsNullOrWhiteSpace(item.Name),
                    ErrorCodes.MenuItems.InvalidName, "Name is required.",
                    $"{nameof(request.Items)}[{i}].{nameof(item.Name)}")
                .Require(item.Price > 0,
                    ErrorCodes.MenuItems.InvalidPrice, "Must be greater than zero.",
                    $"{nameof(request.Items)}[{i}].{nameof(item.Price)}");
        }
        if (v.HasErrors) return v.ToResult();

        if (!await _restaurantRepo.Query(q => q.Where(r => r.Id == request.RestaurantId)).AnyAsync(cancellationToken))
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.",
                errorCode: ErrorCodes.MenuItems.RestaurantNotFound);
        }

        return Result.Ok();
    }

    public async Task<Result<int, HttpError>> HandleAsync(
        SeedMenuRequest request, CancellationToken cancellationToken)
    {
        // MapList maps every input in one call; RestaurantId is set here because the
        // MenuItemInput map intentionally ignores it (see MenuItemsRequestProfile).
        var entities = mapper.MapList<MenuItem>(request.Items);
        entities.ForEach(e => e.RestaurantId = request.RestaurantId);

        // BulkInsertAsync uses the provider-optimized path (COPY on Postgres) and acts
        // immediately — no SaveChanges. It stamps CreatedAt and OwnerId itself.
        return await Result.TryAsync(
            async () =>
            {
                await _menuRepo.BulkInsertAsync(entities, cancellationToken);
                return entities.Count;
            },
            ex => HttpError.Internal("Failed to seed the menu.", errorCode: ErrorCodes.MenuItems.SeedFailed));
    }
}
