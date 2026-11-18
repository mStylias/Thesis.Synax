using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.MenuItems;

public class AdjustPricesRequest : ICommand<Result<int, HttpError>>
{
    public Guid RestaurantId { get; set; }

    /// <summary>Percentage adjustment, e.g. <c>10</c> = +10%, <c>-25</c> = -25%.</summary>
    public decimal Percent { get; set; }
}

public class AdjustPricesHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<AdjustPricesRequest, Result<int, HttpError>>,
      IWithValidation<AdjustPricesRequest, Result<HttpError>>
{
    private readonly IRepository<MenuItem, BookingContext> _menuRepo = unitOfWork.GetRepository<MenuItem>();

    /// <summary>
    /// A single synchronous rule: the <see cref="Validation"/> context collapses the whole
    /// check-and-build ceremony into one expression.
    /// </summary>
    public Task<Result<HttpError>> ValidateAsync(
        AdjustPricesRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new Validation("The price adjustment is invalid.")
            .Require(request.Percent > -100,
                ErrorCodes.MenuItems.InvalidAdjustment, "Must be greater than -100.", nameof(request.Percent))
            .ToResult());

    public async Task<Result<int, HttpError>> HandleAsync(
        AdjustPricesRequest request, CancellationToken cancellationToken)
    {
        // Set-based update with a computed setter — one SQL statement, no entities loaded,
        // bypasses the change tracker entirely (and therefore SaveChanges).
        var count = await _menuRepo.ExecuteUpdateAsync(
            q => q.Where(m => m.RestaurantId == request.RestaurantId),
            set => set.SetProperty(m => m.Price, m => Math.Round(m.Price * (1 + request.Percent / 100m), 2)),
            cancellationToken);

        return Result.Ok<int, HttpError>(count)
            .AddMessage($"Adjusted {count} prices by {request.Percent}%.");
    }
}
