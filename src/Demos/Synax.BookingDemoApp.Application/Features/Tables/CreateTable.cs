using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Tables;

public class CreateTableRequest : ICommand<Result<TableResponse, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public int Number { get; set; }
    public int Seats { get; set; }
    public TableLocation Location { get; set; }
}

public class CreateTableHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper)
    : ICommandHandler<CreateTableRequest, Result<TableResponse, HttpError>>,
      IWithValidation<CreateTableRequest, Result<HttpError>>
{
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();
    private readonly IRepository<Table, BookingContext> _tableRepo = unitOfWork.GetRepository<Table>();

    /// <summary>
    /// Showcases the <see cref="Validation"/> context: field rules accumulate so the client sees
    /// every invalid field in a single 400, while the data-backed checks use ordinary control
    /// flow and keep their own status (404, 409) — statuses are never merged.
    /// </summary>
    public async Task<Result<HttpError>> ValidateAsync(
        CreateTableRequest request, CancellationToken cancellationToken)
    {
        var v = new Validation("The table request is invalid.")
            .Require(request.Number > 0,
                ErrorCodes.Tables.InvalidNumber, "Must be greater than zero.", nameof(request.Number))
            .Require(request.Seats > 0,
                ErrorCodes.Tables.InvalidSeats, "Must be greater than zero.", nameof(request.Seats));
        if (v.HasErrors) return v.ToResult();

        var restaurantExists = await _restaurantRepo
            .Query(q => q.Where(r => r.Id == request.RestaurantId))
            .AnyAsync(cancellationToken);
        if (!restaurantExists)
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.",
                errorCode: ErrorCodes.Tables.RestaurantNotFound);
        }

        var duplicateNumber = await _tableRepo
            .Query(q => q.Where(t => t.RestaurantId == request.RestaurantId && t.Number == request.Number))
            .AnyAsync(cancellationToken);
        if (duplicateNumber)
        {
            return HttpError.Conflict(
                $"A table with number {request.Number} already exists in this restaurant.",
                errorCode: ErrorCodes.Tables.DuplicateNumber);
        }

        return Result.Ok();
    }

    public async Task<Result<TableResponse, HttpError>> HandleAsync(
        CreateTableRequest request, CancellationToken cancellationToken)
    {
        // OwnerId/CreatedAt are stamped by the ownership/auditing interceptors on save.
        var entity = mapper.Map<Table>(request);

        await _tableRepo.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return mapper.Map<TableResponse>(entity);
    }
}
