namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class MenuItems
    {
        public const string RestaurantNotFound = "MENU_RESTAURANT_NOT_FOUND";
        public const string SeedFailed = "MENU_SEED_FAILED";
        public const string InvalidAdjustment = "MENU_ADJUSTMENT_INVALID";
        public const string InvalidPrice = "MENU_PRICE_INVALID";
        public const string InvalidName = "MENU_NAME_INVALID";
    }
}
