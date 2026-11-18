using Microsoft.EntityFrameworkCore;
using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Tables;

public class HardDeleteTableRequest : ICommand<Result<HttpError>>
{
    public Guid Id { get; set; }
}

public class HardDeleteTableHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<HardDeleteTableRequest, Result<HttpError>>
{
    private readonly IRepository<Table, BookingContext> _tableRepo = unitOfWork.GetRepository<Table>();

    public async Task<Result<HttpError>> HandleAsync(
        HardDeleteTableRequest request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters bypasses the soft-delete and ownership filters, so even a
        // soft-deleted table can be permanently removed. Tracked fetch for HardDelete.
        var entity = await _tableRepo
            .Query(q => q.IgnoreQueryFilters().Where(t => t.Id == request.Id), asNoTracking: false)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return HttpError.NotFound(
                "The specified table was not found.",
                errorCode: ErrorCodes.Tables.NotFound);
        }

        // Physically deletes the row, bypassing the soft-delete conversion.
        _tableRepo.HardDelete(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}
