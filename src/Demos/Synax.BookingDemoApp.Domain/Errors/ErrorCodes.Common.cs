namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class Common
    {
        public const string Unauthenticated = "UNAUTHENTICATED";
        public const string Forbidden = "FORBIDDEN";
        public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    }
}
