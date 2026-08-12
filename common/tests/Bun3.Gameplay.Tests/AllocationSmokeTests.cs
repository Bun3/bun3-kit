using System;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class AllocationSmokeTests
{
    [Test]
    public void BigNum_ops_and_format_do_not_allocate()
    {
        var a = BigNum.FromParts(37, 28);
        var b = BigNum.FromParts(15, -1);
        Span<char> buffer = stackalloc char[64];

        // 워밍업 (JIT/정적 초기화 할당 배제)
        var warm = a * b + a - b / 3;
        warm.TryFormat(buffer, out _, BigNumFormat.Korean);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            var x = a * b + a - b / 3;
            x.TryFormat(buffer, out _, BigNumFormat.Korean);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, "BigNum 연산/포맷 경로에서 힙 할당 발생");
    }

    [Test]
    public void Tag_queries_do_not_allocate()
    {
        var catalog = TagCatalogTestData.Load();
        var ghost = catalog.GetRequired("State.Dead.Ghost");
        var dead = catalog.GetRequired("State.Dead");
        var set = catalog.CreateContainer(8);
        var counts = catalog.CreateCountContainer(8);
        set.Add(ghost);
        counts.Add(ghost, 2);

        _ = set.Has(dead);
        _ = counts.Has(dead);
        _ = counts.Count(dead);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var hits = 0;
        for (var i = 0; i < 100_000; i++)
        {
            if (set.Has(dead)) hits++;
            if (counts.Has(dead)) hits++;
            hits += counts.Count(dead);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(hits, Is.EqualTo(400_000));
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void Reserved_tag_mutations_do_not_allocate()
    {
        var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildChainCatalog(8, 16, false));
        var leaves = new GameplayTag[8];
        for (var i = 0; i < leaves.Length; i++)
            leaves[i] = catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, 16));
        var tags = catalog.CreateContainer(8);
        var counts = catalog.CreateCountContainer(8);

        RunCycles(tags, counts, leaves, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        RunCycles(tags, counts, leaves, 100);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(tags.ExactKindCount, Is.Zero);
        Assert.That(counts.ExactKindCount, Is.Zero);
        Assert.That(allocated, Is.Zero);
    }

    private static void RunCycles(
        TagContainer tags,
        TagCountContainer counts,
        GameplayTag[] leaves,
        int cycles)
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            for (var i = 0; i < leaves.Length; i++)
            {
                tags.Add(leaves[i]);
                counts.Add(leaves[i]);
            }
            for (var i = 0; i < leaves.Length; i++)
            {
                tags.Remove(leaves[i]);
                counts.Remove(leaves[i]);
            }
        }
    }
}
