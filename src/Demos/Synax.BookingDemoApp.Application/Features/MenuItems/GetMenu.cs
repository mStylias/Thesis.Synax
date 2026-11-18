using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.MenuItems;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.MenuItems;

public class GetMenuRequest : IQuery<Result<List<MenuItemResponse>, HttpError>>
{
    public Guid RestaurantId { get; set; }
}

public class GetMenuHandler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<GetMenuRequest, Result<List<MenuItemResponse>, HttpError>>
{
    private readonly IRepository<MenuItem, BookingContext> _menuRepo = unitOfWork.GetRepository<MenuItem>();

    public async Task<Result<List<MenuItemResponse>, HttpError>> HandleAsync(
        GetMenuRequest request, CancellationToken cancellationToken)
    {
        // QueryDto projects to the response record in SQL via the registered mapper —
        // only the projected columns are selected, nothing is tracked.
        return await _menuRepo
            .QueryDto<MenuItemResponse>(q => q
                .Where(m => m.RestaurantId == request.RestaurantId && m.IsAvailable)
                .OrderBy(m => m.Category)
                .ThenBy(m => m.Name))
            .ToListAsync(cancellationToken);
    }
}
