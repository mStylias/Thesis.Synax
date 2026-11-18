using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Synax.AspNetCore.Errors;
using Synax.BookingDemoApp.Application.Auth;
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

public class LoginRequest : ICommand<Result<LoginResponse, HttpError>>
{
    public string Email { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public UserResponse User { get; set; } = null!;
}

public class LoginHandler(
    IUnitOfWork<BookingContext> unitOfWork,
    IMapper mapper,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider)
    : ICommandHandler<LoginRequest, Result<LoginResponse, HttpError>>
{
    private readonly IRepository<AppUser, BookingContext> _userRepo = unitOfWork.GetRepository<AppUser>();

    public async Task<Result<LoginResponse, HttpError>> HandleAsync(
        LoginRequest request, CancellationToken cancellationToken)
    {
        // The demo has no passwords — knowing a seeded email is "authentication" enough here.
        // Emails are seeded lowercase, so a plain equality match suffices.
        var user = await _userRepo.Query(q => q.Where(u => u.Email == request.Email))
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return HttpError.Unauthorized(
                "Unknown email.", errorCode: ErrorCodes.Auth.InvalidCredentials);
        }

        var jwt = jwtOptions.Value;
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(jwt.ExpiryMinutes);

        // Result.Try showcase: wraps the (exception-throwing) token library call and turns any
        // failure into a typed HttpError instead of letting the exception escape the handler.
        return Result.Try(
            () =>
            {
                var descriptor = new SecurityTokenDescriptor
                {
                    Issuer = jwt.Issuer,
                    Audience = jwt.Audience,
                    Expires = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(jwt.ExpiryMinutes),
                    SigningCredentials = new SigningCredentials(
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                        SecurityAlgorithms.HmacSha256),
                    Claims = new Dictionary<string, object>
                    {
                        ["sub"] = user.Id.ToString(),
                        [JwtOptions.TenantClaim] = user.TenantId.ToString(),
                        ["role"] = user.Role.ToString(),
                        ["name"] = user.Name,
                        ["email"] = user.Email
                    }
                };

                var token = new JsonWebTokenHandler().CreateToken(descriptor);

                return new LoginResponse
                {
                    Token = token,
                    ExpiresAt = expiresAt,
                    User = mapper.Map<UserResponse>(user)!
                };
            },
            ex => HttpError.Internal(
                "Failed to generate the access token.",
                errorCode: ErrorCodes.Auth.TokenGenerationFailed));
    }
}
