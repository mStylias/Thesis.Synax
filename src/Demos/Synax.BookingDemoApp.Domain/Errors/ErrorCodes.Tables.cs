namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class Tables
    {
        public const string NotFound = "TABLE_NOT_FOUND";
        public const string DuplicateNumber = "TABLE_NUMBER_DUPLICATE";
        public const string InvalidSeats = "TABLE_SEATS_INVALID";
        public const string InvalidNumber = "TABLE_NUMBER_INVALID";
        public const string RestaurantNotFound = "TABLE_RESTAURANT_NOT_FOUND";
    }
}
