using Synax.EntityFrameworkCore.Core;

namespace Synax.EntityFrameworkCore.Testing.UnitTests;

/// <summary>
/// Covers the public canned-query factory <see cref="SynaxQuery.From{T}(IEnumerable{T})"/> —
/// materializers must evaluate with LINQ to Objects, no EF provider involved.
/// </summary>
public class SynaxQueryFromTests
{
    private static readonly int[] Numbers = [3, 1, 4, 1, 5];

    [Test]
    public async Task ToListAsync_OnCannedSource_ReturnsAllElements()
    {
        var result = await SynaxQuery.From(Numbers).ToListAsync();

        await Assert.That(result).IsEquivalentTo(Numbers);
    }

    [Test]
    public async Task FirstOrDefaultAsync_WithPredicate_ReturnsMatch()
    {
        var result = await SynaxQuery.From(Numbers).FirstOrDefaultAsync(n => n > 3);

        await Assert.That(result).IsEqualTo(4);
    }

    [Test]
    public async Task FirstOrDefaultAsync_WhenEmpty_ReturnsDefault()
    {
        var result = await SynaxQuery.From(Array.Empty<Widget>()).FirstOrDefaultAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task SingleAsync_WithMultipleMatches_Throws()
    {
        await Assert.That(() => SynaxQuery.From(Numbers).SingleAsync(n => n == 1))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CountAndAggregates_EvaluateSynchronously()
    {
        var query = SynaxQuery.From(Numbers);

        await Assert.That(await query.CountAsync()).IsEqualTo(5);
        await Assert.That(await query.CountAsync(n => n == 1)).IsEqualTo(2);
        await Assert.That(await query.AnyAsync(n => n == 5)).IsTrue();
        await Assert.That(await query.AllAsync(n => n > 0)).IsTrue();
        await Assert.That(await query.SumAsync(n => n)).IsEqualTo(14);
        await Assert.That(await query.MinAsync()).IsEqualTo(1);
        await Assert.That(await query.MaxAsync()).IsEqualTo(5);
        await Assert.That(await query.AverageAsync(n => (double)n)).IsEqualTo(2.8);
    }

    [Test]
    public async Task ToDictionaryAsync_KeysBySelector()
    {
        var widgets = new[]
        {
            new Widget { Name = "a", Value = 1 },
            new Widget { Name = "b", Value = 2 }
        };

        var result = await SynaxQuery.From(widgets).ToDictionaryAsync(w => w.Name, w => w.Value);

        await Assert.That(result["a"]).IsEqualTo(1);
        await Assert.That(result["b"]).IsEqualTo(2);
    }

    [Test]
    public async Task AsAsyncEnumerable_StreamsAllElements()
    {
        var collected = new List<int>();

        await foreach (var n in SynaxQuery.From(Numbers).AsAsyncEnumerable())
        {
            collected.Add(n);
        }

        await Assert.That(collected).IsEquivalentTo(Numbers);
    }

    [Test]
    public async Task ForEachAsync_InvokesActionPerElement()
    {
        var sum = 0;

        await SynaxQuery.From(Numbers).ForEachAsync(n => sum += n);

        await Assert.That(sum).IsEqualTo(14);
    }

    [Test]
    public async Task ExecuteAsync_ExposesRawQueryable()
    {
        var result = await SynaxQuery.From(Numbers)
            .ExecuteAsync(q => Task.FromResult(q.OrderByDescending(n => n).First()));

        await Assert.That(result).IsEqualTo(5);
    }
}
