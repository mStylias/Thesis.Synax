namespace Synax.EntityFrameworkCore.Abstractions.Ownership;

/// <summary>
/// Marks an entity as optionally owned by some scoping target (commonly a company or tenant).
/// When <see cref="OwnerId"/> is <c>null</c>, the row is considered shared and accessible to
/// everyone; otherwise, only the matching owner can access it.
/// </summary>
/// <typeparam name="TKey">
/// The type of the owner identifier property. Use the nullable form of the key type
/// (e.g. <c>int?</c>, <c>Guid?</c>, <c>string?</c>).
/// </typeparam>
public interface IHasOptionalOwner<TKey>
{
    /// <summary>
    /// The identifier of the owner this row belongs to, or <c>null</c> if the row is shared.
    /// </summary>
    TKey OwnerId { get; set; }
}
