namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class Bookings
    {
        public const string NotFound = "BOOKING_NOT_FOUND";
        public const string TableNotFound = "BOOKING_TABLE_NOT_FOUND";
        public const string PartyTooLarge = "BOOKING_PARTY_TOO_LARGE";
        public const string DateInPast = "BOOKING_DATE_IN_PAST";
        public const string CreateFailed = "BOOKING_CREATE_FAILED";
        public const string ImportFailed = "BOOKING_IMPORT_FAILED";
        public const string InvalidCursor = "BOOKING_CURSOR_INVALID";
    }
}
