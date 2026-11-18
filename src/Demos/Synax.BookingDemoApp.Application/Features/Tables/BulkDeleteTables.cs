using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Tables;

public class BulkDeleteTablesRequest : ICommand<Result<int, HttpError>>
{
    public Guid RestaurantId { get; set; }
}

public class BulkDeleteTablesHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<BulkDeleteTablesRequest, Result<int, HttpError>>
{
    private readonly IRepository<Table, BookingContext> _tableRepo = unitOfWork.GetRepository<Table>();

    public async Task<Result<int, HttpError>> HandleAsync(
        BulkDeleteTablesRequest request, CancellationToken cancellationToken)
    {
        // Set-based delete: Table implements ISoftDelete, so this is automatically converted
        // to a soft-delete UPDATE. It acts immediately — no SaveChangesAsync involved.
        var count = await _tableRepo.ExecuteDeleteAsync(
            q => q.Where(t => t.RestaurantId == request.RestaurantId), cancellationToken);

        return Result.Ok<int, HttpError>(count).AddMessage($"Soft-deleted {count} tables.");
    }
}
