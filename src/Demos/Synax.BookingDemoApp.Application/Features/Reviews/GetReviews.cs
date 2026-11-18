using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Reviews;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Reviews;

public class GetReviewsRequest : IQuery<Result<KeysetPage<ReviewResponse, Guid>, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public int PageSize { get; set; } = 20;
    public Guid? After { get; set; }
}

public class GetReviewsHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper)
    : IQueryHandler<GetReviewsRequest, Result<KeysetPage<ReviewResponse, Guid>, HttpError>>
{
    private readonly IRepository<Review, BookingContext> _reviewRepo = unitOfWork.GetRepository<Review>();

    public async Task<Result<KeysetPage<ReviewResponse, Guid>, HttpError>> HandleAsync(
        GetReviewsRequest request, CancellationToken cancellationToken)
    {
        // Raw mapper projection path: GetExpression hands the Review → ReviewResponse
        // MemberInit tree straight to QuerySelect, so EF translates it to SQL — only the
        // response columns are selected. Descending keyset pagination over the id gives
        // newest-first ordering: Guid v7 ids are time-ordered, so the primary key doubles
        // as a creation-time cursor.
        return await _reviewRepo
            .QuerySelect(
                mapper.GetExpression<Review, ReviewResponse>(),
                q => q.Where(r => r.RestaurantId == request.RestaurantId))
            .ToKeysetPageAsync(r => r.Id, request.PageSize, after: request.After, descending: true, cancellationToken: cancellationToken);
    }
}
