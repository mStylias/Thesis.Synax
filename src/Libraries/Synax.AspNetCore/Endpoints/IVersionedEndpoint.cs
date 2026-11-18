namespace Synax.AspNetCore.Endpoints;

/// <summary>
/// An endpoint that belongs to a specific API version.
/// Versioned endpoints are mapped under <c>api/v{version}/{tag}/...</c>.
/// </summary>
public interface IVersionedEndpoint : IEndpoint
{
    ApiVersion Version { get; }
}
