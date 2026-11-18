using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Enums;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Tables;

public class GetTablesRequest : IQuery<Result<List<TableResponse>, HttpError>>
{
    public Guid RestaurantId { get; set; }
}

public class GetTablesHandler(IUnitOfWork<BookingContext> unitOfWork)
    : IQueryHandler<GetTablesRequest, Result<List<TableResponse>, HttpError>>
{
    private readonly IRepository<Table, BookingContext> _tableRepo = unitOfWork.GetRepository<Table>();

    /// <summary>
    /// Pure column references so the projection stays translatable to SQL — <c>Location</c> is
    /// converted to its display string in memory afterwards, not inside the query.
    /// </summary>
    private record TableRow(Guid Id, Guid RestaurantId, int Number, int Seats, TableLocation Location);

    public async Task<Result<List<TableResponse>, HttpError>> HandleAsync(
        GetTablesRequest request, CancellationToken cancellationToken)
    {
        // Showcases QuerySelect: an inline projection expression, no mapper involved.
        var rows = await _tableRepo.QuerySelect(
                t => new TableRow(t.Id, t.RestaurantId, t.Number, t.Seats, t.Location),
                q => q.Where(t => t.RestaurantId == request.RestaurantId).OrderBy(t => t.Number))
            .ToListAsync(cancellationToken);

        var tables = rows
            .Select(row => new TableResponse
            {
                Id = row.Id,
                RestaurantId = row.RestaurantId,
                Number = row.Number,
                Seats = row.Seats,
                Location = row.Location.ToString()
            })
            .ToList();

        return tables.Count == 0
            // Warning still succeeds (HTTP 200) — but result logging picks it up at Warning level.
            ? Result.Warn<List<TableResponse>, HttpError>(tables).AddMessage("Restaurant has no tables yet.")
            : Result.Ok(tables);
    }
}
