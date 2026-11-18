namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class Restaurants
    {
        public const string NotFound = "RESTAURANT_NOT_FOUND";
        public const string NotDeleted = "RESTAURANT_NOT_DELETED";
        public const string InvalidName = "RESTAURANT_NAME_INVALID";
        public const string InvalidAddress = "RESTAURANT_ADDRESS_INVALID";
        public const string InvalidCapacity = "RESTAURANT_CAPACITY_INVALID";
        public const string UnknownCuisine = "RESTAURANT_CUISINE_UNKNOWN";
        public const string CreateFailed = "RESTAURANT_CREATE_FAILED";
        public const string FetchFailed = "RESTAURANT_FETCH_FAILED";
        public const string UpdateFailed = "RESTAURANT_UPDATE_FAILED";
        public const string DeleteFailed = "RESTAURANT_DELETE_FAILED";
        public const string RestoreFailed = "RESTAURANT_RESTORE_FAILED";
    }
}
