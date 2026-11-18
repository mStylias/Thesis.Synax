using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.MenuItems;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.MenuItems;

public class CreateMenuItemRequest : ICommand<Result<MenuItemResponse, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public MenuCategory Category { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public class CreateMenuItemHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper)
    : ICommandHandler<CreateMenuItemRequest, Result<MenuItemResponse, HttpError>>,
      IWithValidation<CreateMenuItemRequest, Result<HttpError>>
{
    private readonly IRepository<MenuItem, BookingContext> _menuRepo = unitOfWork.GetRepository<MenuItem>();
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    /// <summary>
    /// Pure short-circuit: the field rule first (no DB round-trip for an invalid price), then
    /// the restaurant check with its own 404 — each failure is its own response.
    /// </summary>
    public async Task<Result<HttpError>> ValidateAsync(
        CreateMenuItemRequest request, CancellationToken cancellationToken)
    {
        var v = new Validation("The menu item request is invalid.")
            .Require(request.Price > 0,
                ErrorCodes.MenuItems.InvalidPrice, "Must be greater than zero.", nameof(request.Price));
        if (v.HasErrors) return v.ToResult();

        if (!await _restaurantRepo.Query(q => q.Where(r => r.Id == request.RestaurantId)).AnyAsync(cancellationToken))
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.",
                errorCode: ErrorCodes.MenuItems.RestaurantNotFound);
        }

        return Result.Ok();
    }

    public async Task<Result<MenuItemResponse, HttpError>> HandleAsync(
        CreateMenuItemRequest request, CancellationToken cancellationToken)
    {
        // AddDtoAsync maps the request straight to the entity through the registered
        // IRepositoryMapper (see MenuItemsRequestProfile) and stages it — no manual
        // mapping in the handler. OwnerId/CreatedAt are stamped by the interceptors.
        var entity = await _menuRepo.AddDtoAsync(request, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return mapper.Map<MenuItemResponse>(entity);
    }
}
