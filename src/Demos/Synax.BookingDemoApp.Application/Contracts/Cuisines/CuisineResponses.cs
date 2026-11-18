namespace Synax.BookingDemoApp.Application.Contracts.Cuisines;

/// <summary>
/// The cuisine wire shape. <c>IsGlobal</c> is computed from the optional owner:
/// global rows (<c>OwnerId == null</c>) are visible to every tenant.
/// </summary>
public class CuisineResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsGlobal { get; set; }
}
