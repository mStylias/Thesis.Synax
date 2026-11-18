using Synax.BookingDemoApp.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Synax.BookingDemoApp.Domain.Entities;
using Synax.BookingDemoApp.Infrastructure;
using Synax.BookingDemoApp.Application.Contracts.Restaurants;
using Synax.BookingDemoApp.Application.Contracts.Tables;
using Synax.EntityFrameworkCore.Abstractions.Core;
using Synax.EntityFrameworkCore.Core;
using Synax.EntityFrameworkCore.OData;

namespace Synax.BookingDemoApp.IntegrationTests;

/// <summary>
/// Proves the OData query options translate to real SQL over mapper-projected DTO queries
/// (<c>QueryDto</c>): $filter/$orderby/$top/$skip/$count, $select's wrapper projection, $expand
/// over a nested DTO collection, and that ownership scoping still bounds everything the client
/// can reach. The unit tests cover the same surface over LINQ-to-Objects; this is the
/// translation-side proof.
/// </summary>
[ClassDataSource<BookingFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("booking-demo-identity")]
public class ODataSqlTests(BookingFixture fixture)
{
    [Test]
    public async Task FilterOrderSkipTopCount_TranslateOverProjectedQuery()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Alpha", 10),
            Restaurant(tenant, cuisineId, "Beta", 20),
            Restaurant(tenant, cuisineId, "Gamma", 30),
            Restaurant(tenant, cuisineId, "Delta", 40),
            Restaurant(tenant, cuisineId, "Epsilon", 50));

        var page = await QueryRestaurantsAsync(
            "?$filter=maxCapacity gt 25&$orderby=maxCapacity desc&$skip=1&$top=2&$count=true");

        var names = page.Value.Cast<RestaurantResponse>().Select(r => r.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Delta", "Gamma"]);
        await Assert.That(page.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Filter_OnStringFunctions_Translates()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Blue Lagoon", 10),
            Restaurant(tenant, cuisineId, "Green Garden", 20));

        var page = await QueryRestaurantsAsync("?$filter=contains(name,'Lagoon')");

        var names = page.Value.Cast<RestaurantResponse>().Select(r => r.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Blue Lagoon"]);
    }

    [Test]
    public async Task Filter_OnEnumMember_TranslatesEntityRooted_ButNotOverEnumToStringProjection()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenant, cuisineId));
        await fixture.SeedAsync(
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 1, seats: 2),
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 2, seats: 6));

        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        // Entity-rooted: the EDM sees the enum, the binder emits Location == TableLocation.Indoor,
        // and the HasConversion<string> converter turns that into a SQL string comparison.
        var page = await unitOfWork.GetRepository<Table>()
            .Query()
            .ToODataPageAsync(CreateRequest("?$filter=location eq 'Indoor' and seats ge 4"));

        var rows = page.Value.Cast<Table>().ToList();
        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0].Number).IsEqualTo(2);

        // DTO-projected: the mapper's enum→string binding compiles to Location.ToString(), which
        // EF cannot translate inside a WHERE — a documented limitation (filter enums entity-rooted
        // or expose a translatable member instead).
        Func<Task> filterOverProjection = () => unitOfWork.GetRepository<Table>()
            .QueryDto<TableResponse>()
            .ToODataPageAsync(CreateRequest("?$filter=location eq 'Indoor'"));

        await Assert.That(filterOverProjection).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Select_ProjectsToDictionariesInSql()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Solo", 15),
            Restaurant(tenant, cuisineId, "Duo", 25));

        var page = await QueryRestaurantsAsync("?$select=name,maxCapacity&$orderby=maxCapacity&$top=1");

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row.Keys).IsEquivalentTo(["name", "maxCapacity"]);
        await Assert.That(row["name"]).IsEqualTo("Solo");
    }

    [Test]
    public async Task Expand_OverProjectedNestedCollection_Translates()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenant, cuisineId));
        await fixture.SeedAsync(
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 1),
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 2));

        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        var page = await unitOfWork.GetRepository<Restaurant>()
            .QueryDto<RestaurantDetailResponse>(q => q.Where(r => r.Id == restaurantId))
            .ToODataPageAsync(CreateRequest("?$expand=tables"));

        var row = (IDictionary<string, object?>)page.Value[0]!;
        var tables = (List<object?>)row["tables"]!;
        await Assert.That(tables).Count().IsEqualTo(2);

        var numbers = tables
            .Cast<IDictionary<string, object?>>()
            .Select(t => t["number"])
            .ToList();
        await Assert.That(numbers).Contains(1);
        await Assert.That(numbers).Contains(2);
    }

    [Test]
    public async Task OwnershipScoping_BoundsEverythingTheClientCanQuery()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenantA);
        await fixture.SeedAsync(Restaurant(tenantA, cuisineId, "Secret Place", 12));

        // Tenant B can send any OData query it likes — the ownership filter runs underneath.
        fixture.ActAsTenant(tenantB);
        var page = await QueryRestaurantsAsync("?$filter=contains(name,'Secret')&$count=true");

        await Assert.That(page.Value).Count().IsEqualTo(0);
        await Assert.That(page.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidQuery_ThrowsSynaxODataQueryException_NotSql()
    {
        fixture.ActAsTenant(Guid.NewGuid());

        Func<Task> act = () => QueryRestaurantsAsync("?$filter=noSuchColumn eq 1");

        await Assert.That(act).Throws<SynaxODataQueryException>();
    }

    // ---------- $compute (Phase 1) ----------

    [Test]
    public async Task Compute_ArithmeticTranslatesToSql()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Small", 8),
            Restaurant(tenant, cuisineId, "Medium", 40),
            Restaurant(tenant, cuisineId, "Large", 120));

        var page = await QueryRestaurantsAsync(
            "?$compute=maxCapacity div 4 as tableCount&$filter=tableCount ge 10&$orderby=tableCount desc",
            new SynaxODataOptions { EnableCompute = true });

        var names = page.Value.Cast<RestaurantResponse>().Select(r => r.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Large", "Medium"]);

        // Both the filter over the computed value and the ordering by it are in the statement
        // (the divisor is parameterized), not applied to materialized rows afterwards.
        var sql = fixture.Sql.LastMentioning("restaurants");
        await Assert.That(sql).Contains("WHERE");
        await Assert.That(sql).Contains("""r."MaxCapacity" / @""");
        await Assert.That(sql).Contains("""ORDER BY r."MaxCapacity" / @""");
    }

    [Test]
    public async Task Compute_SelectedValueComesBackFromSql()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(Restaurant(tenant, cuisineId, "Solo", 40));

        var page = await QueryRestaurantsAsync(
            "?$compute=maxCapacity div 4 as tableCount&$select=name,tableCount",
            new SynaxODataOptions { EnableCompute = true });

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row.Keys).IsEquivalentTo(["name", "tableCount"]);
        await Assert.That(row["tableCount"]).IsEqualTo(10);
    }

    // ---------- $search (Phase 2) ----------

    private static SynaxODataOptions Searchable() =>
        new SynaxODataOptions().Searchable<RestaurantResponse>(
            restaurant => restaurant.Name,
            restaurant => restaurant.Address);

    [Test]
    public async Task Search_TranslatesToACaseInsensitiveSqlComparison()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Harbour Grill", 30),
            Restaurant(tenant, cuisineId, "Mountain View", 30));

        var page = await QueryRestaurantsAsync("?$search=HARBOUR", Searchable());

        var names = page.Value.Cast<RestaurantResponse>().Select(r => r.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Harbour Grill"]);

        // lower(...) plus a containment operator — a scan, but a scan the database performs.
        var sql = fixture.Sql.LastMentioning("restaurants");
        await Assert.That(sql).Contains("lower(");
    }

    [Test]
    public async Task Search_CombinesTermsAndComposesWithFilterAndCount()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Harbour Grill", 30),
            Restaurant(tenant, cuisineId, "Harbour Cafe", 10),
            Restaurant(tenant, cuisineId, "Mountain Grill", 30));

        var page = await QueryRestaurantsAsync(
            "?$search=harbour OR mountain&$filter=maxCapacity gt 20&$orderby=name&$count=true",
            Searchable());

        var names = page.Value.Cast<RestaurantResponse>().Select(r => r.Name).ToList();
        await Assert.That(names).IsEquivalentTo(["Harbour Grill", "Mountain Grill"]);
        await Assert.That(page.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Search_RunsUnderneathOwnershipScoping()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenantA);
        await fixture.SeedAsync(Restaurant(tenantA, cuisineId, "Hidden Harbour", 30));

        fixture.ActAsTenant(tenantB);
        var page = await QueryRestaurantsAsync("?$search=harbour&$count=true", Searchable());

        await Assert.That(page.Value).Count().IsEqualTo(0);
        await Assert.That(page.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Search_AgainstATypeWithNoDeclaredMembers_IsAClientError()
    {
        fixture.ActAsTenant(Guid.NewGuid());

        Func<Task> act = () => QueryRestaurantsAsync("?$search=harbour");

        var exception = await Assert.That(act).Throws<SynaxODataQueryException>();
        await Assert.That(exception!.Message).Contains(nameof(RestaurantResponse));
    }

    // ---------- Server-driven paging (Phase 3) ----------

    [Test]
    public async Task Paging_WalksEveryRowWithTheLimitInSql()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var settings = new SynaxODataOptions { PageSize = 2 };

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        for (var index = 1; index <= 5; index++)
            await fixture.SeedAsync(Restaurant(tenant, cuisineId, "Page" + index, index * 10));

        var page = await QueryRestaurantsAsync("?$orderby=maxCapacity", settings);

        // The database returns pageSize + 1 rows, never the whole table.
        await Assert.That(fixture.Sql.LastMentioning("restaurants")).Contains("LIMIT");

        var seen = new List<string>();
        for (var guard = 0; guard < 10; guard++)
        {
            seen.AddRange(page.Value.Cast<RestaurantResponse>().Select(r => r.Name));
            await Assert.That(page.Value.Count).IsLessThanOrEqualTo(2);

            if (page.NextLink is null)
                break;

            page = await FollowRestaurantsAsync(page.NextLink, settings);
        }

        await Assert.That(seen).IsEquivalentTo(["Page1", "Page2", "Page3", "Page4", "Page5"]);
        await Assert.That(page.NextLink).IsNull();
    }

    [Test]
    public async Task Paging_IsStableWhenRowsAreInsertedBetweenPages()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var settings = new SynaxODataOptions { PageSize = 2 };

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Alpha", 10),
            Restaurant(tenant, cuisineId, "Bravo", 20),
            Restaurant(tenant, cuisineId, "Delta", 40),
            Restaurant(tenant, cuisineId, "Echo", 50));

        var first = await QueryRestaurantsAsync("?$orderby=name", settings);
        await Assert.That(first.Value.Cast<RestaurantResponse>().Select(r => r.Name).ToList())
            .IsEquivalentTo(["Alpha", "Bravo"]);

        // A row that sorts *before* the cursor appears; a $skip-based page would now re-serve
        // "Bravo". A keyset cursor cannot: it asks for the rows after "Bravo".
        await fixture.SeedAsync(Restaurant(tenant, cuisineId, "Aardvark", 5));

        var second = await FollowRestaurantsAsync(first.NextLink!, settings);
        await Assert.That(second.Value.Cast<RestaurantResponse>().Select(r => r.Name).ToList())
            .IsEquivalentTo(["Delta", "Echo"]);
    }

    [Test]
    public async Task Paging_CountStillReportsTheFullFilteredTotal()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "One", 10),
            Restaurant(tenant, cuisineId, "Two", 20),
            Restaurant(tenant, cuisineId, "Three", 30));

        var page = await QueryRestaurantsAsync(
            "?$orderby=name&$count=true",
            new SynaxODataOptions { PageSize = 1 });

        await Assert.That(page.Value).Count().IsEqualTo(1);
        await Assert.That(page.Count).IsEqualTo(3);
        await Assert.That(page.NextLink).IsNotNull();
    }

    [Test]
    public async Task Paging_OverADateTimeSortKeyRoundTripsTheCursor()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var settings = new SynaxODataOptions { PageSize = 1 };

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(Restaurant(tenant, cuisineId, "First", 10));
        await fixture.SeedAsync(Restaurant(tenant, cuisineId, "Second", 20));

        var first = await QueryRestaurantsAsync("?$orderby=createdAt", settings);
        await Assert.That(first.NextLink).IsNotNull();

        var second = await FollowRestaurantsAsync(first.NextLink!, settings);

        var firstName = first.Value.Cast<RestaurantResponse>().Single().Name;
        var secondName = second.Value.Cast<RestaurantResponse>().Single().Name;
        await Assert.That(firstName).IsNotEqualTo(secondName);
    }

    [Test]
    public async Task Paging_WithSelect_ReshapesRowsAndStillPages()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var settings = new SynaxODataOptions { PageSize = 1 };

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Selected", 10),
            Restaurant(tenant, cuisineId, "Zeta", 20));

        var page = await QueryRestaurantsAsync("?$select=name&$orderby=name", settings);

        var row = (IDictionary<string, object?>)page.Value[0]!;
        await Assert.That(row.Keys).IsEquivalentTo(["name"]);
        await Assert.That(row["name"]).IsEqualTo("Selected");

        var next = await FollowRestaurantsAsync(page.NextLink!, settings);
        await Assert.That(((IDictionary<string, object?>)next.Value[0]!)["name"]).IsEqualTo("Zeta");
    }

    // ---------- $apply (Phase 4) ----------

    private static SynaxODataOptions Aggregating() => new() { EnableApply = true };

    [Test]
    public async Task Apply_GroupByWithAggregate_TranslatesToAGroupByStatement()
    {
        var tenant = Guid.NewGuid();
        var italian = Guid.NewGuid();
        var greek = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(italian), TestData.Cuisine(greek));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, italian, "Roma", 30),
            Restaurant(tenant, italian, "Milano", 20),
            Restaurant(tenant, greek, "Athina", 45));

        var page = await QueryRestaurantsAsync(
            "?$apply=groupby((cuisineId),aggregate(maxCapacity with sum as seats,$count as venues))",
            Aggregating());

        var groups = page.Value
            .Cast<IDictionary<string, object?>>()
            .ToDictionary(row => (Guid)row["cuisineId"]!, row => row);

        await Assert.That(groups[italian]["seats"]).IsEqualTo(50);
        await Assert.That(groups[italian]["venues"]).IsEqualTo(2L);
        await Assert.That(groups[greek]["seats"]).IsEqualTo(45);

        // The database did the grouping — not the client after reading every row.
        var sql = fixture.Sql.LastMentioning("restaurants");
        await Assert.That(sql).Contains("GROUP BY");
        await Assert.That(sql).Contains("sum(");
    }

    [Test]
    public async Task Apply_AggregateWithoutGroupBy_TranslatesToASingleRowAggregate()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Small", 10),
            Restaurant(tenant, cuisineId, "Large", 90));

        var page = await QueryRestaurantsAsync(
            "?$apply=aggregate(maxCapacity with min as smallest,maxCapacity with max as largest," +
            "maxCapacity with sum as total)",
            Aggregating());

        var row = (IDictionary<string, object?>)page.Value.Single()!;
        await Assert.That(row["smallest"]).IsEqualTo(10);
        await Assert.That(row["largest"]).IsEqualTo(90);
        await Assert.That(row["total"]).IsEqualTo(100);

        var sql = fixture.Sql.LastMentioning("restaurants");
        await Assert.That(sql).Contains("min(");
        await Assert.That(sql).Contains("max(");
    }

    [Test]
    public async Task Apply_ComputeThenAggregate_PutsTheArithmeticInTheGroupedSql()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Alpha", 40),
            Restaurant(tenant, cuisineId, "Beta", 60));

        var page = await QueryRestaurantsAsync(
            "?$apply=compute(maxCapacity div 4 as tables)/groupby((cuisineId)," +
            "aggregate(tables with sum as totalTables))",
            Aggregating());

        var row = (IDictionary<string, object?>)page.Value.Single()!;
        await Assert.That(row["totalTables"]).IsEqualTo(25);

        var sql = fixture.Sql.LastMentioning("restaurants");
        await Assert.That(sql).Contains("GROUP BY");
        await Assert.That(sql).Contains("MaxCapacity\" / @");
    }

    [Test]
    public async Task Apply_FilterTransformation_NarrowsBeforeGroupingAndKeepsTheWhereInSql()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, cuisineId, "Tiny", 5),
            Restaurant(tenant, cuisineId, "Big", 50),
            Restaurant(tenant, cuisineId, "Bigger", 70));

        var page = await QueryRestaurantsAsync(
            "?$apply=filter(maxCapacity gt 10)/groupby((cuisineId),aggregate($count as venues))",
            Aggregating());

        var row = (IDictionary<string, object?>)page.Value.Single()!;
        await Assert.That(row["venues"]).IsEqualTo(2L);

        var sql = fixture.Sql.LastMentioning("restaurants");
        await Assert.That(sql).Contains("WHERE");
        await Assert.That(sql).Contains("GROUP BY");
    }

    [Test]
    public async Task Apply_RunsUnderneathOwnershipScoping()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenantA);
        await fixture.SeedAsync(Restaurant(tenantA, cuisineId, "Mine", 30));

        fixture.ActAsTenant(tenantB);
        await fixture.SeedAsync(
            Restaurant(tenantB, cuisineId, "Theirs One", 100),
            Restaurant(tenantB, cuisineId, "Theirs Two", 100));

        // Back to A: the aggregate must describe A's world only.
        fixture.ActAsTenant(tenantA);
        var page = await QueryRestaurantsAsync(
            "?$apply=aggregate(maxCapacity with sum as total,$count as venues)",
            Aggregating());

        var row = (IDictionary<string, object?>)page.Value.Single()!;
        await Assert.That(row["total"]).IsEqualTo(30);
        await Assert.That(row["venues"]).IsEqualTo(1L);
    }

    [Test]
    public async Task Apply_CountReportsTheNumberOfGroups()
    {
        var tenant = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(first), TestData.Cuisine(second));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Restaurant(tenant, first, "A", 10),
            Restaurant(tenant, first, "B", 20),
            Restaurant(tenant, second, "C", 30));

        var page = await QueryRestaurantsAsync(
            "?$apply=groupby((cuisineId),aggregate(maxCapacity with sum as seats))&$count=true",
            Aggregating());

        await Assert.That(page.Value).Count().IsEqualTo(2);
        await Assert.That(page.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Apply_PagesGroupsByOffsetAndWalksThemAll()
    {
        var tenant = Guid.NewGuid();
        var cuisines = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var settings = new SynaxODataOptions { EnableApply = true, PageSize = 2 };

        fixture.ActAsNobody();
        foreach (var cuisine in cuisines)
            await fixture.SeedAsync(TestData.Cuisine(cuisine));

        fixture.ActAsTenant(tenant);
        foreach (var cuisine in cuisines)
            await fixture.SeedAsync(Restaurant(tenant, cuisine, "R" + cuisine.ToString("N")[..4], 10));

        var first = await QueryRestaurantsAsync(
            "?$apply=groupby((cuisineId),aggregate($count as venues))&$orderby=cuisineId", settings);

        await Assert.That(first.Value).Count().IsEqualTo(2);
        await Assert.That(first.NextLink).Contains("$skip=2");

        var second = await FollowRestaurantsAsync(first.NextLink!, settings);
        await Assert.That(second.Value).Count().IsEqualTo(1);
        await Assert.That(second.NextLink).IsNull();

        var seen = first.Value.Concat(second.Value)
            .Cast<IDictionary<string, object?>>()
            .Select(row => (Guid)row["cuisineId"]!)
            .ToList();

        await Assert.That(seen.Order().ToList()).IsEquivalentTo(cuisines.Order().ToList());
    }

    [Test]
    public async Task Apply_GroupingAnEnumProjection_HitsTheSameLimitFilteringDoes()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenant, cuisineId));
        await fixture.SeedAsync(
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 1, seats: 2),
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 2, seats: 6));

        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        // Entity-rooted: the EDM models the enum, and HasConversion<string> groups it in SQL.
        var page = await unitOfWork.GetRepository<Table>()
            .Query()
            .ToODataPageAsync(
                CreateRequest("?$apply=groupby((location),aggregate(seats with sum as capacity))"),
                Aggregating());

        var row = (IDictionary<string, object?>)page.Value.Single()!;
        await Assert.That(row["capacity"]).IsEqualTo(8);

        // DTO-projected: the mapper's Location.ToString() cannot be translated into a GROUP BY,
        // exactly as it cannot be translated into a WHERE — the documented mapper limitation.
        Func<Task> groupOverProjection = () => unitOfWork.GetRepository<Table>()
            .QueryDto<TableResponse>()
            .ToODataPageAsync(
                CreateRequest("?$apply=groupby((location),aggregate(seats with sum as capacity))"),
                Aggregating());

        await Assert.That(groupOverProjection).Throws<InvalidOperationException>();
    }

    // ---------- Nested $expand options (Phase 5) ----------

    [Test]
    public async Task NestedFilterOrderByAndTop_TranslateIntoTheExpandedSubquery()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenant, cuisineId));
        await fixture.SeedAsync(
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 1, seats: 2),
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 2, seats: 6),
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 3, seats: 8));

        var row = await DetailRowAsync(
            restaurantId, "?$expand=tables($filter=seats gt 4;$orderby=number desc;$top=1)");

        var tables = ((List<object?>)row["tables"]!).Cast<IDictionary<string, object?>>().ToList();
        await Assert.That(tables).Count().IsEqualTo(1);
        await Assert.That(tables[0]["number"]).IsEqualTo(3);

        // The narrowing happened in the database, not after reading every table.
        var sql = fixture.Sql.LastMentioning("tables");
        await Assert.That(sql).Contains("Seats");
        await Assert.That(sql).Contains("ORDER BY");
    }

    [Test]
    public async Task NestedCount_ComesBackFromSqlAlongsideTheWindowedRows()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenant, cuisineId));
        await fixture.SeedAsync(
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 1),
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 2),
            TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 3));

        var row = await DetailRowAsync(restaurantId, "?$expand=tables($orderby=number;$top=1;$count=true)");

        await Assert.That((List<object?>)row["tables"]!).Count().IsEqualTo(1);
        await Assert.That(row["tables@odata.count"]).IsEqualTo(3L);

        // The total is the database's, not a count of the one row that came back.
        var sql = fixture.Sql.LastMentioning("tables");
        await Assert.That(sql).Contains("count(");
    }

    [Test]
    public async Task NestedSelect_ProjectsTheExpandedRowsInSql()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenant, cuisineId));
        await fixture.SeedAsync(TestData.Table(Guid.NewGuid(), restaurantId, tenant, number: 7, seats: 4));

        var row = await DetailRowAsync(restaurantId, "?$select=name&$expand=tables($select=number)");

        var table = (IDictionary<string, object?>)((List<object?>)row["tables"]!)[0]!;
        await Assert.That(table.Keys).IsEquivalentTo(["number"]);
        await Assert.That(row.Keys).IsEquivalentTo(["name", "tables"]);
    }

    [Test]
    public async Task NestedFilter_OnAReferenceNavigation_EmptiesTheMemberInSql()
    {
        var tenant = Guid.NewGuid();
        var wanted = Guid.NewGuid();
        var other = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(wanted), TestData.Cuisine(other));

        fixture.ActAsTenant(tenant);
        var match = Restaurant(tenant, wanted, "Matches", 20);
        var miss = Restaurant(tenant, other, "Misses", 20);
        await fixture.SeedAsync(match, miss);

        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        // Entity-rooted, because the exposed DTOs flatten the cuisine down to a name — a reference
        // navigation is what a nested $filter needs something to empty.
        var page = await unitOfWork.GetRepository<Restaurant>()
            .Query(q => q.Where(r => r.Id == match.Id || r.Id == miss.Id))
            .ToODataPageAsync(CreateRequest(
                $"?$select=name&$expand=cuisine($filter=id eq {wanted})&$orderby=name"));

        var byName = page.Value
            .Cast<IDictionary<string, object?>>()
            .ToDictionary(row => (string)row["name"]!, row => row["cuisine"]);

        await Assert.That(byName["Matches"]).IsNotNull();
        await Assert.That(byName["Misses"]).IsNull();
    }

    [Test]
    public async Task NestedOptions_RunUnderneathOwnershipScoping()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenantA);
        await fixture.SeedAsync(TestData.Restaurant(restaurantId, tenantA, cuisineId));
        await fixture.SeedAsync(TestData.Table(Guid.NewGuid(), restaurantId, tenantA, number: 1, seats: 9));

        // Another tenant's table against the same restaurant row: only the ownership filter keeps it
        // out of the expansion, and the nested $filter would otherwise happily match it.
        fixture.ActAsTenant(tenantB);
        await fixture.SeedAsync(TestData.Table(Guid.NewGuid(), restaurantId, tenantB, number: 2, seats: 9));

        fixture.ActAsTenant(tenantA);
        var row = await DetailRowAsync(restaurantId, "?$expand=tables($filter=seats gt 4;$count=true)");

        var tables = ((List<object?>)row["tables"]!).Cast<IDictionary<string, object?>>().ToList();
        await Assert.That(tables).Count().IsEqualTo(1);
        await Assert.That(tables[0]["number"]).IsEqualTo(1);
        await Assert.That(row["tables@odata.count"]).IsEqualTo(1L);
    }

    [Test]
    public async Task CollectionFilters_TranslateToExistsSubqueries()
    {
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var withLarge = Guid.NewGuid();
        var withSmall = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            TestData.Restaurant(withLarge, tenant, cuisineId),
            TestData.Restaurant(withSmall, tenant, cuisineId));
        await fixture.SeedAsync(
            TestData.Table(Guid.NewGuid(), withLarge, tenant, number: 1, seats: 10),
            TestData.Table(Guid.NewGuid(), withSmall, tenant, number: 1, seats: 2));

        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        var page = await unitOfWork.GetRepository<Restaurant>()
            .Query(q => q.Where(r => r.Id == withLarge || r.Id == withSmall))
            .ToODataPageAsync(CreateRequest("?$filter=tables/any(t: t/seats gt 8)"));

        var ids = page.Value.Cast<Restaurant>().Select(r => r.Id).ToList();
        await Assert.That(ids).IsEquivalentTo([withLarge]);

        var sql = fixture.Sql.LastMentioning("restaurants");
        await Assert.That(sql).Contains("EXISTS");
    }

    [Test]
    public async Task FilterOrderTop_TranslateOverRecordProjection_WithAnAggregateMember()
    {
        // A positional record is projected through its constructor, not an object initializer.
        // Composing a client option over that projection yields `new Response(...).Member`, and
        // EF can only reduce it when the projection names the member each argument initializes.
        // Unnamed, the whole constructor stays inside the ORDER BY or WHERE and the query fails
        // to translate — every option below threw before the mapper named them.
        var tenant = Guid.NewGuid();
        var cuisineId = Guid.NewGuid();
        var busy = Guid.NewGuid();
        var quiet = Guid.NewGuid();

        fixture.ActAsNobody();
        await fixture.SeedAsync(TestData.Cuisine(cuisineId));

        fixture.ActAsTenant(tenant);
        await fixture.SeedAsync(
            Named(TestData.Restaurant(busy, tenant, cuisineId), "Busy", 40),
            Named(TestData.Restaurant(quiet, tenant, cuisineId), "Quiet", 10));
        await fixture.SeedAsync(
            TestData.Table(Guid.NewGuid(), busy, tenant, number: 1, seats: 2),
            TestData.Table(Guid.NewGuid(), busy, tenant, number: 2, seats: 4),
            TestData.Table(Guid.NewGuid(), quiet, tenant, number: 1, seats: 2));

        // Ordering by a plain member: the constructor must reduce away even though one of its
        // arguments is a subquery.
        var ordered = await QueryRestaurantRecordsAsync("?$orderby=name desc");
        await Assert.That(ordered.Select(r => r.Name).ToList()).IsEquivalentTo(["Quiet", "Busy"]);

        // Filtering, paging and counting over the same projection.
        var filtered = await QueryRestaurantRecordsAsync("?$filter=maxCapacity gt 20&$top=1");
        await Assert.That(filtered.Select(r => r.Name).ToList()).IsEquivalentTo(["Busy"]);

        // And the aggregate member itself, which is the one a provider cannot put in an ORDER BY
        // unless the constructor reduced first.
        var byTables = await QueryRestaurantRecordsAsync("?$orderby=tableCount desc&$top=1");
        await Assert.That(byTables.Single().Name).IsEqualTo("Busy");
        await Assert.That(byTables.Single().TableCount).IsEqualTo(2);
    }

    // ---------- Helpers ----------

    private async Task<ODataPage> QueryRestaurantsAsync(string queryString, SynaxODataOptions? settings = null)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        return await unitOfWork.GetRepository<Restaurant>()
            .QueryDto<RestaurantResponse>()
            .ToODataPageAsync(CreateRequest(queryString), settings);
    }

    /// <summary>Re-runs the query from an <c>@odata.nextLink</c>, the way a paging client would.</summary>
    private Task<ODataPage> FollowRestaurantsAsync(string nextLink, SynaxODataOptions settings)
    {
        var separator = nextLink.IndexOf('?');
        return QueryRestaurantsAsync(separator < 0 ? string.Empty : nextLink[separator..], settings);
    }

    /// <summary>The single detail row for one restaurant, as a normalized dictionary.</summary>
    private async Task<IDictionary<string, object?>> DetailRowAsync(Guid restaurantId, string queryString)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        var page = await unitOfWork.GetRepository<Restaurant>()
            .QueryDto<RestaurantDetailResponse>(q => q.Where(r => r.Id == restaurantId))
            .ToODataPageAsync(CreateRequest(queryString));

        return (IDictionary<string, object?>)page.Value.Single()!;
    }

    private static HttpRequest CreateRequest(string queryString)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/api/v1/restaurants/search";
        context.Request.QueryString =
            queryString.Length == 0 ? QueryString.Empty : new QueryString(queryString);

        return context.Request;
    }

    /// <summary>The restaurant summaries as a positional record, projected and queried.</summary>
    private async Task<List<RestaurantRecordResponse>> QueryRestaurantRecordsAsync(string queryString)
    {
        await using var scope = fixture.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork<BookingContext>>();

        var page = await unitOfWork.GetRepository<Restaurant>()
            .QueryDto<RestaurantRecordResponse>()
            .ToODataPageAsync(CreateRequest(queryString));

        return page.Value.Cast<RestaurantRecordResponse>().ToList();
    }

    private static Restaurant Named(Restaurant restaurant, string name, int maxCapacity)
    {
        restaurant.Name = name;
        restaurant.MaxCapacity = maxCapacity;
        return restaurant;
    }

    private static Restaurant Restaurant(Guid ownerId, Guid cuisineId, string name, int maxCapacity)
    {
        var restaurant = TestData.Restaurant(Guid.NewGuid(), ownerId, cuisineId);
        restaurant.Name = name;
        restaurant.MaxCapacity = maxCapacity;
        return restaurant;
    }
}
