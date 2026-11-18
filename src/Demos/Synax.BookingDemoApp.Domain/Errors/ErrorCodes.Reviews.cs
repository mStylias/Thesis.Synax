namespace Synax.BookingDemoApp.Domain.Errors;

public static partial class ErrorCodes
{
    public static class Reviews
    {
        public const string RestaurantNotFound = "REVIEW_RESTAURANT_NOT_FOUND";
        public const string InvalidRating = "REVIEW_RATING_INVALID";
        public const string InvalidComment = "REVIEW_COMMENT_INVALID";
    }
}
