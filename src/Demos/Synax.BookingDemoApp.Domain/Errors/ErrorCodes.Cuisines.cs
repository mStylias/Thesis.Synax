namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class Cuisines
    {
        public const string NotFound = "CUISINE_NOT_FOUND";
        public const string DuplicateName = "CUISINE_NAME_DUPLICATE";
        public const string InvalidName = "CUISINE_NAME_INVALID";
    }
}
