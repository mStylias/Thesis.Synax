namespace Synax.BookingDemoApp.Application.Exceptions;

/// <summary>
/// A deliberately silly exception thrown by the admin "brew" endpoint, purely to demonstrate the
/// exception handler's custom <c>MapException&lt;T&gt;((HttpContext, ex) =&gt; ...)</c> overload
/// (mapped to HTTP 418 in <c>AddPresentation</c>).
/// </summary>
public class DemoTeapotException(string requestedDrink)
    : Exception($"Cannot brew '{requestedDrink}': this endpoint is a teapot.")
{
    public string RequestedDrink { get; } = requestedDrink;
}
