namespace Synax.Results;

/// <summary>
/// Exposes a machine-readable error code on a typed error. Generic consumers that cannot know
/// the concrete error type (telemetry, logging) use this to surface the code — e.g. the Synax
/// mediator tags failed spans with <c>synax.error_code</c> when a result's error implements it.
/// </summary>
public interface IHasErrorCode
{
    string ErrorCode { get; }
}
