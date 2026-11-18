namespace Synax.BookingDemoApp.Infrastructure.Seeding;

/// <summary>
/// Well-known identifiers for the seeded demo data. Fixed GUIDs so docs, Scalar examples, and
/// tests can reference them. Log in with one of the <c>*Email</c> values at <c>POST /auth/login</c>.
/// </summary>
public static class DemoData
{
    // Tenants (the "owner" in ownership scoping).
    public static readonly Guid TenantAId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid TenantBId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    // Tenant A — "La Tavola Group".
    public static readonly Guid AliceId = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001");
    public const string AliceEmail = "alice@latavola.demo";           // Owner
    public static readonly Guid BobId = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000002");
    public const string BobEmail = "bob@latavola.demo";               // Staff
    public static readonly Guid CarlaId = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000003");
    public const string CarlaEmail = "carla@customers.demo";          // Customer

    // Tenant B — "Sakura Holdings".
    public static readonly Guid DianaId = Guid.Parse("bbbbbbbb-1111-0000-0000-000000000001");
    public const string DianaEmail = "diana@sakura.demo";             // Owner
    public static readonly Guid EvanId = Guid.Parse("bbbbbbbb-1111-0000-0000-000000000002");
    public const string EvanEmail = "evan@customers.demo";            // Customer

    // Global cuisines (OwnerId = null — visible to every tenant).
    public static readonly Guid ItalianCuisineId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    public static readonly Guid JapaneseCuisineId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    public static readonly Guid GreekCuisineId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    // Tenant A's custom cuisine (optional ownership — only tenant A sees it).
    public static readonly Guid CypriotCuisineId = Guid.Parse("cccccccc-aaaa-0000-0000-000000000001");

    // Restaurants.
    public static readonly Guid LaTavolaId = Guid.Parse("dddddddd-aaaa-0000-0000-000000000001");
    public static readonly Guid SakuraHouseId = Guid.Parse("dddddddd-bbbb-0000-0000-000000000001");
}
