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
        var registry = new TagRegistry();
        var set = new TagSet(registry);
        var ghost = registry.GetOrRegister("State.Dead.Ghost");
        var dead = registry.GetOrRegister("State.Dead");
        set.Add(ghost, 2);

        // 워밍업
        _ = set.Has(dead) && set.Count(dead) > 0 && registry.GetName(ghost).Length > 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            _ = set.Has(dead);
            _ = set.Count(dead);
            _ = set.HasExact(ghost);
            _ = registry.GetName(ghost);
            _ = registry.IsAncestorOrSelf(dead, ghost);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, "태그 쿼리 경로에서 힙 할당 발생");
    }
}
