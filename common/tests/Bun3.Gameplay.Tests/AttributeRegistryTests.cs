#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeRegistryTests
{
    [Test]
    public void Attributes_are_named_and_found_by_name()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(Hp, "Hp", min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        builder.Register(MaxHp, "MaxHp");
        var registry = builder.Build();

        Assert.That(registry.GetName(Hp), Is.EqualTo("Hp"));
        Assert.That(registry.TryGetId("MaxHp", out var id), Is.True);
        Assert.That(id, Is.EqualTo(MaxHp));
        Assert.That(registry.TryGetId("Nope", out _), Is.False);
    }

    [Test]
    public void An_empty_attribute_name_is_rejected_at_registration()
    {
        var builder = new AttributeRegistryBuilder();
        Assert.That(() => builder.Register(Hp, ""), Throws.ArgumentException);
    }

    [Test]
    public void A_duplicate_attribute_name_is_a_build_error_naming_both_ids()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(Hp, "Health");
        builder.Register(MaxHp, "Health");

        Assert.That(
            () => builder.Build(),
            Throws.InvalidOperationException.With.Message.Contains("Health")
                .And.Message.Contains(Hp.ToString()).And.Message.Contains(MaxHp.ToString()));
    }

    private const ushort Hp = 1;
    private const ushort MaxHp = 2;
    private const ushort Mp = 3;
    private const ushort MaxMp = 4;

    [Test]
    public void Registration_order_does_not_matter_and_forward_references_are_allowed()
    {
        var forward = new AttributeRegistryBuilder();
        forward.Register(Hp, "Hp", min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        forward.Register(MaxHp, "MaxHp", min: Operand.Constant(1));

        var backward = new AttributeRegistryBuilder();
        backward.Register(MaxHp, "MaxHp", min: Operand.Constant(1));
        backward.Register(Hp, "Hp", min: Operand.Constant(0), max: Operand.Attribute(MaxHp));

        var a = forward.Build();
        var b = backward.Build();
        Assert.That(a.EvaluationOrder.ToArray(), Is.EqualTo(b.EvaluationOrder.ToArray()));
        Assert.That(a.GetClampDependents(MaxHp).ToArray(), Is.EqualTo(new[] { Hp }));
    }

    [Test]
    public void Evaluation_order_puts_referenced_attributes_first_with_id_tiebreak()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(Hp, "Hp", max: Operand.Attribute(MaxHp));
        builder.Register(Mp, "Mp", max: Operand.Attribute(MaxMp));
        builder.Register(MaxMp, "MaxMp");
        builder.Register(MaxHp, "MaxHp");
        var registry = builder.Build();

        var order = registry.EvaluationOrder.ToArray();
        Assert.That(Array.IndexOf(order, MaxHp), Is.LessThan(Array.IndexOf(order, Hp)));
        Assert.That(Array.IndexOf(order, MaxMp), Is.LessThan(Array.IndexOf(order, Mp)));
        // independent elements sort by ascending id
        Assert.That(order, Is.EqualTo(new ushort[] { MaxHp, MaxMp, Hp, Mp }).Or.EqualTo(new ushort[] { MaxMp, MaxHp, Hp, Mp }));
        Assert.That(order[0], Is.EqualTo(MaxHp));   // same-level tiebreak = ascending id, so 2 < 4
    }

    [Test]
    public void Build_rejects_missing_reference_cycle_and_meaningless_policy()
    {
        var missing = new AttributeRegistryBuilder();
        missing.Register(Hp, "Hp", max: Operand.Attribute(MaxHp));   // MaxHp not registered
        Assert.Throws<InvalidOperationException>(() => missing.Build());

        var cyclic = new AttributeRegistryBuilder();
        cyclic.Register(Hp, "Hp", max: Operand.Attribute(MaxHp));
        cyclic.Register(MaxHp, "MaxHp", max: Operand.Attribute(Hp));
        Assert.Throws<InvalidOperationException>(() => cyclic.Build());

        var meaningless = new AttributeRegistryBuilder();
        meaningless.Register(Hp, "Hp", max: Operand.Constant(100), onMaxIncrease: MaxIncreasePolicy.Follow);
        var ex = Assert.Throws<InvalidOperationException>(() => meaningless.Build());   // max is not an attribute reference
        Assert.That(ex!.Message, Does.Contain("'Hp'"));

        var duplicated = new AttributeRegistryBuilder();
        duplicated.Register(Hp, "Hp");
        Assert.Throws<InvalidOperationException>(() => duplicated.Register(Hp, "Hp"));

        var frozen = new AttributeRegistryBuilder();
        frozen.Register(Hp, "Hp");
        frozen.Build();
        Assert.Throws<InvalidOperationException>(() => frozen.Register(Mp, "Mp"));
    }

    [Test]
    public void Build_rejects_source_attribute_operand_in_clamp_bounds()
    {
        var sourceMinBound = new AttributeRegistryBuilder();
        sourceMinBound.Register(Hp, "Hp", min: Operand.SourceAttribute(MaxHp));
        Assert.Throws<InvalidOperationException>(() => sourceMinBound.Build(),
            "min bound with SourceAttribute should be rejected");

        var sourceMaxBound = new AttributeRegistryBuilder();
        sourceMaxBound.Register(Hp, "Hp", max: Operand.SourceAttribute(MaxHp));
        Assert.Throws<InvalidOperationException>(() => sourceMaxBound.Build(),
            "max bound with SourceAttribute should be rejected");
    }

    [Test]
    public void Evaluation_order_is_level_based_not_greedy_min()
    {
        // Distinguishes level-based Kahn from greedy-min:
        // attribute 1 (independent), 2 (refs 1), 5 (independent)
        // level-based: [1, 5, 2] (level 0: 1,5; level 1: 2)
        // greedy-min: [1, 2, 5] (picks the minimum each time)
        var builder = new AttributeRegistryBuilder();
        builder.Register(1, "A1");           // independent
        builder.Register(2, "A2", max: Operand.Attribute(1));  // refs 1
        builder.Register(5, "A5");           // independent
        var registry = builder.Build();

        Assert.That(registry.EvaluationOrder.ToArray(), Is.EqualTo(new ushort[] { 1, 5, 2 }));
    }

    [Test]
    public void Signature_depends_on_the_shape_not_on_registration_order()
    {
        static AttributeRegistry Build(bool reversed, bool differentBound, bool differentPolicy)
        {
            var builder = new AttributeRegistryBuilder();
            void RegisterMax() => builder.Register(
                2, "A2",
                min: Operand.Constant(differentBound ? 1 : 0),
                max: Operand.Constant(100));
            void RegisterHp() => builder.Register(
                1, "A1",
                min: Operand.Constant(0),
                max: Operand.Attribute(2),
                onMaxIncrease: differentPolicy ? MaxIncreasePolicy.Follow : MaxIncreasePolicy.Stay);

            if (reversed)
            {
                RegisterHp();
                RegisterMax();
            }
            else
            {
                RegisterMax();
                RegisterHp();
            }

            return builder.Build();
        }

        var baseline = Build(false, false, false).Signature;
        Assert.That(Build(true, false, false).Signature, Is.EqualTo(baseline));
        Assert.That(Build(false, true, false).Signature, Is.Not.EqualTo(baseline));
        Assert.That(Build(false, false, true).Signature, Is.Not.EqualTo(baseline));
    }
}
