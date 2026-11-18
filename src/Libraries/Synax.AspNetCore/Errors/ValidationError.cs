using System.Text.Json.Serialization;

namespace Synax.AspNetCore.Errors;

/// <summary>
/// A single field-level validation failure.
/// </summary>
/// <param name="Code">Machine-readable rule code, e.g. <c>"PARTY_TOO_LARGE"</c>.</param>
/// <param name="Description">Human-readable message.</param>
/// <param name="PropertyName">
/// The request property this error belongs to — author with <c>nameof(request.PartySize)</c>;
/// nested paths as dotted strings (<c>"address.street"</c>, <c>"items[2].name"</c>).
/// Serialized camelCased so clients can match it against the request JSON they sent.
/// <c>null</c> for errors that belong to no single field.
/// </param>
/// <param name="InnerErrors">Optional hierarchical child errors.</param>
public record struct ValidationError
(
    string Code,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PropertyName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ICollection<ValidationError>? InnerErrors = null
);
