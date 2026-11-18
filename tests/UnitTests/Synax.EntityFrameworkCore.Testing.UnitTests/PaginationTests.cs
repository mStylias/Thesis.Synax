using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// Covers offset and keyset pagination on both canned queries (<see cref="SynaxQuery.From{T}(IEnumerable{T})"/>)
/// and fake-repository queries, including the CompareTo fallback for keys without comparison operators.
/// </summary>
public class PaginationTests
{
    private static FakeUnitOfWork<TestDbContext> CreateSeededUow(int count = 25)
        => new FakeUnitOfWork<TestDbContext>().Seed(Enumerable.Range(1, count)
            .Select(i => new Widget { Name = $"w{i:D2}", Value = i })
            .ToArray());

    [Test]
    public async Task ToPagedListAsync_ReturnsPageWithMetadata()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var page = await repo
            .Query(q => q.OrderBy(w => w.Value))
            .ToPagedListAsync(page: 2, pageSize: 10);

        await Assert.That(page.Items.Select(w => w.Value)).IsEquivalentTo(Enumerable.Range(11, 10));
        await Assert.That(page.TotalCount).IsEqualTo(25);
        await Assert.That(page.TotalPages).IsEqualTo(3);
        await Assert.That(page.HasPreviousPage).IsTrue();
        await Assert.That(page.HasNextPage).IsTrue();
    }

    [Test]
    public async Task ToPagedListAsync_LastPage_IsShortAndHasNoNext()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();

        var page = await repo
            .Query(q => q.OrderBy(w => w.Value))
            .ToPagedListAsync(page: 3, pageSize: 10);

        await Assert.That(page.Items).Count().IsEqualTo(5);
        await Assert.That(page.HasNextPage).IsFalse();
    }

    [Test]
    public async Task ToPagedListAsync_OnCannedQuery_Works()
    {
        var page = await SynaxQuery.From(Enumerable.Range(1, 7).ToList())
            .ToPagedListAsync(page: 4, pageSize: 2);

        await Assert.That(page.Items).IsEquivalentTo([7]);
        await Assert.That(page.TotalCount).IsEqualTo(7);
        await Assert.That(page.TotalPages).IsEqualTo(4);
    }

    [Test]
    public async Task ToPagedListAsync_WithInvalidArguments_Throws()
    {
        var query = SynaxQuery.From(new[] { 1 });
        Func<Task> invalidPage = () => query.ToPagedListAsync(0, 10);
        Func<Task> invalidSize = () => query.ToPagedListAsync(1, 0);

        await Assert.That(invalidPage).Throws<ArgumentOutOfRangeException>();
        await Assert.That(invalidSize).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ToKeysetPageAsync_WalksAllPagesWithoutSkipsOrDuplicates()
    {
        var repo = CreateSeededUow().GetRepository<Widget>();
        var seen = new List<int>();
        object? cursor = null;

        while (true)
        {
            var page = await repo.Query().ToKeysetPageAsync(w => w.Value, pageSize: 10, after: cursor);
            seen.AddRange(page.Items.Select(w => w.Value));

            if (!page.HasMore)
            {
                break;
            }

            cursor = page.NextCursor;
        }

        await Assert.That(seen).IsEquivalentTo(Enumerable.Range(1, 25));
    }

    [Test]
    public async Task ToKeysetPageAsync_Descending_PagesInReverseOrder()
    {
        var repo = CreateSeededUow(5).GetRepository<Widget>();

        var first = await repo.Query().ToKeysetPageAsync(w => w.Value, pageSize: 3, descending: true);
        var second = await repo.Query()
            .ToKeysetPageAsync(w => w.Value, pageSize: 3, after: first.NextCursor, descending: true);

        await Assert.That(first.Items.Select(w => w.Value)).IsEquivalentTo([5, 4, 3]);
        await Assert.That(first.HasMore).IsTrue();
        await Assert.That(second.Items.Select(w => w.Value)).IsEquivalentTo([2, 1]);
        await Assert.That(second.HasMore).IsFalse();
    }

    [Test]
    public async Task ToKeysetPageAsync_WithStringKey_UsesCompareToFallback()
    {
        var repo = CreateSeededUow(5).GetRepository<Widget>();

        var first = await repo.Query().ToKeysetPageAsync(w => w.Name, pageSize: 2);
        var second = await repo.Query().ToKeysetPageAsync(w => w.Name, pageSize: 2, after: first.NextCursor);

        await Assert.That(first.Items.Select(w => w.Name)).IsEquivalentTo(["w01", "w02"]);
        await Assert.That(second.Items.Select(w => w.Name)).IsEquivalentTo(["w03", "w04"]);
    }

    [Test]
    public async Task ToKeysetPageAsync_WithGuidKey_UsesCompareToFallback()
    {
        var widgets = Enumerable.Range(1, 6).Select(i => new Widget { Name = $"g{i}" }).ToArray();
        var ordered = widgets.OrderBy(w => w.Id).ToList();
        var repo = new FakeUnitOfWork<TestDbContext>().Seed(widgets).GetRepository<Widget>();

        var first = await repo.Query().ToKeysetPageAsync(w => w.Id, pageSize: 4);
        var second = await repo.Query().ToKeysetPageAsync(w => w.Id, pageSize: 4, after: first.NextCursor);

        await Assert.That(first.Items.Select(w => w.Id)).IsEquivalentTo(ordered.Take(4).Select(w => w.Id));
        await Assert.That(second.Items.Select(w => w.Id)).IsEquivalentTo(ordered.Skip(4).Select(w => w.Id));
        await Assert.That(second.HasMore).IsFalse();
    }

    [Test]
    public async Task ToKeysetPageAsync_WithWrongCursorType_ThrowsDescriptively()
    {
        var repo = CreateSeededUow(3).GetRepository<Widget>();

        Func<Task> act = () => repo.Query().ToKeysetPageAsync(w => w.Value, pageSize: 2, after: "not-an-int");

        await Assert.That(act)
            .Throws<ArgumentException>()
            .WithMessageContaining("NextCursor");
    }

    [Test]
    public async Task ToKeysetPageAsync_EmptySource_ReturnsEmptyPage()
    {
        var repo = new FakeUnitOfWork<TestDbContext>().GetRepository<Widget>();

        var page = await repo.Query().ToKeysetPageAsync(w => w.Value, pageSize: 10);

        await Assert.That(page.Items).IsEmpty();
        await Assert.That(page.HasMore).IsFalse();
    }
}
