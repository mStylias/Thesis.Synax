using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Tables;

public class DeleteTableRequest : ICommand<Result<HttpError>>
{
    public Guid Id { get; set; }
}

public class DeleteTableHandler(IUnitOfWork<BookingContext> unitOfWork)
    : ICommandHandler<DeleteTableRequest, Result<HttpError>>
{
    private readonly IRepository<Table, BookingContext> _tableRepo = unitOfWork.GetRepository<Table>();

    public async Task<Result<HttpError>> HandleAsync(
        DeleteTableRequest request, CancellationToken cancellationToken)
    {
        // Tracked fetch — Remove needs a tracked entity for the soft-delete conversion.
        var entity = await _tableRepo
            .Query(q => q.Where(t => t.Id == request.Id), asNoTracking: false)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            // WithMessage is a server-side log breadcrumb — never serialized to clients.
            return Result
                .Fail(HttpError.NotFound("The specified table was not found.", errorCode: ErrorCodes.Tables.NotFound))
                .WithMessage($"Table {request.Id} not found for deletion.");
        }

        // The soft-delete interceptor converts this removal into IsDeleted/DeletedAt stamps.
        _tableRepo.Remove(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}
