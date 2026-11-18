using Synax.AspNetCore.Errors;
using Synax.AspNetCore.Identity;
using Synax.BookingDemoApp.Application.Behaviors;
using Synax.BookingDemoApp.Application.Mapping;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Domain.Errors;
using Synax.BookingDemoApp.Domain;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.Mapper.Core;
using Synax.Mediator;
using Synax.Results;

namespace Synax.BookingDemoApp.Application.Features.Auth;

public class GetMeRequest : IQuery<Result<UserResponse, HttpError>>;

/// <summary>
/// <c>[Timed]</c>: the timing behavior is registered globally for commands only, so this query
/// opts in explicitly via the named attribute (see <c>RequestTimingBehavior</c>).
/// </summary>
[Timed]
public class GetMeHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    ICurrentUser<Guid> currentUser,
    IMapper mapper)
    : IQueryHandler<GetMeRequest, Result<UserResponse, HttpError>>
{
    private readonly IRepository<AppUser, BookingContext> _userRepo = unitOfWork.GetRepository<AppUser>();

    public async Task<Result<UserResponse, HttpError>> HandleAsync(
        GetMeRequest request, CancellationToken cancellationToken)
    {
        // TryGetId returns false when unauthenticated or outside an HTTP request — the typed-key
        // shape avoids a silent Guid.Empty.
        if (!currentUser.TryGetId(out var userId))
        {
            return HttpError.Unauthorized(
                "No authenticated user.", errorCode: ErrorCodes.Common.Unauthenticated);
        }

        var user = await _userRepo.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return HttpError.NotFound(
                "The authenticated user no longer exists.", errorCode: ErrorCodes.Auth.UserNotFound);
        }

        return mapper.Map<UserResponse>(user)!;
    }
}
