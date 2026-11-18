using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Application.Behaviors;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.BookingDemoApp.Application.Contracts.Reviews;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.Mediator;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Reviews;

public class CreateReviewRequest : ICommand<Result<ReviewResponse, HttpError>>
{
    public Guid RestaurantId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string ReviewerEmail { get; set; } = string.Empty;
}

[MediatorBehavior(typeof(BookingTransactionBehavior<,>))]
public class CreateReviewHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper,
    IOutbox<BookingContext> outbox)
    : ICommandHandler<CreateReviewRequest, Result<ReviewResponse, HttpError>>,
      IWithValidation<CreateReviewRequest, Result<HttpError>>
{
    private readonly IRepository<Review, BookingContext> _reviewRepo = unitOfWork.GetRepository<Review>();
    private readonly IRepository<Restaurant, BookingContext> _restaurantRepo = unitOfWork.GetRepository<Restaurant>();

    /// <summary>
    /// Partial aggregation: the <see cref="Validation"/> context accumulates every field failure
    /// into one 400, then the restaurant check short-circuits with its own 404 — the DB is never
    /// queried for a request whose fields are already invalid.
    /// </summary>
    public async Task<Result<HttpError>> ValidateAsync(
        CreateReviewRequest request, CancellationToken cancellationToken)
    {
        var v = new Validation("The review request is invalid.")
            .Require(request.Rating is >= 1 and <= 5,
                ErrorCodes.Reviews.InvalidRating, "Rating must be between 1 and 5.", nameof(request.Rating))
            .Require(!string.IsNullOrWhiteSpace(request.Comment),
                ErrorCodes.Reviews.InvalidComment, "Comment must not be empty.", nameof(request.Comment));
        if (v.HasErrors) return v.ToResult();

        var restaurantExists = await _restaurantRepo
            .Query(q => q.Where(r => r.Id == request.RestaurantId))
            .AnyAsync(cancellationToken);
        if (!restaurantExists)
        {
            return HttpError.NotFound(
                "The specified restaurant was not found.",
                errorCode: ErrorCodes.Reviews.RestaurantNotFound);
        }

        return Result.Ok();
    }

    public async Task<Result<ReviewResponse, HttpError>> HandleAsync(
        CreateReviewRequest request, CancellationToken cancellationToken)
    {
        // Commit/rollback is handled by BookingTransactionBehavior (see [MediatorBehavior]).
        // The outbox row is saved in the same SaveChanges as the review — atomically. The
        // notification is only dispatched after the transaction commits; on rollback it never
        // existed. OwnerId/CreatedAt/CreatedById are stamped by the interceptors —
        // CreatedById comes from the authenticated user automatically.
        var entity = mapper.Map<Review>(request);

        await _reviewRepo.AddAsync(entity, cancellationToken);

        await outbox.EnqueueAsync(new ReviewSubmittedNotification
        {
            ReviewId = entity.Id,
            RestaurantId = entity.RestaurantId,
            Rating = entity.Rating,
            ReviewerEmail = entity.ReviewerEmail
        }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return mapper.Map<ReviewResponse>(entity);
    }
}
