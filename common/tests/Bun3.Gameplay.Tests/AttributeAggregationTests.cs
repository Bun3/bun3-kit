#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeAggregationTests
{
    private const ushort Attack = 5;

    private sealed class FakeSource : IAttributeModifierSource
    {
        public ulong Id { get; set; }
        public int Stack { get; set; } = 1;
        public bool Enabled { get; set; } = true;
    }

    private static AttributeSet CreateSet()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(Attack, min: Operand.Constant(0));
        Span<ushort> ids = stackalloc ushort[] { Attack };
        return new AttributeSet(builder.Build(), ids);
    }

    [Test]
    public void Formula_applies_add_then_summed_multiply()
    {
        var set = CreateSet();
        set.SetBase(Attack, 100);
        var buff = new FakeSource { Id = 1 };
        set.AttachModifier(buff, 0, Attack, AttributeModifierOp.Add, 20, scaleWithStack: false);
        set.AttachModifier(buff, 1, Attack, AttributeModifierOp.Multiply, BigNum.FromParts(3, -1), scaleWithStack: false); // +30%
        var other = new FakeSource { Id = 2 };
        set.AttachModifier(other, 0, Attack, AttributeModifierOp.Multiply, BigNum.FromParts(2, -1), scaleWithStack: false); // +20%
        set.RebuildDirty();

        // (100 + 20) × (1 + 0.3 + 0.2) = 180 — aggregation formula
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)180));
    }

    [Test]
    public void Detach_restores_the_exact_previous_current()
    {
        var set = CreateSet();
        set.SetBase(Attack, 100);
        var before = set.GetCurrent(Attack);
        var buff = new FakeSource { Id = 7 };
        set.AttachModifier(buff, 0, Attack, AttributeModifierOp.Multiply, BigNum.FromParts(37, -2), scaleWithStack: false);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.Not.EqualTo(before));

        set.DetachModifiers(buff);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo(before));   // no residue
    }

    [Test]
    public void Latest_override_wins_and_disabled_or_stacked_entries_behave()
    {
        var set = CreateSet();
        set.SetBase(Attack, 100);
        var early = new FakeSource { Id = 1 };
        var late = new FakeSource { Id = 9 };
        set.AttachModifier(late, 0, Attack, AttributeModifierOp.Override, 55, scaleWithStack: false);
        set.AttachModifier(early, 0, Attack, AttributeModifierOp.Override, 77, scaleWithStack: false);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)55));   // highest id wins

        var stacked = new FakeSource { Id = 3, Stack = 4 };
        set.DetachModifiers(late);
        set.DetachModifiers(early);
        set.AttachModifier(stacked, 0, Attack, AttributeModifierOp.Add, 10, scaleWithStack: true);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)140)); // 100 + 10×4

        stacked.Enabled = false;
        set.MarkDirty(Attack);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)100)); // inactive = skipped
    }

    [Test]
    public void Aggregation_is_bit_identical_regardless_of_attach_order()
    {
        // Canonical-order oracle — non-trivial because BigNum truncation is non-associative.
        var random = new Random(20260817);
        for (var round = 0; round < 200; round++)
        {
            var entries = new List<(ulong Id, AttributeModifierOp Op, BigNum Magnitude)>();
            var count = random.Next(2, 12);
            for (var i = 0; i < count; i++)
            {
                var op = (AttributeModifierOp)random.Next(0, 2);   // Add | Multiply
                var mantissa = (long)random.Next(1, 1_000_000_000) * (random.Next(2) == 0 ? 1 : -1);
                entries.Add(((ulong)(i + 1), op, BigNum.FromParts(mantissa, random.Next(-6, 7))));
            }

            BigNum Aggregate(IEnumerable<int> order)
            {
                var set = CreateSet();
                set.SetBase(Attack, BigNum.FromParts(987_654_321_987_654_321, -3));
                foreach (var index in order)
                {
                    var entry = entries[index];
                    set.AttachModifier(new FakeSource { Id = entry.Id }, 0, Attack, entry.Op, entry.Magnitude, false);
                }
                set.RebuildDirty();
                return set.GetCurrent(Attack);
            }

            var forward = new List<int>();
            for (var i = 0; i < count; i++) forward.Add(i);
            var shuffled = new List<int>(forward);
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            Assert.That(Aggregate(shuffled), Is.EqualTo(Aggregate(forward)),
                $"round {round}: application order changed the result.");
        }
    }
}
