namespace Synax.BookingDemoApp.Domain.Errors;

/// <summary>
/// Central registry of every machine-readable error code the app returns. Each feature area
/// contributes a nested class from its own partial file (<c>ErrorCodes.Bookings.cs</c>, ...),
/// so codes are never hardcoded at the call site and the full catalog lives in one type.
/// </summary>
public static partial class ErrorCodes;
