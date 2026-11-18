namespace Synax.EntityFrameworkCore.OData;

/// <summary>
/// Thrown when a client-supplied OData query is invalid or violates the configured
/// <see cref="SynaxODataOptions"/> guardrails — the "this is a 400" signal. The inner exception is
/// the underlying <c>Microsoft.OData.ODataException</c> when the OData parser rejected the query,
/// and <c>null</c> when a Synax guardrail did. Anything else escaping the OData helpers is a
/// programming or translation error, not a client error.
/// </summary>
public sealed class SynaxODataQueryException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The error code the HTTP helpers report for this exception.</summary>
    public const string ErrorCode = "INVALID_ODATA_QUERY";
}
