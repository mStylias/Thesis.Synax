using Microsoft.EntityFrameworkCore;
using Synax.EntityFrameworkCore.Mapper;
using Synax.Mapper.Core;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// Covers <see cref="FakeRepository{TEntity,TDbContext}"/> query composition: EF-only operators
/// are stripped, the escape hatch works through the async provider, and projections evaluate in-memory.
/// </summary>
public class FakeRepositoryQueryTests
{
    private static FakeUnitOfWork<TestDbContext> CreateSeededUow()
        => new FakeUnitOfWork<TestDbContext>().Seed(
            new Widget { Name = "alpha", Value = 1 },
            new Widget { Name = "beta", Value = 2 },
            new Widget { Name = "gamma", Value = 3 });

    [Test]
    public async Task Query_WithWhere_FiltersSeededData()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var result = await repo.Query(q => q.Where(w => w.Value >= 2)).ToListAsync();

        await Assert.That(result.Select(w => w.Name)).IsEquivalentTo(["beta", "gamma"]);
    }

    [Test]
    public async Task Query_WithNullQuery_ReturnsWholeSet()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var result = await repo.Query().ToListAsync();

        await Assert.That(result).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Query_WithEfOnlyOperators_StripsThemAndEvaluates()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var result = await repo
            .Query(q => q
                .Include(w => w.Child)
                .AsNoTracking()
                .AsSplitQuery()
                .TagWith("unit-test")
                .Where(w => w.Value == 1))
            .SingleAsync();

        await Assert.That(result.Name).IsEqualTo("alpha");
    }

    [Test]
    public async Task ExecuteAsync_EscapeHatch_SupportsEfAsyncOperators()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        // The escape hatch hands out the raw IQueryable; EF's static async operators must work
        // because the fake provider implements IAsyncQueryProvider.
        var result = await repo
            .Query(q => q.Where(w => w.Value > 1))
            .ExecuteAsync(q => q.OrderBy(w => w.Value).ToListAsync(CancellationToken.None));

        await Assert.That(result.Select(w => w.Name)).IsEquivalentTo(["beta", "gamma"]);
    }

    [Test]
    public async Task ExecuteAsync_WithGroupBy_Evaluates()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var groups = await repo.Query()
            .ExecuteAsync(q => q
                .GroupBy(w => w.Value >= 2)
                .Select(g => new { Big = g.Key, Count = g.Count() })
                .ToListAsync(CancellationToken.None));

        await Assert.That(groups.Single(g => g.Big).Count).IsEqualTo(2);
        await Assert.That(groups.Single(g => !g.Big).Count).IsEqualTo(1);
    }

    [Test]
    public async Task QuerySelect_ProjectsWithoutMapper()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var names = await repo
            .QuerySelect(w => w.Name, q => q.Where(w => w.Value < 3))
            .ToListAsync();

        await Assert.That(names).IsEquivalentTo(["alpha", "beta"]);
    }

    [Test]
    public async Task QueryDto_WithConfiguredMapper_Projects()
    {
        var options = new FakeUnitOfWorkOptions
        {
            Mapper = new SynaxMapperRepositoryMapper(
                new MapperConfiguration(cfg => cfg.AddProfile<WidgetProfile>()).CreateMapper())
        };
        var uow = new FakeUnitOfWork<TestDbContext>(options).Seed(new Widget { Name = "alpha", Value = 1 });

        var dtos = await uow.GetRepository<Widget>().QueryDto<WidgetDto>().ToListAsync();

        await Assert.That(dtos).Count().IsEqualTo(1);
        await Assert.That(dtos[0].Name).IsEqualTo("alpha");
    }

    [Test]
    public async Task QueryDto_WithoutMapper_ThrowsDescriptively()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        Func<Task> act = () => repo.QueryDto<WidgetDto>().ToListAsync();

        await Assert.That(act)
            .Throws<InvalidOperationException>()
            .WithMessageContaining("requires a mapper");
    }

    [Test]
    public async Task AddDtoAsync_WithNullDto_ThrowsArgumentNull()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        Func<Task> act = () => repo.AddDtoAsync<WidgetDto>(null!);

        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task GetListAsync_ReturnsSeededEntities()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var result = await repo.GetListAsync();

        await Assert.That(result).Count().IsEqualTo(3);
    }

    [Test]
    public async Task AnyAndCount_NoPredicate_ReturnUnfilteredResults()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        await Assert.That(await repo.AnyAsync()).IsTrue();
        await Assert.That(await repo.CountAsync()).IsEqualTo(3);
    }

    [Test]
    public async Task AnyAndCount_ViaQuery_UsePredicates()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        await Assert.That(await repo.Query(q => q.Where(w => w.Value == 3)).AnyAsync()).IsTrue();
        await Assert.That(await repo.Query(q => q.Where(w => w.Value == 42)).AnyAsync()).IsFalse();
        await Assert.That(await repo.Query(q => q.Where(w => w.Value >= 2)).CountAsync()).IsEqualTo(2);
    }
}
