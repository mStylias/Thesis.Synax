namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class Auth
    {
        public const string InvalidCredentials = "INVALID_CREDENTIALS";
        public const string TokenGenerationFailed = "TOKEN_GENERATION_FAILED";
        public const string UserNotFound = "USER_NOT_FOUND";
    }
}
